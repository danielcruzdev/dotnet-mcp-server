using Microsoft.Extensions.Options;

namespace DotNetMcpServer.Agent.Config;

/// <summary>
/// Derives the values that cannot come from configuration alone: where the repository root
/// is, and which executable serves MCP. This runs after binding, which is why it is an
/// <see cref="IPostConfigureOptions{TOptions}"/> rather than part of the binding itself.
/// </summary>
public sealed class McpSettingsSetup : IPostConfigureOptions<McpSettings>
{
    private readonly string _applicationBaseDirectory;
    private readonly string _currentDirectory;

    public McpSettingsSetup(string applicationBaseDirectory, string currentDirectory)
    {
        _applicationBaseDirectory = applicationBaseDirectory;
        _currentDirectory = currentDirectory;
    }

    public void PostConfigure(string? name, McpSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var workingDirectory = ResolveWorkingDirectoryBase(options.WorkingDirectory);

        options.WorkingDirectory = workingDirectory;
        options.WorkspaceRoot = ResolveDirectory(options.WorkspaceRoot, workingDirectory);

        ResolveServerCommand(options, workingDirectory);
    }

    /// <summary>
    /// Anchors a relative working directory to the repository root, falling back to the
    /// process working directory when no root marker is found. Absolute paths pass through.
    /// </summary>
    private string ResolveWorkingDirectoryBase(string configuredPath)
    {
        if (Path.IsPathFullyQualified(configuredPath))
        {
            return configuredPath;
        }

        var baseDirectory = FindRepositoryRoot(_applicationBaseDirectory) ?? _currentDirectory;

        return ResolveDirectory(configuredPath, baseDirectory);
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a repository-root marker:
    /// a solution file (<c>*.slnx</c> or <c>*.sln</c>) or a <c>.git</c> directory. Both
    /// solution formats are accepted so the lookup survives whichever one the repository
    /// standardises on.
    /// </summary>
    private static string? FindRepositoryRoot(string startDirectory)
    {
        var current = Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(current))
        {
            if (IsRepositoryRoot(current))
            {
                return current;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    private static bool IsRepositoryRoot(string directory)
    {
        return Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly).Length > 0
            || Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).Length > 0
            || Directory.Exists(Path.Combine(directory, ".git"));
    }

    /// <summary>
    /// Picks the MCP server executable when configuration leaves <c>command</c> blank.
    /// </summary>
    /// <remarks>
    /// The server is deliberately launched as a compiled binary rather than via
    /// <c>dotnet run</c>: MSBuild writes build output to stdout, and stdout is the MCP
    /// protocol channel. A single build warning would corrupt the session. The agent's own
    /// output folder names the configuration and target framework, so the sibling server
    /// build is found without any extra configuration.
    /// </remarks>
    private void ResolveServerCommand(McpSettings options, string repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(options.Command))
        {
            return;
        }

        var agentOutput = new DirectoryInfo(_applicationBaseDirectory);
        var targetFramework = agentOutput.Name;
        var configuration = agentOutput.Parent?.Name ?? "Debug";

        var executableName = OperatingSystem.IsWindows()
            ? "DotNetMcpServer.Server.exe"
            : "DotNetMcpServer.Server";

        options.Command = Path.Combine(
            repositoryRoot, "src", "DotNetMcpServer.Server", "bin", configuration, targetFramework, executableName);
    }

    private static string ResolveDirectory(string configuredPath, string fallbackBase)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(fallbackBase);
        }

        if (Path.IsPathFullyQualified(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(fallbackBase, configuredPath));
    }
}
