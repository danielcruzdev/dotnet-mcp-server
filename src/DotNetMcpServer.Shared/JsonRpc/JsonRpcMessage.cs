using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotNetMcpServer.Shared.JsonRpc;

public sealed class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public JsonNode? Id { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("params")]
    public JsonNode? Params { get; init; }

    [JsonPropertyName("result")]
    public JsonNode? Result { get; init; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; init; }

    public bool IsRequest => Method is not null && Id is not null;

    public bool IsNotification => Method is not null && Id is null;

    public bool IsResponse => Method is null && Id is not null;

    public static JsonRpcMessage CreateRequest(string method, long id, JsonNode? parameters = null)
    {
        return new JsonRpcMessage
        {
            Method = method,
            Id = JsonValue.Create(id),
            Params = parameters
        };
    }

    public static JsonRpcMessage CreateNotification(string method, JsonNode? parameters = null)
    {
        return new JsonRpcMessage
        {
            Method = method,
            Params = parameters
        };
    }

    public static JsonRpcMessage CreateResult(JsonNode id, JsonNode? result)
    {
        return new JsonRpcMessage
        {
            Id = id.DeepClone(),
            Result = result
        };
    }

    public static JsonRpcMessage CreateError(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        return new JsonRpcMessage
        {
            Id = id?.DeepClone(),
            Error = new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
    }
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonNode? Data { get; init; }
}

