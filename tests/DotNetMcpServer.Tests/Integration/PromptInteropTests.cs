using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives <c>prompts/list</c> and <c>prompts/get</c> against the shipped server as a real
/// subprocess.
/// </summary>
public sealed class PromptInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-prompts-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "docs"));
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "docs", "design.md"),
            "# Design\nThe transport is newline-delimited JSON.\n");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        _client = await McpClient.CreateAsync(transport);
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

    [Fact]
    public void Server_advertises_the_prompts_capability()
    {
        Assert.NotNull(Client.ServerCapabilities.Prompts);
    }

    [Fact]
    public async Task ListPrompts_describes_every_prompt_and_its_arguments()
    {
        var prompts = await Client.ListPromptsAsync();

        Assert.Equal(
            ["study_plan", "summarize_document"],
            prompts.Select(prompt => prompt.Name).OrderBy(name => name, StringComparer.Ordinal));

        var summarize = prompts.Single(prompt => prompt.Name == "summarize_document").ProtocolPrompt;
        Assert.NotNull(summarize.Arguments);

        var path = summarize.Arguments.Single(argument => argument.Name == "path");
        var audience = summarize.Arguments.Single(argument => argument.Name == "audience");

        Assert.True(path.Required);
        Assert.NotEqual(true, audience.Required);
        Assert.All(summarize.Arguments, argument => Assert.False(string.IsNullOrWhiteSpace(argument.Description)));
    }

    [Fact]
    public async Task GetPrompt_attaches_the_document_it_asks_about()
    {
        var result = await Client.GetPromptAsync(
            "summarize_document",
            new Dictionary<string, object?> { ["path"] = "docs/design.md" });

        Assert.Equal(2, result.Messages.Count);
        Assert.All(result.Messages, message => Assert.Equal(Role.User, message.Role));

        var instruction = Assert.IsType<TextContentBlock>(result.Messages[0].Content);
        Assert.Contains("engineer", instruction.Text, StringComparison.Ordinal);

        var embedded = Assert.IsType<EmbeddedResourceBlock>(result.Messages[1].Content);
        var document = Assert.IsType<TextResourceContents>(embedded.Resource);
        Assert.Equal("workspace://file/docs/design.md", document.Uri);
        Assert.Contains("newline-delimited JSON", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPrompt_honours_an_optional_argument()
    {
        var result = await Client.GetPromptAsync(
            "summarize_document",
            new Dictionary<string, object?> { ["path"] = "docs/design.md", ["audience"] = "newcomer" });

        var instruction = Assert.IsType<TextContentBlock>(result.Messages[0].Content);
        Assert.Contains("first time", instruction.Text, StringComparison.Ordinal);
        Assert.Contains("newcomer", result.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPrompt_renders_a_plan_from_its_arguments()
    {
        var result = await Client.GetPromptAsync(
            "study_plan",
            new Dictionary<string, object?> { ["topic"] = "resource templates", ["hoursPerWeek"] = 8 });

        var instruction = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content);
        Assert.Contains("resource templates", instruction.Text, StringComparison.Ordinal);
        Assert.Contains("8 hours a week", instruction.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPrompt_rejects_an_argument_outside_its_range()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.GetPromptAsync(
                "study_plan",
                new Dictionary<string, object?> { ["topic"] = "anything", ["hoursPerWeek"] = 500 }).AsTask());
    }

    [Fact]
    public async Task GetPrompt_refuses_a_document_outside_the_workspace()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.GetPromptAsync(
                "summarize_document",
                new Dictionary<string, object?> { ["path"] = "../../etc/passwd" }).AsTask());
    }

    [Fact]
    public async Task GetPrompt_rejects_an_unknown_prompt()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.GetPromptAsync("no_such_prompt").AsTask());
    }
}
