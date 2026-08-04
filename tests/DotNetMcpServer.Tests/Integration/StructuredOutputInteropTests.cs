using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Asserts what <c>tools/list</c> promises about each tool — its output schema and its
/// behavioural hints — and that <c>tools/call</c> keeps the promise, against the shipped
/// server as a real subprocess.
/// </summary>
public sealed class StructuredOutputInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-structured-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.md"), "# Notes\nOne line.\n");

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

    [Theory]
    [InlineData("read_text_file", true, null, true, false)]
    [InlineData("calculate_expression", true, null, true, false)]
    [InlineData("get_current_datetime", true, null, false, false)]
    [InlineData("scan_workspace", true, null, true, false)]
    [InlineData("append_study_note", null, false, false, false)]
    public async Task Every_tool_declares_its_behaviour(
        string name,
        bool? readOnly,
        bool? destructive,
        bool idempotent,
        bool openWorld)
    {
        var tool = await FindAsync(name);

        Assert.NotNull(tool.Annotations);
        Assert.Equal(readOnly, tool.Annotations.ReadOnlyHint);
        Assert.Equal(destructive, tool.Annotations.DestructiveHint);
        Assert.Equal(idempotent, tool.Annotations.IdempotentHint);
        Assert.Equal(openWorld, tool.Annotations.OpenWorldHint);
    }

    [Theory]
    [InlineData("calculate_expression", "expression", "result")]
    [InlineData("get_current_datetime", "timeZone", "iso8601")]
    [InlineData("scan_workspace", "documents", "lines")]
    public async Task A_tool_whose_answer_is_data_publishes_an_output_schema(
        string name,
        string firstProperty,
        string secondProperty)
    {
        var tool = await FindAsync(name);

        Assert.NotNull(tool.OutputSchema);

        var properties = tool.OutputSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty(firstProperty, out _));
        Assert.True(properties.TryGetProperty(secondProperty, out _));
    }

    [Theory]
    [InlineData("read_text_file")]
    [InlineData("append_study_note")]
    public async Task A_tool_whose_answer_is_prose_publishes_no_output_schema(string name)
    {
        var tool = await FindAsync(name);

        Assert.Null(tool.OutputSchema);
    }

    [Fact]
    public async Task Calling_a_structured_tool_returns_the_shape_it_advertised()
    {
        var result = await Client.CallToolAsync(
            "calculate_expression",
            new Dictionary<string, object?> { ["expression"] = "(1200 + 350) / 5" });

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);

        var structured = result.StructuredContent.Value;
        Assert.Equal("(1200 + 350) / 5", structured.GetProperty("expression").GetString());
        Assert.Equal(310m, structured.GetProperty("result").GetDecimal());
    }

    [Fact]
    public async Task A_structured_result_is_also_readable_as_text()
    {
        // Structured content is additive: a client that only understands content blocks still
        // gets the answer, so turning it on cannot break an older client.
        var result = await Client.CallToolAsync("scan_workspace");

        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.Contains("\"documents\"", text, StringComparison.Ordinal);

        Assert.Equal(1, result.StructuredContent?.GetProperty("documents").GetInt32());
    }

    [Fact]
    public async Task An_unstructured_tool_still_returns_plain_text()
    {
        var result = await Client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "notes.md" });

        Assert.Null(result.StructuredContent);
        Assert.Contains(
            "One line.",
            string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text)),
            StringComparison.Ordinal);
    }

    private async Task<Tool> FindAsync(string name)
    {
        var tools = await Client.ListToolsAsync();

        return tools.Single(tool => tool.Name == name).ProtocolTool;
    }
}
