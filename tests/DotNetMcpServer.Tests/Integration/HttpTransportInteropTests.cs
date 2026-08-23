using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the shipped server over Streamable HTTP with the official SDK client, against a real
/// Kestrel listener in a real subprocess.
/// </summary>
/// <remarks>
/// The stdio suite proves the server speaks MCP. This one proves the second transport serves
/// the same MCP — same tools, same results — which is what makes <c>F4-08</c>'s
/// both-transports theory meaningful rather than decorative.
/// </remarks>
[Trait("Phase", "4")]
public sealed class HttpTransportInteropTests : IAsyncLifetime
{
    private static readonly string[] ExpectedToolNames =
    [
        "append_study_note",
        "calculate_expression",
        "get_current_datetime",
        "read_text_file",
        "scan_workspace"
    ];

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-http-" + Guid.NewGuid().ToString("N"));
    private HttpServerProcess? _server;
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "notes.md"),
            "# Interop\nThe handshake completed.\n");

        _server = await HttpServerProcess.StartAsync(_workspace);

        _client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "dotnet-mcp-server-http",
            Endpoint = _server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        }));
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private McpClient Client => _client ?? throw new InvalidOperationException("Client was not initialised.");

    private HttpServerProcess Server => _server ?? throw new InvalidOperationException("Server was not started.");

    [Fact]
    public void Handshake_completes_over_streamable_http()
    {
        Assert.NotNull(Client.ServerInfo);
        Assert.False(string.IsNullOrWhiteSpace(Client.ServerInfo.Name));
    }

    [Fact]
    public async Task ListTools_advertises_the_same_tools_as_stdio()
    {
        var tools = await Client.ListToolsAsync();

        Assert.Equal(ExpectedToolNames, tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CallTool_evaluates_an_expression_over_http()
    {
        var result = await Client.CallToolAsync(
            "calculate_expression",
            new Dictionary<string, object?> { ["expression"] = "6 * 7" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("42", Assert.IsType<TextContentBlock>(result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallTool_reads_a_workspace_file_over_http()
    {
        var result = await Client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "notes.md" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains(
            "The handshake completed.",
            Assert.IsType<TextContentBlock>(result.Content[0]).Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Path_traversal_is_refused_over_http_too()
    {
        var result = await Client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "../../etc/passwd" });

        Assert.Equal(true, result.IsError);
    }

    [Fact]
    public async Task Resources_and_prompts_are_served_over_http()
    {
        var resources = await Client.ListResourcesAsync();
        var prompts = await Client.ListPromptsAsync();

        Assert.NotEmpty(resources);
        Assert.NotEmpty(prompts);
    }

    [Fact]
    public async Task The_http_host_writes_nothing_to_stdout()
    {
        // Same invariant as stdio, asserted here because the HTTP host is the one place where
        // writing to stdout would look harmless.
        await Client.ListToolsAsync();

        Assert.Equal(string.Empty, Server.StandardOutput);
    }
}
