using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Runs one set of assertions against both transports, so stdio and Streamable HTTP are held
/// to the same behaviour rather than to two suites that drifted apart.
/// </summary>
/// <remarks>
/// Every case here is transport-independent by construction: same tools, same results, same
/// refusals. What is deliberately <em>not</em> here is anything the transports genuinely
/// disagree about — elicitation, sampling and unsolicited notifications, which stateless
/// Streamable HTTP cannot carry at all (SEP-2567). Those have their own tests, on the
/// transport that has them, and <see cref="HttpSessionInteropTests"/> pins the boundary. A
/// theory that quietly skipped a case on one transport would be the drift this class exists to
/// catch.
/// </remarks>
[Trait("Phase", "4")]
public sealed class BothTransportsInteropTests : IAsyncLifetime
{
    private static readonly string[] EveryTool =
    [
        "append_study_note",
        "calculate_expression",
        "get_current_datetime",
        "read_text_file",
        "scan_workspace"
    ];

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-both-" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(Path.Combine(_workspace, "docs"));

        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.md"), "# Notes\nThe handshake completed.\n");
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "docs", "design.md"),
            "# Design\nThe transport frames messages as newline-delimited JSON.\n");
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

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task The_handshake_reports_a_server_identity(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        Assert.NotNull(client.ServerInfo);
        Assert.False(string.IsNullOrWhiteSpace(client.ServerInfo.Name));
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task Every_capability_is_advertised(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        Assert.NotNull(client.ServerCapabilities.Tools);
        Assert.NotNull(client.ServerCapabilities.Resources);
        Assert.NotNull(client.ServerCapabilities.Prompts);
        Assert.NotNull(client.ServerCapabilities.Completions);
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task Every_tool_is_advertised(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var tools = await client.ListToolsAsync();

        Assert.Equal(EveryTool, tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task A_tool_call_returns_its_answer(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var result = await client.CallToolAsync(
            "calculate_expression",
            new Dictionary<string, object?> { ["expression"] = "6 * 7" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("42", Assert.IsType<TextContentBlock>(result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task A_tool_call_reads_a_workspace_document(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var result = await client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "notes.md" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains(
            "The handshake completed.",
            Assert.IsType<TextContentBlock>(result.Content[0]).Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task A_structured_tool_answers_with_structured_content(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var result = await client.CallToolAsync("scan_workspace");

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(2, result.StructuredContent.Value.GetProperty("documents").GetInt32());
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task The_workspace_boundary_holds(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var result = await client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "../../etc/passwd" });

        Assert.Equal(true, result.IsError);
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task Resources_are_listed_and_read(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var resources = await client.ListResourcesAsync();
        Assert.Contains(resources, resource => resource.Uri == "workspace://file/notes.md");

        var read = await client.ReadResourceAsync("workspace://file/notes.md");
        var contents = Assert.IsType<TextResourceContents>(read.Contents[0]);

        Assert.Contains("The handshake completed.", contents.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task A_missing_resource_is_refused(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        await Assert.ThrowsAnyAsync<McpException>(
            async () => await client.ReadResourceAsync("workspace://file/absent.md"));
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task Prompts_are_listed_and_rendered(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var prompts = await client.ListPromptsAsync();
        Assert.Contains(prompts, prompt => prompt.Name == "summarize_document");

        var rendered = await client.GetPromptAsync(
            "summarize_document",
            new Dictionary<string, object?> { ["path"] = "docs/design.md" });

        var embedded = Assert.IsType<EmbeddedResourceBlock>(rendered.Messages[^1].Content);
        var document = Assert.IsType<TextResourceContents>(embedded.Resource);

        Assert.Contains("newline-delimited JSON", document.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(McpTransportKind.Stdio)]
    [InlineData(McpTransportKind.Http)]
    public async Task Argument_completion_answers(McpTransportKind transport)
    {
        var client = await ConnectAsync(transport);

        var result = await client.CompleteAsync(
            new PromptReference { Name = "summarize_document" },
            "path",
            "docs/");

        Assert.Equal(["docs/design.md"], result.Completion.Values);
    }

    private async Task<ModelContextProtocol.Client.McpClient> ConnectAsync(McpTransportKind transport)
    {
        var server = await McpServerUnderTest.StartAsync(transport, _workspace);

        _disposables.Add(server);

        return server.Client;
    }
}
