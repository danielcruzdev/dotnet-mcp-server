using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Protocol.Handwritten.Json;
using Mcp.Protocol.Handwritten.JsonRpc;
using Mcp.Protocol.Handwritten.Mcp;

namespace Mcp.Protocol.Handwritten.Server;

public sealed class HandwrittenMcpServer
{
    private readonly JsonRpcConnection _rpc;
    private readonly ToolRegistry _toolRegistry;
    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly string _protocolVersion;

    public HandwrittenMcpServer(JsonRpcConnection rpc, ToolRegistry toolRegistry, string serverName, string serverVersion, string protocolVersion)
    {
        _rpc = rpc;
        _toolRegistry = toolRegistry;
        _serverName = serverName;
        _serverVersion = serverVersion;
        _protocolVersion = protocolVersion;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            JsonRpcMessage? message;
            try
            {
                message = await _rpc.ReadMessageAsync(cancellationToken);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (message is null)
            {
                break;
            }

            if (message.Method is null)
            {
                continue;
            }

            if (message.IsNotification)
            {
                HandleNotification(message);
                continue;
            }

            var response = await HandleRequestAsync(message, cancellationToken);
            await _rpc.WriteMessageAsync(response, cancellationToken);
        }
    }

    private static void HandleNotification(JsonRpcMessage notification)
    {
        if (notification.Method == McpMethods.InitializedNotification)
        {
            Console.Error.WriteLine("[handwritten-mcp] client initialised.");
        }
    }

    private async Task<JsonRpcMessage> HandleRequestAsync(JsonRpcMessage request, CancellationToken cancellationToken)
    {
        if (request.Id is null)
        {
            return JsonRpcMessage.CreateError(null, JsonRpcErrorCodes.InvalidRequest, "Request has no id.");
        }

        return request.Method switch
        {
            McpMethods.Initialize => HandleInitialize(request),
            McpMethods.ListTools => HandleToolsList(request),
            McpMethods.CallTool => await HandleToolCallAsync(request, cancellationToken),
            _ => JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Method '{request.Method}' not found.")
        };
    }

    /// <summary>
    /// Protocol revisions this server can actually speak, newest first.
    /// </summary>
    private static readonly string[] SupportedProtocolVersions =
    [
        "2025-11-25",
        "2025-06-18",
        "2025-03-26"
    ];

    /// <summary>
    /// Negotiates the protocol revision per the MCP lifecycle: honour the client's request
    /// when it is supported, otherwise answer with the newest revision this server speaks and
    /// let the client decide whether to continue. Echoing the client's version back
    /// unconditionally — as this once did — claims support for revisions that were never
    /// implemented.
    /// </summary>
    private static string NegotiateProtocolVersion(string? requested, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) && SupportedProtocolVersions.Contains(requested, StringComparer.Ordinal))
        {
            return requested;
        }

        return SupportedProtocolVersions[0] is { Length: > 0 } newest ? newest : fallback;
    }

    private JsonRpcMessage HandleInitialize(JsonRpcMessage request)
    {
        var initializeRequest = request.Params?.Deserialize<McpInitializeRequest>(JsonDefaults.SerializerOptions);

        var result = new McpInitializeResult
        {
            ProtocolVersion = NegotiateProtocolVersion(initializeRequest?.ProtocolVersion, _protocolVersion),
            ServerInfo = new McpServerInfo
            {
                Name = _serverName,
                Version = _serverVersion
            },
            Capabilities = new McpServerCapabilities
            {
                Tools = new JsonObject()
            }
        };

        return JsonRpcMessage.CreateResult(request.Id!, JsonSerializer.SerializeToNode(result, JsonDefaults.SerializerOptions));
    }

    private JsonRpcMessage HandleToolsList(JsonRpcMessage request)
    {
        var result = new McpToolListResult
        {
            Tools = _toolRegistry.ListDefinitions()
        };

        return JsonRpcMessage.CreateResult(request.Id!, JsonSerializer.SerializeToNode(result, JsonDefaults.SerializerOptions));
    }

    private async Task<JsonRpcMessage> HandleToolCallAsync(JsonRpcMessage request, CancellationToken cancellationToken)
    {
        if (request.Params is not JsonObject rawParams)
        {
            return JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.InvalidParams, "Invalid tools/call parameters.");
        }

        var callRequest = rawParams.Deserialize<McpToolCallRequest>(JsonDefaults.SerializerOptions);
        if (callRequest is null || string.IsNullOrWhiteSpace(callRequest.Name))
        {
            return JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.InvalidParams, "Invalid tools/call payload.");
        }

        if (!_toolRegistry.TryGet(callRequest.Name, out var tool) || tool is null)
        {
            return JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.InvalidParams, $"Tool '{callRequest.Name}' does not exist.");
        }

        try
        {
            var result = await tool.ExecuteAsync(callRequest.Arguments, cancellationToken);
            return JsonRpcMessage.CreateResult(request.Id!, JsonSerializer.SerializeToNode(result, JsonDefaults.SerializerOptions));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[handwritten-mcp] error in '{callRequest.Name}': {exception.Message}");
            var result = McpToolCallResult.Fail($"Failed to execute '{callRequest.Name}': {exception.Message}");
            return JsonRpcMessage.CreateResult(request.Id!, JsonSerializer.SerializeToNode(result, JsonDefaults.SerializerOptions));
        }
    }
}


