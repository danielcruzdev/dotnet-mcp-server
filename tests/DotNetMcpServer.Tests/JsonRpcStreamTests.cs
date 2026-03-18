using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.JsonRpc;

namespace DotNetMcpServer.Tests;

public class JsonRpcStreamTests
{
    [Fact]
    public async Task WriteAndReadMessage_RoundTripsPayload()
    {
        await using var transport = new MemoryStream();

        await using (var writer = new JsonRpcStream(Stream.Null, transport))
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

        await using var reader = new JsonRpcStream(transport, Stream.Null);
        var message = await reader.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("tools/list", message!.Method);
        Assert.Equal(42, message.Id?.GetValue<int>());
        Assert.Equal("next", message.Params?["cursor"]?.GetValue<string>());
    }
}

