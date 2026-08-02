using System.Text.Json.Nodes;
using Mcp.Protocol.Handwritten.Mcp;

namespace Mcp.Protocol.Handwritten.Server;

public interface IMcpTool
{
    McpToolDefinition Definition { get; }

    Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken);
}

