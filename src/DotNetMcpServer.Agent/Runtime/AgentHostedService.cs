using DotNetMcpServer.Agent.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace DotNetMcpServer.Agent.Runtime;

/// <summary>
/// Owns the MCP connection and the interactive session for the lifetime of the host.
/// </summary>
/// <remarks>
/// The client is created here rather than in the container because
/// <see cref="McpClient.CreateAsync"/> is asynchronous, and constructor injection is not.
/// </remarks>
internal sealed partial class AgentHostedService : IHostedService, IAsyncDisposable
{
    private readonly InteractiveAgentRunner _runner;
    private readonly McpSettings _mcpSettings;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AgentHostedService> _logger;
    private readonly CancellationTokenSource _stopping = new();

    private McpClient? _mcpClient;
    private Task? _session;

    public AgentHostedService(
        InteractiveAgentRunner runner,
        IOptions<McpSettings> mcpSettings,
        IHostApplicationLifetime lifetime,
        ILogger<AgentHostedService> logger)
    {
        _runner = runner;
        _mcpSettings = mcpSettings.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The server is launched as a compiled binary, never through `dotnet run`: MSBuild
        // writes to stdout, and stdout is the protocol channel.
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = _mcpSettings.Command,
            Arguments = _mcpSettings.ArgumentList,
            WorkingDirectory = _mcpSettings.WorkingDirectory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["MCP_WORKSPACE_ROOT"] = _mcpSettings.WorkspaceRoot
            }
        });

        LogConnecting(_mcpSettings.Command);
        _mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        // Started, not awaited: StartAsync must return for the host to finish starting.
        _session = RunSessionAsync();
    }

    private async Task RunSessionAsync()
    {
        try
        {
            await _runner.RunAsync(_mcpClient!, _stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested; not an error.
        }
        catch (Exception exception)
        {
            LogSessionFailed(exception);
            Environment.ExitCode = 1;
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    /// <remarks>
    /// The session task is deliberately not awaited here: it may be parked on a blocking
    /// <see cref="Console.ReadLine"/>, which does not observe cancellation. Making console
    /// input cancellable is F2-06.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to the MCP server at {Command}")]
    private partial void LogConnecting(string command);

    [LoggerMessage(Level = LogLevel.Error, Message = "The agent session ended with an error")]
    private partial void LogSessionFailed(Exception exception);

    public async ValueTask DisposeAsync()
    {
        _stopping.Dispose();

        if (_mcpClient is not null)
        {
            await _mcpClient.DisposeAsync();
        }
    }
}
