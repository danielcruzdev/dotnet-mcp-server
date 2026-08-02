using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Server.Workspace;
using ModelContextProtocol;

namespace DotNetMcpServer.Tests.Tools;

/// <summary>
/// The workspace tools' own behaviour, run in-process against a temp workspace. The interop
/// suite proves these tools are reachable over the protocol, but it asserts coarsely — the
/// character cap, the clamp and the failure messages have no coverage there.
/// </summary>
public sealed class WorkspaceToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ws-tools-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceContext _workspace;

    public WorkspaceToolTests()
    {
        Directory.CreateDirectory(_root);
        _workspace = new WorkspaceContext(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, content);

        return path;
    }

    [Fact]
    public async Task ReadTextFile_returns_the_whole_file_when_it_fits()
    {
        await WriteFileAsync("small.md", "# Title\nBody.\n");

        var result = await WorkspaceTools.ReadTextFile(_workspace, "small.md");

        Assert.Contains("Body.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[content truncated]", result, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Counts the payload characters rather than asserting on the reported total, which also
    /// covers the header and the truncation marker.
    /// </remarks>
    private static int PayloadLength(string result) => result.Count(character => character == 'x');

    [Fact]
    public async Task ReadTextFile_truncates_at_the_character_cap()
    {
        await WriteFileAsync("large.md", new string('x', 5000));

        var result = await WorkspaceTools.ReadTextFile(_workspace, "large.md", maxCharacters: 300);

        Assert.Contains("[content truncated]", result, StringComparison.Ordinal);
        Assert.Equal(300, PayloadLength(result));
    }

    /// <summary>
    /// An out-of-range cap is clamped rather than rejected: it is a hint from the model, not a
    /// user's configuration, so silently correcting it is the right trade here.
    /// </summary>
    [Theory]
    [InlineData(5, 200)]
    [InlineData(999999, 8000)]
    public async Task ReadTextFile_clamps_a_cap_outside_the_supported_range(int requested, int expected)
    {
        await WriteFileAsync("large.md", new string('x', 20000));

        var result = await WorkspaceTools.ReadTextFile(_workspace, "large.md", requested);

        Assert.Equal(expected, PayloadLength(result));
    }

    [Fact]
    public async Task ReadTextFile_reports_a_file_that_is_not_there()
    {
        var exception = await Assert.ThrowsAsync<McpException>(
            () => WorkspaceTools.ReadTextFile(_workspace, "absent.md"));

        Assert.Contains("absent.md", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadTextFile_requires_a_path(string path)
    {
        await Assert.ThrowsAsync<McpException>(() => WorkspaceTools.ReadTextFile(_workspace, path));
    }

    /// <summary>
    /// The containment failure has to reach the caller as an <see cref="McpException"/>, or the
    /// SDK reports it as a transport-level fault instead of a tool error.
    /// </summary>
    [Fact]
    public async Task ReadTextFile_turns_a_containment_failure_into_a_tool_error()
    {
        var exception = await Assert.ThrowsAsync<McpException>(
            () => WorkspaceTools.ReadTextFile(_workspace, "../../secrets.txt"));

        Assert.Contains("workspace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppendStudyNote_creates_the_notes_directory_and_appends_to_it()
    {
        Assert.False(Directory.Exists(Path.Combine(_root, "notes")));

        await WorkspaceTools.AppendStudyNote(_workspace, "First note.", "One");
        await WorkspaceTools.AppendStudyNote(_workspace, "Second note.", "Two");

        var notes = await File.ReadAllTextAsync(Path.Combine(_root, "notes", "study-notes.md"));

        Assert.Contains("## One", notes, StringComparison.Ordinal);
        Assert.Contains("First note.", notes, StringComparison.Ordinal);

        // The second call must not overwrite the first — this is an append tool.
        Assert.Contains("## Two", notes, StringComparison.Ordinal);
        Assert.Contains("Second note.", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AppendStudyNote_requires_a_note(string note)
    {
        await Assert.ThrowsAsync<McpException>(() => WorkspaceTools.AppendStudyNote(_workspace, note));
    }
}
