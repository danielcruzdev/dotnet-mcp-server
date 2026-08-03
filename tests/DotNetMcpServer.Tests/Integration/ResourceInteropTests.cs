using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the shipped server's resource surface with the official SDK client, as a real
/// subprocess. Resources are only worth having if a client that is not this repository can
/// list and read them, which is what these assert.
/// </summary>
public sealed class ResourceInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-resources-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "docs"));
        Directory.CreateDirectory(Path.Combine(_workspace, "bin"));
        Directory.CreateDirectory(Path.Combine(_workspace, ".hidden"));

        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.md"), "# Notes\nA workspace document.\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "docs", "guide.md"), "# Guide\nNested document.\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "data.json"), """{ "answer": 42 }""");
        await File.WriteAllLinesAsync(
            Path.Combine(_workspace, "lines.txt"),
            Enumerable.Range(1, 10).Select(line => $"line {line}"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "bin", "generated.md"), "build output");
        await File.WriteAllTextAsync(Path.Combine(_workspace, ".hidden", "secret.md"), "hidden");
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "diagram.png"), [0x89, 0x50, 0x4E, 0x47]);

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
    public void Server_advertises_the_resources_capability()
    {
        Assert.NotNull(Client.ServerCapabilities.Resources);
    }

    [Fact]
    public async Task ListResources_returns_workspace_documents()
    {
        var resources = await Client.ListResourcesAsync();

        var names = resources.Select(resource => resource.Name).OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(["data.json", "docs/guide.md", "lines.txt", "notes.md"], names);

        var notes = resources.Single(resource => resource.Name == "notes.md");
        Assert.Equal("workspace://file/notes.md", notes.Uri);
        Assert.Equal("text/markdown", notes.MimeType);
        Assert.True(notes.ProtocolResource.Size > 0);
    }

    [Fact]
    public async Task ListResources_skips_build_output_hidden_directories_and_binaries()
    {
        var resources = await Client.ListResourcesAsync();
        var names = resources.Select(resource => resource.Name).ToList();

        Assert.DoesNotContain("bin/generated.md", names, StringComparer.Ordinal);
        Assert.DoesNotContain(".hidden/secret.md", names, StringComparer.Ordinal);
        Assert.DoesNotContain("diagram.png", names, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ReadResource_returns_the_document_text()
    {
        var result = await Client.ReadResourceAsync("workspace://file/docs/guide.md");

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("workspace://file/docs/guide.md", contents.Uri);
        Assert.Equal("text/markdown", contents.MimeType);
        Assert.Contains("Nested document.", contents.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadResource_rejects_a_uri_outside_the_scheme()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.ReadResourceAsync("https://example.com/notes.md").AsTask());
    }

    [Fact]
    public async Task ReadResource_rejects_a_missing_document()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.ReadResourceAsync("workspace://file/absent.md").AsTask());
    }

    [Fact]
    public async Task ReadResource_refuses_to_escape_the_workspace()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.ReadResourceAsync("workspace://file/../../etc/passwd").AsTask());
    }

    [Fact]
    public async Task ListResources_pages_with_a_cursor()
    {
        // 50 per page: 4 documents already exist, so 60 more forces exactly two pages.
        for (var index = 0; index < 60; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_workspace, $"page-{index:D2}.txt"),
                $"document {index}");
        }

        var first = await Client.ListResourcesAsync(new ListResourcesRequestParams());
        Assert.Equal(50, first.Resources.Count);
        Assert.NotNull(first.NextCursor);

        var second = await Client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = first.NextCursor });
        Assert.Equal(14, second.Resources.Count);
        Assert.Null(second.NextCursor);

        var uris = first.Resources.Concat(second.Resources).Select(resource => resource.Uri).ToList();
        Assert.Equal(uris.Count, uris.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ListResources_rejects_a_cursor_it_did_not_issue()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = "not-a-cursor" }).AsTask());
    }

    [Fact]
    public async Task ListResourceTemplates_advertises_the_excerpt_template()
    {
        var templates = await Client.ListResourceTemplatesAsync();

        var excerpt = Assert.Single(templates);
        Assert.Equal("workspace://excerpt/{start}-{end}/{+path}", excerpt.UriTemplate);
        Assert.Equal("workspace_excerpt", excerpt.Name);

        // A template is not a resource: it must not show up in resources/list.
        var resources = await Client.ListResourcesAsync();
        Assert.DoesNotContain(resources, resource => resource.Name == excerpt.Name);
    }

    [Fact]
    public async Task ReadResource_expands_the_template_into_a_line_range()
    {
        var result = await Client.ReadResourceAsync("workspace://excerpt/3-5/lines.txt");

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("line 3\nline 4\nline 5", contents.Text);
    }

    [Fact]
    public async Task ReadResource_expands_a_template_path_containing_separators()
    {
        // The point of the reserved expansion: {+path} keeps matching past the first '/'.
        var result = await Client.ReadResourceAsync("workspace://excerpt/1-1/docs/guide.md");

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("# Guide", contents.Text);
    }

    [Fact]
    public async Task ReadResource_clamps_a_range_that_runs_past_the_end_of_the_document()
    {
        var result = await Client.ReadResourceAsync("workspace://excerpt/9-9999/lines.txt");

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("line 9\nline 10", contents.Text);
    }

    [Fact]
    public async Task ReadResource_rejects_an_inverted_line_range()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.ReadResourceAsync("workspace://excerpt/9-2/lines.txt").AsTask());
    }

    [Fact]
    public async Task ReadResource_refuses_a_template_path_that_escapes_the_workspace()
    {
        await Assert.ThrowsAnyAsync<McpException>(
            () => Client.ReadResourceAsync("workspace://excerpt/1-2/../../etc/passwd").AsTask());
    }
}
