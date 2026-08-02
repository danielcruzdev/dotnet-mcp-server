namespace DotNetMcpServer.Agent.Config;

/// <summary>
/// Translates the flat environment variable names this project documents in
/// <c>.env.example</c> and <c>docs/INSTALL.md</c> into the section-qualified keys
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> addresses settings by.
/// </summary>
/// <remarks>
/// The alternative is the framework's own <c>Section__Key</c> convention, which needs no
/// translation layer at all. It was rejected because <c>OPENAI_API_KEY</c> is an ecosystem
/// convention rather than something this project invented, and the documented contract is
/// worth more than the twenty lines saved.
/// </remarks>
public static class FlatEnvironmentVariables
{
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.Ordinal)
    {
        ["OPENAI_API_KEY"] = "openAI:apiKey",
        ["OPENAI_MODEL"] = "openAI:model",
        ["OPENAI_BASE_URL"] = "openAI:baseUrl",
        ["OPENAI_TEMPERATURE"] = "openAI:temperature",
        ["MCP_COMMAND"] = "mcp:command",
        ["MCP_ARGUMENTS"] = "mcp:arguments",
        ["MCP_WORKING_DIRECTORY"] = "mcp:workingDirectory",
        ["MCP_WORKSPACE_ROOT"] = "mcp:workspaceRoot",
        ["AGENT_SYSTEM_PROMPT"] = "runtime:systemPrompt",
        ["AGENT_MAX_TOOL_ITERATIONS"] = "runtime:maxToolIterations"
    };

    /// <summary>
    /// Projects the supported variables found in <paramref name="environment"/> onto
    /// configuration keys. A variable that is absent, empty, or whitespace is left out
    /// entirely, so it falls through to the value from <c>appsettings.json</c> rather than
    /// blanking it.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ToConfiguration(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, key) in Mappings)
        {
            if (environment.TryGetValue(variable, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                configuration[key] = value;
            }
        }

        return configuration;
    }

    /// <summary>
    /// Reads the supported variables from the current process environment, ready to be added
    /// as a configuration source.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = entry.Value as string;
        }

        return ToConfiguration(environment);
    }
}
