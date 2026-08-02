using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using DotNetMcpServer.Agent.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// The hosted service owns the MCP connection and the process lifetime, so these tests launch
/// the real server subprocess. The model is never reached — the session ends on input, not on
/// an answer — which is asserted by giving it a transport that throws if it is called.
/// </summary>
public sealed class AgentHostedServiceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "agent-host-" + Guid.NewGuid().ToString("N"));

    public AgentHostedServiceTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        /// <summary>Completes the first time the service asks the host to shut down.</summary>
        public Task StopRequested => _stopRequested.Task;

        public void StopApplication()
        {
            _stopRequested.TrySetResult();
        }

        public void Dispose() => _stopping.Dispose();
    }

    private AgentHostedService BuildService(ServiceProvider provider, IUserInput userInput, FakeLifetime lifetime)
    {
        var runner = new InteractiveAgentRunner(
            Options.Create(new AgentRuntimeSettings()),
            provider.GetRequiredService<OpenAiChatClient>(),
            userInput,
            NullLogger<InteractiveAgentRunner>.Instance);

        var settings = Options.Create(new McpSettings
        {
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            WorkingDirectory = _workspace,
            WorkspaceRoot = _workspace
        });

        return new AgentHostedService(runner, settings, lifetime, NullLogger<AgentHostedService>.Instance);
    }

    [Fact]
    public async Task Starting_connects_to_the_server_and_a_finished_session_stops_the_host()
    {
        using var model = new AgentTestHost.UnusedModel();
        using var provider = AgentTestHost.WithModel(model);
        using var lifetime = new FakeLifetime();

        // End of input, so the session runs its course without the user typing anything.
        var service = BuildService(provider, new ConsoleUserInput(TextReader.Null), lifetime);

        await using (service)
        {
            await service.StartAsync(CancellationToken.None);

            await lifetime.StopRequested.WaitAsync(TimeSpan.FromSeconds(30));

            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The case F2-06 was about: the session is parked on a console read when shutdown starts.
    /// Before the read observed cancellation, <c>StopAsync</c> could not wait for the session at
    /// all — it would have hung here forever.
    /// </summary>
    [Fact]
    public async Task Stopping_unblocks_a_session_parked_on_user_input()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        using var reader = new AgentTestHost.BlockingReader(release);
        using var model = new AgentTestHost.UnusedModel();
        using var provider = AgentTestHost.WithModel(model);
        using var lifetime = new FakeLifetime();

        var service = BuildService(provider, new ConsoleUserInput(reader), lifetime);

        await using (service)
        {
            await service.StartAsync(CancellationToken.None);

            // Let the session reach the prompt and block there.
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.False(lifetime.StopRequested.IsCompleted);

            using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await service.StopAsync(shutdownDeadline.Token);

            // The session unwound rather than being abandoned: its finally block ran.
            Assert.True(lifetime.StopRequested.IsCompleted);
        }

        release.Set();
    }
}
