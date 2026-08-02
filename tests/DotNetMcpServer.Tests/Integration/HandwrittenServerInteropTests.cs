using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the hand-written MCP implementation with the **official SDK client**, as a real
/// subprocess over stdio.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that matters most in the repository. The project's claim is "I implemented
/// the protocol by hand, then chose the SDK." Without this suite that is an assertion; with it
/// the hand-written server is demonstrably understood by an independent implementation.
/// </para>
/// <para>
/// It also guards the original defect. The artifact once framed messages with an LSP-style
/// <c>Content-Length</c> header instead of the newline delimiting the MCP spec requires. Under
/// that framing the handshake below cannot complete, so a regression fails here loudly rather
/// than silently shipping a server no client can talk to.
/// </para>
/// </remarks>
public sealed class HandwrittenServerInteropTests : IAsyncLifetime
{
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "handwritten-mcp",
            Command = ServerLocator.ExecutablePath("Mcp.Protocol.Handwritten")
        });

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    private McpClient Client => _client ?? throw new InvalidOperationException("Client was not initialised.");

    [Fact]
    public void Official_client_completes_the_handshake()
    {
        // Getting here means initialize and notifications/initialized both round-tripped over
        // newline-delimited JSON, framed by hand-written code and parsed by the SDK.
        Assert.NotNull(Client.ServerInfo);
        Assert.Equal("handwritten-mcp", Client.ServerInfo.Name);
    }

    [Fact]
    public void Negotiated_protocol_version_is_one_the_server_actually_supports()
    {
        string[] supported = ["2025-11-25", "2025-06-18", "2025-03-26"];

        Assert.Contains(supported, version => version == Client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task Official_client_discovers_the_tools()
    {
        var tools = await Client.ListToolsAsync();

        Assert.Equal(["add", "echo"], tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    [Fact]
    public async Task Official_client_calls_a_tool_and_reads_the_result()
    {
        var result = await Client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "newline-delimited" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("newline-delimited", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Numeric_arguments_survive_the_round_trip()
    {
        var result = await Client.CallToolAsync(
            "add",
            new Dictionary<string, object?> { ["a"] = 1200, ["b"] = 350 });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("1550", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_containing_newlines_survives_because_json_escapes_them()
    {
        // The framing is newline-delimited, so an unescaped newline inside a payload would be
        // read as a message boundary and corrupt the session. The serializer escapes it; this
        // proves the guarantee rather than assuming it.
        const string multiline = "first line\nsecond line\r\nthird";

        var result = await Client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = multiline });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("second line", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("third", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_tool_is_reported_as_an_error_not_a_crash()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await Client.CallToolAsync("does_not_exist", new Dictionary<string, object?>()));
    }

    private static string TextOf(CallToolResult result)
    {
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
