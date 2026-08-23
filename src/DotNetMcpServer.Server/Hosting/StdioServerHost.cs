using DotNetMcpServer.Server.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// The local transport: newline-delimited JSON over stdin and stdout, launched as a
/// subprocess by the client.
/// </summary>
internal static class StdioServerHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // stdout carries the MCP protocol stream and nothing else. Every log line goes to
        // stderr, otherwise a single log write corrupts the session for the client.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        // The client, not this configuration, decides how verbose the log stream it receives
        // is, so every level has to reach the bridge for logging/setLevel to have anything to
        // filter. The console keeps its own default and is unaffected.
        builder.Logging.AddFilter<ClientLogBridge>(category: null, level: LogLevel.Trace);

        builder.Services.AddSingleton<ClientLogBridge>();
        builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<ClientLogBridge>());

        var mcpServer = builder.Services
            .AddWorkspaceMcpServer(args)
            .WithStdioServerTransport();

        // Registered on its own so the suppression covers this one call and nothing else.
#pragma warning disable MCP9005 // Logging is deprecated by 2026-07-28 (SEP-2577); see ClientLogBridge.
        mcpServer.WithSetLoggingLevelHandler(ClientLogBridge.AttachOnSetLevelAsync);
#pragma warning restore MCP9005

        await builder.Build().RunAsync();
    }
}
