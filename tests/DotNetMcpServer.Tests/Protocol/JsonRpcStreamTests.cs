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

    [Fact]
    public async Task A_trailing_message_with_no_closing_newline_is_still_read()
    {
        var pipe = new Pipe();

        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":7,"method":"tools/list"}"""));

        // The peer closed without writing the final newline. The bytes are a whole message
        // and the framing has to say so rather than discard them.
        await pipe.Writer.CompleteAsync();

        await using var connection = new JsonRpcConnection(pipe.Reader.AsStream(), Stream.Null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var message = await connection.ReadMessageAsync(timeout.Token);

        Assert.Equal("tools/list", message?.Method);
        Assert.Equal(7, message?.Id?.GetValue<int>());
    }

    [Fact]
    public async Task A_peer_that_closes_with_nothing_buffered_ends_the_session()
    {
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();

        await using var connection = new JsonRpcConnection(pipe.Reader.AsStream(), Stream.Null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Assert.Null(await connection.ReadMessageAsync(timeout.Token));
    }
}

