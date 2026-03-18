using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server.Tools;

public interface IMcpTool
{
    McpToolDefinition Definition { get; }

    Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken);
}

