using System.ComponentModel;
using System.Globalization;
using System.Text;
using DotNetMcpServer.Server.Workspace;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Tools;

/// <summary>
/// Tools that touch the workspace. Path containment lives in <see cref="WorkspaceContext"/>,
/// which is injected by the host — these methods never build a path themselves.
/// </summary>
[McpServerToolType]
public static class WorkspaceTools
{
    private const int DefaultMaxCharacters = 1600;
    private const int MinimumMaxCharacters = 200;
    private const int MaximumMaxCharacters = 8000;

    [McpServerTool(Name = "read_text_file")]
    [Description("Reads a text file from inside the project workspace.")]
    public static async Task<string> ReadTextFile(
        WorkspaceContext workspace,
        [Description("Path relative to the workspace root, e.g. README.md")] string path,
        [Description("Maximum number of characters to return (200-8000).")] int? maxCharacters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("'path' is required.");
        }

        var limit = Math.Clamp(maxCharacters ?? DefaultMaxCharacters, MinimumMaxCharacters, MaximumMaxCharacters);

        string absolutePath;
        try
        {
            absolutePath = workspace.ResolvePath(path);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new McpException(exception.Message);
        }

        if (!File.Exists(absolutePath))
        {
            throw new McpException($"File not found: {path}");
        }

        // Phase 5 replaces this with a capped streaming read; today a very large file is
        // fully materialised before it is truncated.
        var content = await File.ReadAllTextAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        var truncated = content.Length > limit;
        var body = truncated ? content[..limit] + "\n\n[content truncated]" : content;

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {path}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Characters returned: {body.Length}");
        builder.AppendLine();
        builder.Append(body);

        return builder.ToString();
    }

    [McpServerTool(Name = "append_study_note")]
    [Description("Creates or appends a note in notes/study-notes.md inside the workspace.")]
    public static async Task<string> AppendStudyNote(
        WorkspaceContext workspace,
        [Description("The note body.")] string note,
        [Description("Optional note title.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new McpException("'note' is required.");
        }

        var notesDirectory = workspace.ResolvePath("notes");
        Directory.CreateDirectory(notesDirectory);

        var notesFile = Path.Combine(notesDirectory, "study-notes.md");
        var entry = FormatEntry(title, note, DateTimeOffset.Now);

        await File.AppendAllTextAsync(notesFile, entry, cancellationToken).ConfigureAwait(false);

        return $"Note saved to: {notesFile}";
    }

    /// <summary>
    /// Formats a single note entry. Separated from the file write so tests can assert on
    /// the rendered markdown without touching the filesystem.
    /// </summary>
    internal static string FormatEntry(string? title, string note, DateTimeOffset createdAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"## {(string.IsNullOrWhiteSpace(title) ? "Note" : title)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Created at: {createdAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine(note.Trim());
        builder.AppendLine();

        return builder.ToString();
    }
}
