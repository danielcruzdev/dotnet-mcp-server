using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;

namespace DotNetMcpServer.Tests.Tools;

public class ReadTextFileToolTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly ReadTextFileTool _tool;

    public ReadTextFileToolTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        _tool = new ReadTextFileTool(_workspaceRoot);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReturnsContent()
    {
        var filePath = Path.Combine(_workspaceRoot, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello, World!");
        var args = new JsonObject { ["path"] = "test.txt" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Hello, World!", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_FileInSubdirectory_ReturnsContent()
    {
        var subDir = Path.Combine(_workspaceRoot, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "deep.txt"), "Deep content");
        var args = new JsonObject { ["path"] = "sub/deep.txt" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Deep content", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_ReturnsError()
    {
        var args = new JsonObject { ["path"] = "nonexistent.txt" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("nonexistent.txt", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_PathTraversal_ReturnsError()
    {
        var args = new JsonObject { ["path"] = "../../etc/passwd" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPath_ReturnsError()
    {
        var args = new JsonObject { ["path"] = "" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPath_ReturnsError()
    {
        var args = new JsonObject();

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_LargeFile_TruncatesContent()
    {
        var filePath = Path.Combine(_workspaceRoot, "large.txt");
        await File.WriteAllTextAsync(filePath, new string('A', 5000));
        var args = new JsonObject
        {
            ["path"] = "large.txt",
            ["maxCharacters"] = 500
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("[conteúdo truncado]", result.Content[0].Text);
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        Assert.Equal("read_text_file", _tool.Definition.Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspaceRoot, recursive: true); }
        catch { /* cleanup best-effort */ }
    }
}
