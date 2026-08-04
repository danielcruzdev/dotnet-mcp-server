using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the shipped MCP server as a real subprocess using the official SDK client.
/// These are the tests that prove the server interoperates with anything speaking MCP —
/// Claude Desktop, VS Code, Claude Code — rather than only with the agent in this repository.
/// </summary>
public sealed class SdkServerInteropTests : IAsyncLifetime
{
    private static readonly string[] ExpectedToolNames =
    [
        "append_study_note",
        "calculate_expression",
        "get_current_datetime",
        "read_text_file",
        "scan_workspace"
    ];

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-interop-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "notes.md"),
            "# Interop\nThe handshake completed.\n");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private McpClient Client => _client ?? throw new InvalidOperationException("Client was not initialised.");

    [Fact]
    public async Task Handshake_completes_and_reports_server_identity()
    {
        // Reaching this point at all means initialize + notifications/initialized succeeded
        // over newline-delimited JSON, which is what C1 was about.
        Assert.NotNull(Client.ServerInfo);
        Assert.False(string.IsNullOrWhiteSpace(Client.ServerInfo.Name));
    }

    [Fact]
    public async Task ListTools_advertises_every_tool()
    {
        var tools = await Client.ListToolsAsync();

        Assert.Equal(ExpectedToolNames, tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    [Fact]
    public async Task CallTool_evaluates_an_expression()
    {
        var result = await Client.CallToolAsync(
            "calculate_expression",
            new Dictionary<string, object?> { ["expression"] = "(1200 + 350) / 5" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("310", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallTool_reads_a_file_from_the_workspace()
    {
        var result = await Client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "notes.md" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("The handshake completed.", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallTool_refuses_to_escape_the_workspace()
    {
        var result = await Client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "../../../etc/passwd" });

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CallTool_appends_a_note_that_lands_on_disk()
    {
        var result = await Client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["title"] = "MCP", ["note"] = "Newline-delimited framing." });

        Assert.NotEqual(true, result.IsError);

        var notes = await File.ReadAllTextAsync(Path.Combine(_workspace, "notes", "study-notes.md"));
        Assert.Contains("Newline-delimited framing.", notes, StringComparison.Ordinal);
        Assert.Contains("## MCP", notes, StringComparison.Ordinal);
    }

    private static string TextOf(CallToolResult result)
    {
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
