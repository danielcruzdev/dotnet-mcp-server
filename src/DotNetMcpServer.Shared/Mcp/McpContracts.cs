using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotNetMcpServer.Shared.Mcp;

public static class McpMethods
{
    public const string Initialize = "initialize";
    public const string InitializedNotification = "notifications/initialized";
    public const string ListTools = "tools/list";
    public const string CallTool = "tools/call";
}

public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

public sealed class McpInitializeRequest
{
    [JsonPropertyName("protocolVersion")]
    public string? ProtocolVersion { get; init; }
}

public sealed class McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

public sealed class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public JsonObject Tools { get; init; } = new JsonObject();
}

public sealed class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = string.Empty;

    [JsonPropertyName("serverInfo")]
    public McpServerInfo ServerInfo { get; init; } = new();

    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; init; } = new();
}

public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public JsonObject InputSchema { get; init; } = new JsonObject();
}

public sealed class McpToolListResult
{
    [JsonPropertyName("tools")]
    public List<McpToolDefinition> Tools { get; init; } = new();
}

public sealed class McpToolCallRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public JsonObject Arguments { get; init; } = new JsonObject();
}

public sealed class McpTextContent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpTextContent> Content { get; init; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; init; }

    public static McpToolCallResult Success(string text)
    {
        return new McpToolCallResult
        {
            Content = new List<McpTextContent>
            {
                new McpTextContent
                {
                    Text = text
                }
            }
        };
    }

    public static McpToolCallResult Fail(string text)
    {
        return new McpToolCallResult
        {
            IsError = true,
            Content = new List<McpTextContent>
            {
                new McpTextContent
                {
                    Text = text
                }
            }
        };
    }
}

