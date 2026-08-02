namespace DotNetMcpServer.Server.Workspace;

/// <summary>
/// The directory the file tools are allowed to touch, and the single place where a
/// caller-supplied relative path is turned into an absolute one.
/// </summary>
/// <remarks>
/// Containment is enforced here rather than in each tool so there is exactly one guard to
/// audit. Phase 5 hardens it further: symlink resolution, per-OS case sensitivity, and a
/// deny-list for sensitive paths.
/// </remarks>
public sealed class WorkspaceContext
{
    public WorkspaceContext(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>
    /// Resolves the workspace root from <c>--workspace-root</c>, then the
    /// <c>MCP_WORKSPACE_ROOT</c> environment variable, then the current directory.
    /// </summary>
    public static WorkspaceContext Resolve(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--workspace-root", StringComparison.OrdinalIgnoreCase))
            {
                return new WorkspaceContext(args[i + 1]);
            }
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("MCP_WORKSPACE_ROOT");

        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? new WorkspaceContext(Directory.GetCurrentDirectory())
            : new WorkspaceContext(fromEnvironment);
    }

    /// <summary>
    /// Maps a workspace-relative path to an absolute one, refusing anything that escapes
    /// the root.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The path resolves outside the workspace.</exception>
    public string ResolvePath(string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(Root, relativePath));

        if (combined.Equals(Root, StringComparison.OrdinalIgnoreCase))
        {
            return combined;
        }

        var rootWithSeparator = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access denied: the path must stay inside the workspace.");
        }

        return combined;
    }

    /// <summary>
    /// Resolves a workspace-relative directory, creating it if it does not exist.
    /// </summary>
    /// <remarks>
    /// The creation lives here, behind the containment guard, rather than in the tool that
    /// happens to need it — so a directory can only ever be created inside the workspace, and
    /// an unwritable root fails on the call that needed it rather than at wiring time.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The path resolves outside the workspace.</exception>
    public string EnsureDirectory(string relativePath)
    {
        var absolutePath = ResolvePath(relativePath);
        Directory.CreateDirectory(absolutePath);

        return absolutePath;
    }
}
