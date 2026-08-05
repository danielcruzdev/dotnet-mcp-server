using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using Mcp.Protocol.Handwritten.JsonRpc;

namespace DotNetMcpServer.Tests;

public class JsonRpcStreamTests
{
    [Fact]
    public async Task WriteAndReadMessage_RoundTripsPayload()
    {
        await using var transport = new MemoryStream();

        await using (var writer = new JsonRpcConnection(Stream.Null, transport))
        {
            var request = JsonRpcMessage.CreateRequest(
                method: "tools/list",
                id: 42,
                parameters: new JsonObject
                {
                    ["cursor"] = "next"
                });

            await writer.WriteMessageAsync(request, CancellationToken.None);
        }

        transport.Position = 0;

        await using var reader = new JsonRpcConnection(transport, Stream.Null);
        var message = await reader.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("tools/list", message!.Method);
        Assert.Equal(42, message.Id?.GetValue<int>());
        Assert.Equal("next", message.Params?["cursor"]?.GetValue<string>());
    }

    [Fact]
    public async Task Two_messages_arriving_in_one_read_are_both_delivered()
    {
        // A client under load writes its next message before the server has read the previous
        // one, so both land in a single read. The second must be served from what is already
        // buffered -- waiting for bytes that will never come deadlocks the session.
        var pipe = new Pipe();

        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":1,"method":"initialize"}""" + "\n" +
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n"));
        await pipe.Writer.FlushAsync();

        // The writer stays open: a live client has sent these two and is waiting for a reply.
        await using var connection = new JsonRpcConnection(pipe.Reader.AsStream(), Stream.Null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = await connection.ReadMessageAsync(timeout.Token);
        var second = await connection.ReadMessageAsync(timeout.Token);

        Assert.Equal("initialize", first?.Method);
        Assert.Equal("notifications/initialized", second?.Method);
    }
}

