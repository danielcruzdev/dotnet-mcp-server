using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Shared.Json;
using DotNetMcpServer.Shared.JsonRpc;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server;

public sealed class McpServerHost
{
    private readonly JsonRpcStream _rpc;
    private readonly ToolRegistry _toolRegistry;
    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly string _protocolVersion;

    public McpServerHost(JsonRpcStream rpc, ToolRegistry toolRegistry, string serverName, string serverVersion, string protocolVersion)
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

    private void HandleNotification(JsonRpcMessage notification)
    {
        if (notification.Method == McpMethods.InitializedNotification)
        {
            Console.Error.WriteLine("[mcp-server] cliente inicializado.");
        }
    }

    private async Task<JsonRpcMessage> HandleRequestAsync(JsonRpcMessage request, CancellationToken cancellationToken)
    {
        if (request.Id is null)
        {
            return JsonRpcMessage.CreateError(null, JsonRpcErrorCodes.InvalidRequest, "Request sem id.");
        }

        return request.Method switch
        {
            McpMethods.Initialize => HandleInitialize(request),
            McpMethods.ListTools => HandleToolsList(request),
            McpMethods.CallTool => await HandleToolCallAsync(request, cancellationToken),
            _ => JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Método '{request.Method}' não encontrado.")
        };
    }

    private JsonRpcMessage HandleInitialize(JsonRpcMessage request)
    {
        var initializeRequest = request.Params?.Deserialize<McpInitializeRequest>(JsonDefaults.SerializerOptions);
        var protocolVersion = string.IsNullOrWhiteSpace(initializeRequest?.ProtocolVersion)
            ? _protocolVersion
            : initializeRequest.ProtocolVersion;

        var result = new McpInitializeResult
        {
            ProtocolVersion = protocolVersion ?? _protocolVersion,
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
            return JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.InvalidParams, "Parâmetros do tools/call inválidos.");
        }

        var callRequest = rawParams.Deserialize<McpToolCallRequest>(JsonDefaults.SerializerOptions);
        if (callRequest is null || string.IsNullOrWhiteSpace(callRequest.Name))
        {
            return JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.InvalidParams, "Payload de tools/call inválido.");
        }

        if (!_toolRegistry.TryGet(callRequest.Name, out var tool) || tool is null)
        {
            return JsonRpcMessage.CreateError(request.Id, JsonRpcErrorCodes.InvalidParams, $"Tool '{callRequest.Name}' não existe.");
        }

        try
        {
            var result = await tool.ExecuteAsync(callRequest.Arguments, cancellationToken);
            return JsonRpcMessage.CreateResult(request.Id!, JsonSerializer.SerializeToNode(result, JsonDefaults.SerializerOptions));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[mcp-server] erro em '{callRequest.Name}': {exception.Message}");
            var result = McpToolCallResult.Fail($"Falha ao executar '{callRequest.Name}': {exception.Message}");
            return JsonRpcMessage.CreateResult(request.Id!, JsonSerializer.SerializeToNode(result, JsonDefaults.SerializerOptions));
        }
    }
}


