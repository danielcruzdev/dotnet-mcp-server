using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using DotNetMcpServer.Agent.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the agent's turn loop against a real MCP server subprocess with only the model
/// stubbed out. The MCP client cannot be faked — <c>McpClient</c> is abstract with non-virtual
/// methods — and there is no reason to want to: the tool calls in these tests really execute.
/// </summary>
public sealed class AgentTurnTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "agent-turn-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;
    private IList<McpClientTool> _tools = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        _client = await McpClient.CreateAsync(transport);
        _tools = await _client.ListToolsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private McpClient Client => _client ?? throw new InvalidOperationException("Client was not initialised.");

    /// <summary>
    /// Replays scripted completions in order; the last one repeats, so "the model never stops
    /// calling tools" needs no guess at how many rounds the runner will take.
    /// </summary>
    private sealed class ScriptedModel : HttpMessageHandler
    {
        private readonly string[] _bodies;
        private int _calls;

        public ScriptedModel(params string[] bodies)
        {
            _bodies = bodies;
        }

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _calls) - 1;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_bodies[Math.Min(index, _bodies.Length - 1)], Encoding.UTF8, "application/json")
            });
        }
    }

    private static string ToolCall(string narration, string toolName) =>
        $$$"""{"choices":[{"message":{"role":"assistant","content":"{{{narration}}}","tool_calls":[{"id":"call_1","type":"function","function":{"name":"{{{toolName}}}","arguments":"{}"}}]}}]}""";

    private static string FinalAnswer(string content) =>
        $$$"""{"choices":[{"message":{"role":"assistant","content":"{{{content}}}"}}]}""";

    private static ServiceProvider BuildProvider(HttpMessageHandler model)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<OpenAiSettings>>(Options.Create(new OpenAiSettings
        {
            ApiKey = "sk-test-key",
            BaseUrl = "https://openai.invalid/v1"
        }));

        services.AddOpenAiChatClient().ConfigurePrimaryHttpMessageHandler(() => model);

        return services.BuildServiceProvider();
    }

    private static InteractiveAgentRunner BuildRunner(ServiceProvider provider, int maxToolIterations) =>
        new(
            Options.Create(new AgentRuntimeSettings { MaxToolIterations = maxToolIterations }),
            provider.GetRequiredService<OpenAiChatClient>(),
            new ConsoleUserInput(TextReader.Null),
            NullLogger<InteractiveAgentRunner>.Instance);

    private static List<JsonObject> NewConversation(string question) =>
    [
        new JsonObject { ["role"] = "system", ["content"] = "You are a test." },
        new JsonObject { ["role"] = "user", ["content"] = question }
    ];

    /// <summary>
    /// The turn used to throw here, and the exception killed the whole session — one stubborn
    /// turn cost the user the conversation.
    /// </summary>
    [Fact]
    public async Task A_turn_that_never_stops_calling_tools_degrades_instead_of_ending_the_session()
    {
        using var model = new ScriptedModel(ToolCall("Checking the clock.", "get_current_datetime"));
        using var provider = BuildProvider(model);

        var runner = BuildRunner(provider, maxToolIterations: 2);
        var messages = NewConversation("what time is it?");

        var answer = await runner.CompleteTurnAsync(Client, messages, _tools, CancellationToken.None);

        Assert.Contains("Stopped after 2 rounds", answer, StringComparison.Ordinal);

        // The partial answer is the narration the model produced while it was still working.
        Assert.Contains("Checking the clock.", answer, StringComparison.Ordinal);

        Assert.Equal(2, model.Calls);

        // The conversation stays usable: history ends on the assistant, so the next turn is valid.
        Assert.Equal("assistant", messages[^1]["role"]?.GetValue<string>());
    }

    [Fact]
    public async Task A_turn_that_reaches_an_answer_returns_it_with_the_tool_result_in_history()
    {
        using var model = new ScriptedModel(
            ToolCall("Let me look.", "get_current_datetime"),
            FinalAnswer("It is recorded above."));
        using var provider = BuildProvider(model);

        var runner = BuildRunner(provider, maxToolIterations: 6);
        var messages = NewConversation("what time is it?");

        var answer = await runner.CompleteTurnAsync(Client, messages, _tools, CancellationToken.None);

        Assert.Equal("It is recorded above.", answer);
        Assert.Equal(2, model.Calls);

        // The tool really ran against the server subprocess, and its output reached the model.
        var toolMessage = messages.Single(message => message["role"]?.GetValue<string>() == "tool");
        Assert.Equal("get_current_datetime", toolMessage["name"]?.GetValue<string>());
        Assert.Contains("UTC now:", toolMessage["content"]?.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_exhausted_turn_still_says_something_when_the_model_narrated_nothing()
    {
        var notice = InteractiveAgentRunner.FormatExhaustedTurn([], maxToolIterations: 4);

        Assert.Contains("Stopped after 4 rounds", notice, StringComparison.Ordinal);
    }
}
