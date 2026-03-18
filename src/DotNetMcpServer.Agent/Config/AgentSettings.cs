using System.Text.Json.Serialization;

namespace DotNetMcpServer.Agent.Config;

public sealed class AgentSettings
{
    [JsonPropertyName("openAI")]
    public OpenAiSettings OpenAI { get; set; } = new();

    [JsonPropertyName("mcp")]
    public McpSettings Mcp { get; set; } = new();

    [JsonPropertyName("runtime")]
    public AgentRuntimeSettings Runtime { get; set; } = new();
}

public sealed class OpenAiSettings
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o-mini";

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.2;
}

public sealed class McpSettings
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "dotnet";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "run --project src/DotNetMcpServer.Server/DotNetMcpServer.Server.csproj";

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = ".";

    [JsonPropertyName("workspaceRoot")]
    public string WorkspaceRoot { get; set; } = ".";

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2025-03-26";
}

public sealed class AgentRuntimeSettings
{
    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; set; } = "Você é um agente de IA técnico e objetivo. Sempre use ferramentas MCP quando precisar de dados do workspace.";

    [JsonPropertyName("maxToolIterations")]
    public int MaxToolIterations { get; set; } = 6;
}

