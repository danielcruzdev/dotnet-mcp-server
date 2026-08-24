using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Proves a token's scopes decide which tools exist, and that the decision is made before a
/// tool runs rather than inside it.
/// </summary>
/// <remarks>
/// The read tools sit behind <c>mcp:tools</c> and the one tool that writes to the workspace
/// additionally needs <c>mcp:write</c>, so a client that only answers questions can be issued
/// a token that cannot modify anything. Two behaviours matter and are asserted separately: an
/// unauthorized tool is <em>not listed</em>, and calling it anyway still fails. A server that
/// only hid the tool would be relying on the client to be polite.
/// </remarks>
[Trait("Phase", "4")]
public sealed class HttpToolScopeInteropTests : IAsyncLifetime
{
    private static readonly string[] ReadOnlyTools =
    [
        "calculate_expression",
        "get_current_datetime",
        "read_text_file",
        "scan_workspace"
    ];

    private static readonly string[] EveryTool =
    [
        "append_study_note",
        "calculate_expression",
        "get_current_datetime",
        "read_text_file",
        "scan_workspace"
    ];

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-scope-" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(Path.Combine(_workspace, "alpha.md"), "# Alpha\n");
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync();
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task A_read_only_token_is_not_offered_the_tool_that_writes()
    {
        var client = await ConnectAsync(McpScopesUnderTest.Tools);

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            ReadOnlyTools,
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_token_with_both_scopes_is_offered_everything()
    {
        var client = await ConnectAsync(McpScopesUnderTest.Tools, McpScopesUnderTest.Write);

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            EveryTool,
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_read_only_token_cannot_call_the_tool_that_writes()
    {
        var client = await ConnectAsync(McpScopesUnderTest.Tools);

        // Called directly rather than through the listing, because hiding a tool from a list
        // is not a security boundary — refusing the call is.
        await Assert.ThrowsAnyAsync<McpException>(async () => await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "should never be written", ["title"] = "Denied" }));

        Assert.False(
            File.Exists(Path.Combine(_workspace, "notes", "study-notes.md")),
            "The refused call still wrote to the workspace.");
    }

    [Fact]
    public async Task A_write_token_can_call_the_tool_that_writes()
    {
        var client = await ConnectAsync(McpScopesUnderTest.Tools, McpScopesUnderTest.Write);

        var result = await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "written under mcp:write", ["title"] = "Allowed" });

        Assert.NotEqual(true, result.IsError);
        Assert.True(File.Exists(Path.Combine(_workspace, "notes", "study-notes.md")));
    }

    [Fact]
    public async Task A_token_with_no_scopes_at_all_is_offered_no_tools()
    {
        var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.Empty(tools);
    }

    private async Task<McpClient> ConnectAsync(params string[] scopes)
    {
        var server = await HttpServerProcess.StartAsync(
            _workspace,
            TestTokens.ServerArguments(McpScopesUnderTest.Tools, McpScopesUnderTest.Write));

        _disposables.Add(server);

        var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "dotnet-mcp-server-http",
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = "Bearer " + TestTokens.Issue(scopes: scopes)
            }
        }));

        _disposables.Add(client);

        return client;
    }

    /// <summary>
    /// The scope names, restated. The server's own constants are internal to a project this
    /// test does reference — but stating them here means a rename that silently changes the
    /// protocol contract fails a test rather than quietly passing one.
    /// </summary>
    private static class McpScopesUnderTest
    {
        public const string Tools = "mcp:tools";
        public const string Write = "mcp:write";
    }
}
