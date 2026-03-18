using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Shared.Json;
using DotNetMcpServer.Shared.JsonRpc;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Agent.Mcp;

public sealed class McpClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly JsonRpcStream _rpc;
    private readonly Task _stderrReaderTask;
    private long _requestId;

    private McpClient(Process process, JsonRpcStream rpc, Task stderrReaderTask)
    {
        _process = process;
        _rpc = rpc;
        _stderrReaderTask = stderrReaderTask;
    }

    public static async Task<McpClient> StartAsync(McpSettings settings, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.Command,
            Arguments = settings.Arguments,
            WorkingDirectory = settings.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["MCP_WORKSPACE_ROOT"] = settings.WorkspaceRoot;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo do MCP server.");

        var stderrReaderTask = Task.Run(async () =>
        {
            while (!process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                Console.Error.WriteLine($"[mcp-server] {line}");
            }
        });

        var rpc = new JsonRpcStream(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        var client = new McpClient(process, rpc, stderrReaderTask);

        await client.InitializeAsync(settings.ProtocolVersion, cancellationToken);

        return client;
    }

    public async Task<List<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync(McpMethods.ListTools, new JsonObject(), cancellationToken);
        var listResult = JsonSerializer.Deserialize<McpToolListResult>(result.ToJsonString(JsonDefaults.SerializerOptions), JsonDefaults.SerializerOptions)
            ?? new McpToolListResult();

        return listResult.Tools;
    }

    public async Task<McpToolCallResult> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments
        };

        var result = await SendRequestAsync(McpMethods.CallTool, payload, cancellationToken);
        var callResult = JsonSerializer.Deserialize<McpToolCallResult>(result.ToJsonString(JsonDefaults.SerializerOptions), JsonDefaults.SerializerOptions);
        return callResult ?? McpToolCallResult.Fail("Resposta inválida da tool.");
    }

    private async Task InitializeAsync(string protocolVersion, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["protocolVersion"] = protocolVersion
        };

        await SendRequestAsync(McpMethods.Initialize, payload, cancellationToken);
        await SendNotificationAsync(McpMethods.InitializedNotification, new JsonObject(), cancellationToken);
    }

    private async Task<JsonObject> SendRequestAsync(string method, JsonObject payload, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var request = JsonRpcMessage.CreateRequest(method, requestId, payload);

        await _rpc.WriteMessageAsync(request, cancellationToken);

        while (true)
        {
            var response = await _rpc.ReadMessageAsync(cancellationToken)
                ?? throw new EndOfStreamException("Conexão com MCP server foi encerrada inesperadamente.");

            if (!response.IsResponse)
            {
                continue;
            }

            var responseId = response.Id?.GetValue<long>();
            if (responseId != requestId)
            {
                continue;
            }

            if (response.Error is not null)
            {
                throw new InvalidOperationException($"MCP error {response.Error.Code}: {response.Error.Message}");
            }

            return response.Result as JsonObject ?? new JsonObject();
        }
    }

    private Task SendNotificationAsync(string method, JsonObject payload, CancellationToken cancellationToken)
    {
        return _rpc.WriteMessageAsync(JsonRpcMessage.CreateNotification(method, payload), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _rpc.DisposeAsync();

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        try
        {
            await _stderrReaderTask;
        }
        catch (Exception)
        {
            // Stderr reader may fail when process is killed — safe to ignore.
        }

        _process.Dispose();
    }
}

