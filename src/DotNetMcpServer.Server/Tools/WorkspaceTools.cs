using System.ComponentModel;
using System.Globalization;
using System.Text;
using DotNetMcpServer.Server.Resources;
using DotNetMcpServer.Server.Workspace;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Tools;

/// <summary>
/// Tools that touch the workspace. Path containment lives in <see cref="WorkspaceContext"/>,
/// which is injected by the host — these methods never build a path themselves.
/// </summary>
[McpServerToolType]
public static partial class WorkspaceTools
{
    /// <summary>
    /// The log category these tools write under. Named explicitly because a static class
    /// cannot be a type argument to <see cref="ILogger{TCategoryName}"/>, and the category is
    /// what a client sees on every notifications/message this server sends.
    /// </summary>
    private const string LogCategory = "DotNetMcpServer.Server.Tools.WorkspaceTools";

    private const int DefaultMaxCharacters = 1600;
    private const int MinimumMaxCharacters = 200;
    private const int MaximumMaxCharacters = 8000;

    // The annotations are hints a client uses to decide what to auto-approve. Their defaults
    // are the cautious ones — not read-only, destructive, non-idempotent, open-world — so
    // saying nothing makes every tool look as dangerous as the worst of them.
    [McpServerTool(Name = "read_text_file", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Reads a text file from inside the project workspace.")]
    public static async Task<string> ReadTextFile(
        WorkspaceContext workspace,
        ILoggerFactory loggerFactory,
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

        var logger = loggerFactory.CreateLogger(LogCategory);
        LogFileRead(logger, path, body.Length, truncated);

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {path}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Characters returned: {body.Length}");
        builder.AppendLine();
        builder.Append(body);

        return builder.ToString();
    }

    // Writes, but only ever adds: nothing already in the file is lost. Not idempotent — the
    // same call twice leaves two notes, which is the point of an append tool.
    [McpServerTool(Name = "append_study_note", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates or appends a note in notes/study-notes.md inside the workspace.")]
    public static async Task<string> AppendStudyNote(
        WorkspaceContext workspace,
        ILoggerFactory loggerFactory,
        McpServer server,
        [Description("The note body.")] string note,
        [Description("Note title. If omitted, the server asks the user for one.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        // Asking for a title for a note that is about to be rejected would be rude, so the
        // note is checked here even though AppendNoteAsync owns the guard.
        if (!string.IsNullOrWhiteSpace(note) && string.IsNullOrWhiteSpace(title))
        {
            title = await AskForTitleAsync(server, cancellationToken).ConfigureAwait(false);
        }

        return await AppendNoteAsync(workspace, loggerFactory, note, title, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the note. Separated from the MCP surface because the surface now needs an
    /// <see cref="McpServer"/> to ask the user a question, and a test cannot conjure one.
    /// </summary>
    internal static async Task<string> AppendNoteAsync(
        WorkspaceContext workspace,
        ILoggerFactory loggerFactory,
        string note,
        string? title,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new McpException("'note' is required.");
        }

        var notesDirectory = workspace.EnsureDirectory("notes");
        var notesFile = Path.Combine(notesDirectory, "study-notes.md");
        var entry = FormatEntry(title, note, DateTimeOffset.Now);

        await File.AppendAllTextAsync(notesFile, entry, cancellationToken).ConfigureAwait(false);

        var logger = loggerFactory.CreateLogger(LogCategory);
        LogNoteAppended(logger, notesFile);

        return $"Note saved to: {notesFile}";
    }

    [McpServerTool(
        Name = "scan_workspace",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Walks every text document in the workspace and reports totals: documents, lines, characters.")]
    public static async Task<WorkspaceScan> ScanWorkspace(
        WorkspaceResourceProvider provider,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        var documents = provider.EnumerateDocuments();
        var lines = 0L;
        var characters = 0L;

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await provider.TryReadAsync(
                WorkspaceResourceProvider.ToUri(documents[index]),
                cancellationToken).ConfigureAwait(false);

            if (content is not null)
            {
                lines += CountLines(content.Text);
                characters += content.Text.Length;
            }

            // Reported after the work, so the count is what has been done rather than what is
            // about to be. The SDK drops these unless the client sent a progress token.
            progress.Report(new ProgressNotificationValue
            {
                Progress = index + 1,
                Total = documents.Count,
                Message = documents[index]
            });
        }

        return new WorkspaceScan(documents.Count, lines, characters);
    }

    /// <summary>
    /// Asks the user, through the client, for the title the call did not supply.
    /// </summary>
    /// <remarks>
    /// Elicitation is a client capability, not a given: a client that does not offer it gets
    /// the default title rather than an error. A tool that only works in front of a human is
    /// a tool that fails in every automated caller.
    /// </remarks>
    /// <returns>The title the user gave, or <see langword="null"/> to fall back.</returns>
    private static async Task<string?> AskForTitleAsync(McpServer server, CancellationToken cancellationToken)
    {
        if (server.ClientCapabilities?.Elicitation is null)
        {
            return null;
        }

        var response = await server
            .ElicitAsync<NoteTitle>("What should this note be called?", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Declining is an answer. The note is still saved, under the default title — losing
        // the note because the user did not want to name it would be the wrong trade.
        return response.IsAccepted ? response.Content?.Title : null;
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = 1;

        foreach (var character in text)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    // A client that turned logging up asked to see what the server is doing on its behalf.
    // These are the two things it does that touch the user's disk.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Read {Path}: {Characters} characters returned, truncated={Truncated}")]
    private static partial void LogFileRead(ILogger logger, string path, int characters, bool truncated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Appended a note to {Path}")]
    private static partial void LogNoteAppended(ILogger logger, string path);

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

/// <summary>
/// The shape <c>append_study_note</c> elicits when it was called without a title.
/// </summary>
/// <remarks>
/// Elicitation schemas are a constrained subset of JSON Schema — strings, numbers, booleans
/// and string enums only — so this stays one flat string. The SDK builds the schema from the
/// public properties of this type.
/// </remarks>
public sealed class NoteTitle
{
    [Description("A short title for the note.")]
    public string? Title { get; set; }
}

/// <summary>What <c>scan_workspace</c> counted.</summary>
/// <remarks>
/// <c>read_text_file</c> and <c>append_study_note</c> deliberately keep returning text: a
/// document and a confirmation are prose, and wrapping them in JSON only makes a client escape
/// them again. Tools whose answer is data get a schema; tools whose answer is words do not.
/// </remarks>
public sealed record WorkspaceScan(
    [property: Description("Number of text documents walked.")] int Documents,
    [property: Description("Total lines across those documents.")] long Lines,
    [property: Description("Total characters across those documents.")] long Characters);
