using System.ComponentModel.DataAnnotations;

namespace DotNetMcpServer.Agent.Config;

public sealed class OpenAiSettings
{
    [Required]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Never carried in <c>appsettings.json</c> — it arrives from <c>OPENAI_API_KEY</c>.
    /// Missing it fails the host at startup rather than at the first request.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = "gpt-4o-mini";

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.2;
}

public sealed class McpSettings
{
    /// <summary>
    /// Left blank by default on purpose. <see cref="McpSettingsSetup"/> then resolves the
    /// compiled server binary. Naming <c>dotnet</c> here would put MSBuild output on stdout,
    /// which is the MCP protocol channel.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = ".";

    public string WorkspaceRoot { get; set; } = ".";

    /// <summary>
    /// <see cref="Arguments"/> split for process launch. The SDK transport takes an argument
    /// list rather than a single string, which also avoids quoting bugs on paths with spaces.
    /// </summary>
    public IList<string> ArgumentList =>
        Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class AgentRuntimeSettings
{
    [Required]
    public string SystemPrompt { get; set; } =
        "You are a technical, concise AI agent. Use the MCP tools whenever you need workspace data.";

    /// <summary>
    /// An out-of-range value fails at startup. It was previously clamped into range silently,
    /// which corrected the user's configuration without telling them.
    /// </summary>
    [Range(1, 12)]
    public int MaxToolIterations { get; set; } = 6;
}
