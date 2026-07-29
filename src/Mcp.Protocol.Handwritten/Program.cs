using Mcp.Protocol.Handwritten.JsonRpc;
using Mcp.Protocol.Handwritten.Server;

// Entry point for the hand-written MCP server.
//
// This binary is not the product -- DotNetMcpServer.Server is, and it is built on the official
// SDK. This exists so the hand-written implementation can be launched by a real MCP client and
// proven to interoperate, which is what turns "I implemented the protocol" from a claim into
// evidence. See docs/adr/0001-official-sdk-with-handwritten-artifact.md.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

// stdout carries the protocol and nothing else. Anything diagnostic goes to stderr.
Console.Error.WriteLine("[handwritten-mcp] starting on stdio (newline-delimited JSON-RPC).");

var registry = new ToolRegistry([new EchoTool(), new AddTool()]);

await using var rpc = new JsonRpcConnection(Console.OpenStandardInput(), Console.OpenStandardOutput());

var server = new HandwrittenMcpServer(
    rpc,
    registry,
    serverName: "handwritten-mcp",
    serverVersion: "1.0.0",
    protocolVersion: "2025-11-25");

try
{
    await server.RunAsync(cancellation.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C, or the client closed the connection.
}
