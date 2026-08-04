using System.Globalization;
using DotNetMcpServer.Server.Workspace;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Server.Resources;

/// <summary>
/// Presents the workspace's text documents as MCP resources: the enumeration behind
/// <c>resources/list</c> and the lookup behind <c>resources/read</c>.
/// </summary>
/// <remarks>
/// Resources are addressed as <c>workspace://file/&lt;relative-path&gt;</c> rather than as
/// <c>file://</c> URIs, so the path a client sees is the same relative path the tools accept
/// and nothing outside the workspace is expressible in the scheme at all. That is a
/// convenience, not a guard — every read still resolves through
/// <see cref="WorkspaceContext"/>.
/// </remarks>
public sealed class WorkspaceResourceProvider
{
    /// <summary>The prefix every resource URI served from the workspace carries.</summary>
    public const string UriPrefix = "workspace://file/";

    /// <summary>Resources returned per <c>resources/list</c> page.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// Upper bound on the walk. A workspace root pointed at a whole drive should degrade to a
    /// truncated list rather than to an unbounded directory traversal.
    /// </summary>
    private const int MaxDocuments = 2000;

    /// <summary>
    /// Refuse to materialise anything larger than this. Phase 5 (<c>F5-08</c>) replaces the
    /// whole-file read with a capped stream; until then the limit is what keeps a stray large
    /// file from being loaded into memory in full.
    /// </summary>
    private const long MaxReadableBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Build output and tooling state. Left in, they bury the documents a client actually
    /// wants under thousands of generated files.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        "TestResults"
    };

    /// <summary>
    /// Doubles as the definition of "document": a file is exposed only if its extension is
    /// listed here, which also settles the MIME type the client is told.
    /// </summary>
    private static readonly Dictionary<string, string> MimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".yml"] = "application/yaml",
        [".yaml"] = "application/yaml",
        [".xml"] = "application/xml",
        [".csproj"] = "application/xml",
        [".props"] = "application/xml",
        [".targets"] = "application/xml",
        [".slnx"] = "application/xml",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".cs"] = "text/x-csharp",
        [".sql"] = "application/sql",
        [".sh"] = "text/x-shellscript",
        [".ps1"] = "text/plain",
        [".editorconfig"] = "text/plain",
        [".gitattributes"] = "text/plain",
        [".gitignore"] = "text/plain"
    };

    private readonly WorkspaceContext _workspace;

    public WorkspaceResourceProvider(WorkspaceContext workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Relative paths of every document the server is willing to expose, in a stable order —
    /// pagination cursors are indices into this list, so the order has to be deterministic.
    /// </summary>
    public IReadOnlyList<string> EnumerateDocuments()
    {
        var documents = new List<string>();
        var pending = new Stack<string>();
        pending.Push(_workspace.Root);

        while (pending.Count > 0 && documents.Count < MaxDocuments)
        {
            var directory = pending.Pop();

            foreach (var file in SafeEntries(directory, files: true))
            {
                if (MimeTypesByExtension.ContainsKey(Path.GetExtension(file)))
                {
                    documents.Add(ToRelativePath(file));
                }
            }

            foreach (var subdirectory in SafeEntries(directory, files: false))
            {
                if (!IsExcludedDirectory(Path.GetFileName(subdirectory)))
                {
                    pending.Push(subdirectory);
                }
            }
        }

        documents.Sort(StringComparer.Ordinal);

        if (documents.Count > MaxDocuments)
        {
            documents.RemoveRange(MaxDocuments, documents.Count - MaxDocuments);
        }

        return documents;
    }

    /// <summary>
    /// One page of <c>resources/list</c>, with the cursor the client should send to get the
    /// next one.
    /// </summary>
    /// <exception cref="McpProtocolException">The cursor did not come from this server.</exception>
    public ListResourcesResult ListPage(string? cursor)
    {
        var documents = EnumerateDocuments();
        var start = ParseCursor(cursor, documents.Count);
        var end = Math.Min(start + PageSize, documents.Count);

        var page = new List<Resource>(end - start);

        for (var index = start; index < end; index++)
        {
            page.Add(Describe(documents[index]));
        }

        return new ListResourcesResult
        {
            Resources = page,
            NextCursor = end < documents.Count ? end.ToString(CultureInfo.InvariantCulture) : null
        };
    }

    /// <summary>
    /// Reads a resource by URI, or returns <see langword="null"/> if the URI is not one of
    /// this provider's — the caller can then let another handler try it.
    /// </summary>
    /// <exception cref="McpProtocolException">The URI is ours but cannot be served.</exception>
    public async Task<TextResourceContents?> TryReadAsync(string uri, CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(uri, out var relativePath))
        {
            return null;
        }

        var file = RequireFile(relativePath, uri);
        var text = await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false);

        return new TextResourceContents
        {
            Uri = uri,
            MimeType = MimeTypeFor(relativePath),
            Text = text
        };
    }

    /// <summary>
    /// Reads an inclusive, 1-based line range of a workspace document — the body of the
    /// <c>workspace://excerpt/{start}-{end}/{+path}</c> template.
    /// </summary>
    /// <exception cref="McpProtocolException">The range or the document is unusable.</exception>
    public async Task<string> ReadExcerptAsync(
        string relativePath,
        int startLine,
        int endLine,
        CancellationToken cancellationToken)
    {
        if (startLine < 1 || endLine < startLine)
        {
            throw new McpProtocolException(
                $"Invalid line range {startLine}-{endLine}: expected 1 <= start <= end.",
                McpErrorCode.InvalidParams);
        }

        var file = RequireFile(relativePath, ToUri(relativePath));
        var lines = await File.ReadAllLinesAsync(file.FullName, cancellationToken).ConfigureAwait(false);

        if (startLine > lines.Length)
        {
            throw new McpProtocolException(
                $"{relativePath} has {lines.Length} lines; the range starts at {startLine}.",
                McpErrorCode.InvalidParams);
        }

        return string.Join('\n', lines[(startLine - 1)..Math.Min(endLine, lines.Length)]);
    }

    /// <summary>Maps a workspace-relative path to the URI a client addresses it by.</summary>
    public static string ToUri(string relativePath)
    {
        var segments = relativePath.Split('/');

        return UriPrefix + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    /// <summary>The reverse of <see cref="ToUri"/>, for URIs in this provider's scheme.</summary>
    public static bool TryGetRelativePath(string uri, out string relativePath)
    {
        relativePath = string.Empty;

        if (!uri.StartsWith(UriPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = uri[UriPrefix.Length..].Split('/');
        relativePath = string.Join('/', segments.Select(Uri.UnescapeDataString));

        return relativePath.Length > 0;
    }

    /// <summary>
    /// Whether a workspace-relative path is one of the documents this server exposes — the
    /// same rule <see cref="EnumerateDocuments"/> applies while walking, asked of a single
    /// path instead. The filesystem watcher uses it to ignore changes nobody can subscribe to.
    /// </summary>
    public static bool IsDocument(string relativePath)
    {
        if (!MimeTypesByExtension.ContainsKey(Path.GetExtension(relativePath)))
        {
            return false;
        }

        var segments = relativePath.Split('/');

        return !segments[..^1].Any(IsExcludedDirectory);
    }

    /// <summary>The MIME type advertised for a document, by extension.</summary>
    public static string MimeTypeFor(string relativePath)
    {
        return MimeTypesByExtension.TryGetValue(Path.GetExtension(relativePath), out var mimeType)
            ? mimeType
            : "text/plain";
    }

    /// <summary>
    /// Resolves a workspace-relative path, translating a containment failure into the
    /// protocol error the client sees.
    /// </summary>
    /// <exception cref="McpProtocolException">The path escapes the workspace.</exception>
    internal string ResolveOrThrow(string relativePath)
    {
        try
        {
            return _workspace.ResolvePath(relativePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new McpProtocolException(exception.Message, McpErrorCode.InvalidParams);
        }
        catch (ArgumentException exception)
        {
            throw new McpProtocolException($"Invalid resource path: {exception.Message}", McpErrorCode.InvalidParams);
        }
    }

    /// <summary>
    /// Resolves a document that must exist and must be small enough to materialise.
    /// </summary>
    /// <exception cref="McpProtocolException">It does not exist, or it is too large.</exception>
    private FileInfo RequireFile(string relativePath, string uri)
    {
        var file = new FileInfo(ResolveOrThrow(relativePath));

        if (!file.Exists)
        {
            throw new McpProtocolException($"Resource not found: {uri}", McpErrorCode.InvalidParams);
        }

        if (file.Length > MaxReadableBytes)
        {
            throw new McpProtocolException(
                $"Resource is too large to read: {uri} ({file.Length} bytes, limit {MaxReadableBytes}).",
                McpErrorCode.InvalidParams);
        }

        return file;
    }

    private Resource Describe(string relativePath)
    {
        var file = new FileInfo(_workspace.ResolvePath(relativePath));

        return new Resource
        {
            Uri = ToUri(relativePath),
            Name = relativePath,
            Title = Path.GetFileName(relativePath),
            Description = $"Workspace document at {relativePath}",
            MimeType = MimeTypeFor(relativePath),
            Size = file.Exists ? file.Length : null
        };
    }

    /// <summary>
    /// Build output and dot-directories are skipped whether they are met while walking or
    /// reported by the watcher, so the rule lives in one place.
    /// </summary>
    private static bool IsExcludedDirectory(string name)
    {
        return name.StartsWith('.') || ExcludedDirectories.Contains(name);
    }

    private string ToRelativePath(string absolutePath)
    {
        return Path.GetRelativePath(_workspace.Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// The spec asks for <c>-32602</c> when a cursor did not come from this server, so an
    /// unreadable cursor is a protocol error rather than a silent reset to the first page.
    /// </summary>
    private static int ParseCursor(string? cursor, int documentCount)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        if (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || start > documentCount)
        {
            throw new McpProtocolException($"Unknown resource cursor: {cursor}", McpErrorCode.InvalidParams);
        }

        return start;
    }

    /// <summary>
    /// Materialises one directory level. A directory that cannot be read is skipped rather
    /// than turned into a failed <c>resources/list</c> for the whole workspace.
    /// </summary>
    private static string[] SafeEntries(string directory, bool files)
    {
        try
        {
            return files ? Directory.GetFiles(directory) : Directory.GetDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
