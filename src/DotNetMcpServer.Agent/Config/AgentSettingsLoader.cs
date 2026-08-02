using System.Globalization;
using System.Text.Json;
using DotNetMcpServer.Agent.Json;

namespace DotNetMcpServer.Agent.Config;

public static class AgentSettingsLoader
{
    public static AgentSettings Load(string applicationBaseDirectory, string currentDirectory)
    {
        var settings = LoadFromJson(applicationBaseDirectory);
        ApplyEnvironmentOverrides(settings);

        var workingDirectoryBase = ResolveWorkingDirectoryBase(settings.Mcp.WorkingDirectory, applicationBaseDirectory, currentDirectory);
        settings.Mcp.WorkingDirectory = workingDirectoryBase;
        settings.Mcp.WorkspaceRoot = ResolveDirectory(settings.Mcp.WorkspaceRoot, settings.Mcp.WorkingDirectory);
        ResolveServerCommand(settings.Mcp, applicationBaseDirectory, workingDirectoryBase);
        settings.Runtime.MaxToolIterations = Math.Clamp(settings.Runtime.MaxToolIterations, 1, 12);

        return settings;
    }

    /// <summary>
    /// Resolves the working directory. If the configured path is relative (or "."),
    /// it first tries to anchor it to the repository root (found by walking up from
    /// <paramref name="applicationBaseDirectory"/>). Falls back to
    /// <paramref name="currentDirectory"/> when no repository root is found.
    /// Absolute paths are returned as-is.
    /// </summary>
    private static string ResolveWorkingDirectoryBase(string configuredPath, string applicationBaseDirectory, string currentDirectory)
    {
        if (Path.IsPathFullyQualified(configuredPath))
        {
            return configuredPath;
        }

        var repositoryRoot = FindRepositoryRoot(applicationBaseDirectory);
        var baseDirectory = repositoryRoot ?? currentDirectory;

        return ResolveDirectory(configuredPath, baseDirectory);
    }

    /// <summary>
    /// Walks up the directory tree from <paramref name="startDirectory"/> looking for a folder
    /// that carries a repository-root marker: a solution file (<c>*.slnx</c> or <c>*.sln</c>)
    /// or a <c>.git</c> directory. Returns <c>null</c> if no such folder is found.
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

    /// <summary>
    /// A directory is treated as the repository root when it holds a solution file or a
    /// <c>.git</c> directory. Both solution formats are accepted so the lookup keeps working
    /// whichever one the repository standardises on.
    /// </summary>
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
    /// protocol channel. A single build warning would corrupt the session.
    /// The agent's own output folder tells us the configuration and target framework,
    /// so the sibling server build is found without any extra configuration.
    /// </remarks>
    private static void ResolveServerCommand(McpSettings mcp, string applicationBaseDirectory, string repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(mcp.Command))
        {
            return;
        }

        var agentOutput = new DirectoryInfo(applicationBaseDirectory);
        var targetFramework = agentOutput.Name;
        var configuration = agentOutput.Parent?.Name ?? "Debug";

        var executableName = OperatingSystem.IsWindows()
            ? "DotNetMcpServer.Server.exe"
            : "DotNetMcpServer.Server";

        mcp.Command = Path.Combine(
            repositoryRoot, "src", "DotNetMcpServer.Server", "bin", configuration, targetFramework, executableName);
    }

    private static AgentSettings LoadFromJson(string baseDirectory)
    {
        var configPath = Path.Combine(baseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            return new AgentSettings();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AgentSettings>(json, JsonDefaults.SerializerOptions) ?? new AgentSettings();
    }

    private static void ApplyEnvironmentOverrides(AgentSettings settings)
    {
        settings.OpenAI.ApiKey = GetOverride("OPENAI_API_KEY", settings.OpenAI.ApiKey);
        settings.OpenAI.Model = GetOverride("OPENAI_MODEL", settings.OpenAI.Model);
        settings.OpenAI.BaseUrl = GetOverride("OPENAI_BASE_URL", settings.OpenAI.BaseUrl);
        settings.Mcp.Command = GetOverride("MCP_COMMAND", settings.Mcp.Command);
        settings.Mcp.Arguments = GetOverride("MCP_ARGUMENTS", settings.Mcp.Arguments);
        settings.Mcp.WorkingDirectory = GetOverride("MCP_WORKING_DIRECTORY", settings.Mcp.WorkingDirectory);
        settings.Mcp.WorkspaceRoot = GetOverride("MCP_WORKSPACE_ROOT", settings.Mcp.WorkspaceRoot);
        settings.Runtime.SystemPrompt = GetOverride("AGENT_SYSTEM_PROMPT", settings.Runtime.SystemPrompt);

        var maxToolIterations = Environment.GetEnvironmentVariable("AGENT_MAX_TOOL_ITERATIONS");
        if (int.TryParse(maxToolIterations, out var parsedIterations))
        {
            settings.Runtime.MaxToolIterations = parsedIterations;
        }

        var temperature = Environment.GetEnvironmentVariable("OPENAI_TEMPERATURE");
        if (double.TryParse(temperature, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedTemperature))
        {
            settings.OpenAI.Temperature = parsedTemperature;
        }
    }

    private static string GetOverride(string environmentVariable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
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

