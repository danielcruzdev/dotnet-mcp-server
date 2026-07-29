using System.Globalization;
using System.Text.Json.Nodes;
using Mcp.Protocol.Handwritten.Mcp;

namespace Mcp.Protocol.Handwritten.Server;

/// <summary>
/// Minimal tools for demonstrating the hand-written protocol end to end.
/// </summary>
/// <remarks>
/// These exist so the artifact can be driven by a real MCP client without depending on the
/// shipped server. They are deliberately trivial: this project's useful tools live in
/// <c>DotNetMcpServer.Server</c>, and duplicating them here would give the artifact a reason
/// to keep growing — which its frozen scope rules out.
/// </remarks>
public sealed class EchoTool : IMcpTool
{
    public McpToolDefinition Definition => new()
    {
        Name = "echo",
        Description = "Returns the supplied message unchanged.",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Text to echo back."
                }
            },
            ["required"] = new JsonArray("message")
        }
    };

    public Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var message = arguments.GetString("message");

        return Task.FromResult(string.IsNullOrWhiteSpace(message)
            ? McpToolCallResult.Fail("'message' is required.")
            : McpToolCallResult.Success(message));
    }
}

/// <summary>
/// Adds two numbers. Present so the conformance suite can assert on a typed, non-string
/// argument surviving the round trip.
/// </summary>
public sealed class AddTool : IMcpTool
{
    public McpToolDefinition Definition => new()
    {
        Name = "add",
        Description = "Adds two numbers and returns the sum.",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["a"] = new JsonObject { ["type"] = "number", ["description"] = "First addend." },
                ["b"] = new JsonObject { ["type"] = "number", ["description"] = "Second addend." }
            },
            ["required"] = new JsonArray("a", "b")
        }
    };

    public Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var a = arguments["a"]?.GetValue<double>();
        var b = arguments["b"]?.GetValue<double>();

        if (a is null || b is null)
        {
            return Task.FromResult(McpToolCallResult.Fail("'a' and 'b' are both required."));
        }

        var sum = (a.Value + b.Value).ToString(CultureInfo.InvariantCulture);

        return Task.FromResult(McpToolCallResult.Success($"Sum: {sum}"));
    }
}
