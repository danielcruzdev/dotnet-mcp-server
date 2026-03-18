using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Shared.JsonRpc;

namespace DotNetMcpServer.Server;

internal static class Program
{
    private const string ServerName = "dotnet-mcp-server";
    private const string ServerVersion = "1.0.0";
    private const string DefaultProtocolVersion = "2025-03-26";

    public static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var workspaceRoot = ResolveWorkspaceRoot(args);
        Console.Error.WriteLine($"[{ServerName}] Workspace root: {workspaceRoot}");

        var registry = new ToolRegistry(new IMcpTool[]
        {
            new GetCurrentDateTimeTool(),
            new CalculateExpressionTool(),
            new ReadTextFileTool(workspaceRoot),
            new AppendStudyNoteTool(workspaceRoot)
        });

        await using var rpc = new JsonRpcStream(Console.OpenStandardInput(), Console.OpenStandardOutput());
        var host = new McpServerHost(rpc, registry, ServerName, ServerVersion, DefaultProtocolVersion);
        await host.RunAsync(cts.Token);
    }

    private static string ResolveWorkspaceRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--workspace-root", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var environmentOverride = Environment.GetEnvironmentVariable("MCP_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return Path.GetFullPath(environmentOverride);
        }

        return Directory.GetCurrentDirectory();
    }
}



