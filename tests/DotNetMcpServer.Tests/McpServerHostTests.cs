using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Server;
using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Shared.Json;
using DotNetMcpServer.Shared.JsonRpc;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Tests;

public class McpServerHostTests
{
    [Fact]
    public async Task HandleInitialize_ReturnsServerInfoAndCapabilities()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var initRequest = JsonRpcMessage.CreateRequest("initialize", 1, new JsonObject
        {
            ["protocolVersion"] = "2025-03-26"
        });

        await env.ClientRpc.WriteMessageAsync(initRequest, CancellationToken.None);
        var response = await env.ClientRpc.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response!.IsResponse);
        Assert.NotNull(response.Result);

        var result = response.Result.Deserialize<McpInitializeResult>(JsonDefaults.SerializerOptions);
        Assert.NotNull(result);
        Assert.Equal("2025-03-26", result!.ProtocolVersion);
        Assert.Equal("TestServer", result.ServerInfo.Name);
    }

    [Fact]
    public async Task HandleToolsList_ReturnsRegisteredTools()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var listRequest = JsonRpcMessage.CreateRequest("tools/list", 1, new JsonObject());

        await env.ClientRpc.WriteMessageAsync(listRequest, CancellationToken.None);
        var response = await env.ClientRpc.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(response);
        var result = response!.Result.Deserialize<McpToolListResult>(JsonDefaults.SerializerOptions);
        Assert.NotNull(result);
        Assert.Single(result!.Tools);
        Assert.Equal("fake_tool", result.Tools[0].Name);
    }

    [Fact]
    public async Task HandleToolCall_ValidTool_ReturnsResult()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var callRequest = JsonRpcMessage.CreateRequest("tools/call", 1, new JsonObject
        {
            ["name"] = "fake_tool",
            ["arguments"] = new JsonObject()
        });

        await env.ClientRpc.WriteMessageAsync(callRequest, CancellationToken.None);
        var response = await env.ClientRpc.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response!.Result);

        var result = response.Result.Deserialize<McpToolCallResult>(JsonDefaults.SerializerOptions);
        Assert.NotNull(result);
        Assert.False(result!.IsError);
        Assert.Contains("fake result", result.Content[0].Text);
    }

    [Fact]
    public async Task HandleToolCall_UnknownTool_ReturnsError()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var callRequest = JsonRpcMessage.CreateRequest("tools/call", 1, new JsonObject
        {
            ["name"] = "nonexistent_tool",
            ["arguments"] = new JsonObject()
        });

        await env.ClientRpc.WriteMessageAsync(callRequest, CancellationToken.None);
        var response = await env.ClientRpc.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response!.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error.Code);
    }

    [Fact]
    public async Task HandleUnknownMethod_ReturnsMethodNotFound()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var request = JsonRpcMessage.CreateRequest("unknown/method", 1, new JsonObject());

        await env.ClientRpc.WriteMessageAsync(request, CancellationToken.None);
        var response = await env.ClientRpc.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response!.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error.Code);
    }

    /// <summary>
    /// Creates an in-memory pair of JsonRpcStreams (client ↔ server) and runs McpServerHost in background.
    /// </summary>
    private sealed class TestEnvironment : IAsyncDisposable
    {
        public JsonRpcStream ClientRpc { get; }
        private readonly Task _serverTask;
        private readonly CancellationTokenSource _cts;
        private readonly Stream _clientToServer;
        private readonly Stream _serverToClient;

        private TestEnvironment(JsonRpcStream clientRpc, Task serverTask, CancellationTokenSource cts, Stream clientToServer, Stream serverToClient)
        {
            ClientRpc = clientRpc;
            _serverTask = serverTask;
            _cts = cts;
            _clientToServer = clientToServer;
            _serverToClient = serverToClient;
        }

        public static async Task<TestEnvironment> CreateAsync()
        {
            // Bidirectional pipes: client writes → server reads, server writes → client reads
            var clientToServer = new DuplexPipe();
            var serverToClient = new DuplexPipe();

            var serverRpc = new JsonRpcStream(clientToServer.ReadStream, serverToClient.WriteStream);
            var clientRpc = new JsonRpcStream(serverToClient.ReadStream, clientToServer.WriteStream);

            var registry = new ToolRegistry([new FakeTool()]);
            var host = new McpServerHost(serverRpc, registry, "TestServer", "1.0.0", "2025-03-26");

            var cts = new CancellationTokenSource();
            var serverTask = Task.Run(() => host.RunAsync(cts.Token));

            await Task.Delay(50); // Give server a moment to start

            return new TestEnvironment(clientRpc, serverTask, cts, clientToServer.ReadStream, serverToClient.ReadStream);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try { await _serverTask; }
            catch (OperationCanceledException) { }
            catch (EndOfStreamException) { }
            _cts.Dispose();
            await ClientRpc.DisposeAsync();
        }
    }

    private sealed class FakeTool : IMcpTool
    {
        public McpToolDefinition Definition => new()
        {
            Name = "fake_tool",
            Description = "A fake tool for testing",
            InputSchema = new JsonObject { ["type"] = "object" }
        };

        public Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(McpToolCallResult.Success("fake result"));
        }
    }

    /// <summary>
    /// Simple in-memory pipe using two MemoryStreams for bidirectional communication.
    /// Uses a BlockingStream wrapper to simulate a real pipe.
    /// </summary>
    private sealed class DuplexPipe
    {
        private readonly BlockingStream _stream = new();

        public Stream ReadStream => _stream;
        public Stream WriteStream => _stream;
    }

    /// <summary>
    /// A stream that blocks on read until data is available, simulating a pipe.
    /// </summary>
    private sealed class BlockingStream : Stream
    {
        private readonly SemaphoreSlim _dataAvailable = new(0);
        private readonly object _lock = new();
        private readonly Queue<byte[]> _chunks = new();
        private byte[]? _currentChunk;
        private int _currentOffset;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            lock (_lock)
            {
                _chunks.Enqueue(copy);
            }
            _dataAvailable.Release();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_currentChunk is not null && _currentOffset < _currentChunk.Length)
                {
                    var bytesToCopy = Math.Min(count, _currentChunk.Length - _currentOffset);
                    Buffer.BlockCopy(_currentChunk, _currentOffset, buffer, offset, bytesToCopy);
                    _currentOffset += bytesToCopy;
                    if (_currentOffset >= _currentChunk.Length)
                    {
                        _currentChunk = null;
                        _currentOffset = 0;
                    }
                    return bytesToCopy;
                }

                await _dataAvailable.WaitAsync(cancellationToken);

                lock (_lock)
                {
                    if (_chunks.Count > 0)
                    {
                        _currentChunk = _chunks.Dequeue();
                        _currentOffset = 0;
                    }
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
