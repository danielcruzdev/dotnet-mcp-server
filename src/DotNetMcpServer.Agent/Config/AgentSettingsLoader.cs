using System.Globalization;
using System.Text.Json;
using DotNetMcpServer.Shared.Json;

namespace DotNetMcpServer.Agent.Config;

public static class AgentSettingsLoader
{
    public static AgentSettings Load(string applicationBaseDirectory, string currentDirectory)
    {
        var settings = LoadFromJson(applicationBaseDirectory);
        ApplyEnvironmentOverrides(settings);

        settings.Mcp.WorkingDirectory = ResolveDirectory(settings.Mcp.WorkingDirectory, currentDirectory);
        settings.Mcp.WorkspaceRoot = ResolveDirectory(settings.Mcp.WorkspaceRoot, settings.Mcp.WorkingDirectory);
        settings.Runtime.MaxToolIterations = Math.Clamp(settings.Runtime.MaxToolIterations, 1, 12);

        return settings;
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

