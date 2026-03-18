using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;

namespace DotNetMcpServer.Tests.Tools;

public class AppendStudyNoteToolTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly AppendStudyNoteTool _tool;

    public AppendStudyNoteToolTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        _tool = new AppendStudyNoteTool(_workspaceRoot);
    }

    [Fact]
    public async Task ExecuteAsync_ValidNote_AppendsToFile()
    {
        var args = new JsonObject
        {
            ["title"] = "Test Note",
            ["note"] = "This is a test note."
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);

        var filePath = Path.Combine(_workspaceRoot, "notes", "study-notes.md");
        Assert.True(File.Exists(filePath));

        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("## Test Note", content);
        Assert.Contains("This is a test note.", content);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTitle_UsesDefaultTitle()
    {
        var args = new JsonObject { ["note"] = "Note without title." };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);

        var filePath = Path.Combine(_workspaceRoot, "notes", "study-notes.md");
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("## Anotação", content);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyNote_ReturnsError()
    {
        var args = new JsonObject { ["note"] = "" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_MissingNote_ReturnsError()
    {
        var args = new JsonObject();

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleNotes_AllAppended()
    {
        var args1 = new JsonObject
        {
            ["title"] = "First",
            ["note"] = "First note"
        };
        var args2 = new JsonObject
        {
            ["title"] = "Second",
            ["note"] = "Second note"
        };

        await _tool.ExecuteAsync(args1, CancellationToken.None);
        await _tool.ExecuteAsync(args2, CancellationToken.None);

        var filePath = Path.Combine(_workspaceRoot, "notes", "study-notes.md");
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("## First", content);
        Assert.Contains("## Second", content);
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        Assert.Equal("append_study_note", _tool.Definition.Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspaceRoot, recursive: true); }
        catch { /* cleanup best-effort */ }
    }
}
