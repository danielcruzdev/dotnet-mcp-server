using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetMcpServer.Agent.Json;

/// <summary>
/// Serializer settings for the agent's own configuration and LLM payloads.
/// The MCP wire format is the SDK's concern, not this type's.
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
