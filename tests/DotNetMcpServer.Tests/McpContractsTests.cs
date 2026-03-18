using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.Json;
using DotNetMcpServer.Shared.JsonRpc;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Tests;

public class McpContractsTests
{
    [Fact]
    public void McpInitializeResult_RoundTrip_PreservesValues()
    {
        var original = new McpInitializeResult
        {
            ProtocolVersion = "2025-03-26",
            ServerInfo = new McpServerInfo
            {
                Name = "TestServer",
                Version = "1.0.0"
            },
            Capabilities = new McpServerCapabilities
            {
                Tools = new JsonObject()
            }
        };

        var json = JsonSerializer.Serialize(original, JsonDefaults.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpInitializeResult>(json, JsonDefaults.SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("2025-03-26", deserialized!.ProtocolVersion);
        Assert.Equal("TestServer", deserialized.ServerInfo.Name);
        Assert.Equal("1.0.0", deserialized.ServerInfo.Version);
    }

    [Fact]
    public void McpToolDefinition_RoundTrip_PreservesValues()
    {
        var original = new McpToolDefinition
        {
            Name = "test_tool",
            Description = "A test tool",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["input"] = new JsonObject { ["type"] = "string" }
                }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonDefaults.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpToolDefinition>(json, JsonDefaults.SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("test_tool", deserialized!.Name);
        Assert.Equal("A test tool", deserialized.Description);
        Assert.Equal("object", deserialized.InputSchema["type"]?.GetValue<string>());
    }

    [Fact]
    public void McpToolCallRequest_RoundTrip_PreservesValues()
    {
        var original = new McpToolCallRequest
        {
            Name = "calculate",
            Arguments = new JsonObject { ["expression"] = "2+2" }
        };

        var json = JsonSerializer.Serialize(original, JsonDefaults.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpToolCallRequest>(json, JsonDefaults.SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("calculate", deserialized!.Name);
        Assert.Equal("2+2", deserialized.Arguments["expression"]?.GetValue<string>());
    }

    [Fact]
    public void McpToolListResult_RoundTrip_PreservesValues()
    {
        var original = new McpToolListResult
        {
            Tools =
            [
                new McpToolDefinition { Name = "tool_a", Description = "Tool A" },
                new McpToolDefinition { Name = "tool_b", Description = "Tool B" }
            ]
        };

        var json = JsonSerializer.Serialize(original, JsonDefaults.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpToolListResult>(json, JsonDefaults.SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Tools.Count);
        Assert.Equal("tool_a", deserialized.Tools[0].Name);
        Assert.Equal("tool_b", deserialized.Tools[1].Name);
    }

    [Fact]
    public void JsonRpcMessage_CreateRequest_SetsCorrectProperties()
    {
        var message = JsonRpcMessage.CreateRequest("tools/list", 42, new JsonObject { ["key"] = "value" });

        Assert.Equal("2.0", message.JsonRpc);
        Assert.Equal("tools/list", message.Method);
        Assert.Equal(42, message.Id?.GetValue<long>());
        Assert.True(message.IsRequest);
        Assert.False(message.IsNotification);
        Assert.False(message.IsResponse);
    }

    [Fact]
    public void JsonRpcMessage_CreateNotification_HasNoId()
    {
        var message = JsonRpcMessage.CreateNotification("notifications/initialized");

        Assert.Null(message.Id);
        Assert.Equal("notifications/initialized", message.Method);
        Assert.True(message.IsNotification);
        Assert.False(message.IsRequest);
    }

    [Fact]
    public void JsonRpcMessage_CreateResult_SetsCorrectProperties()
    {
        var id = JsonValue.Create(1);
        var result = new JsonObject { ["data"] = "test" };
        var message = JsonRpcMessage.CreateResult(id, result);

        Assert.NotNull(message.Id);
        Assert.NotNull(message.Result);
        Assert.Null(message.Method);
        Assert.True(message.IsResponse);
    }

    [Fact]
    public void JsonRpcMessage_CreateError_SetsErrorProperties()
    {
        var id = JsonValue.Create(1);
        var message = JsonRpcMessage.CreateError(id, JsonRpcErrorCodes.MethodNotFound, "Not found");

        Assert.NotNull(message.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, message.Error.Code);
        Assert.Equal("Not found", message.Error.Message);
    }

    [Fact]
    public void McpMethods_ContainsExpectedValues()
    {
        Assert.Equal("initialize", McpMethods.Initialize);
        Assert.Equal("notifications/initialized", McpMethods.InitializedNotification);
        Assert.Equal("tools/list", McpMethods.ListTools);
        Assert.Equal("tools/call", McpMethods.CallTool);
    }

    [Fact]
    public void JsonRpcErrorCodes_ContainsStandardValues()
    {
        Assert.Equal(-32700, JsonRpcErrorCodes.ParseError);
        Assert.Equal(-32600, JsonRpcErrorCodes.InvalidRequest);
        Assert.Equal(-32601, JsonRpcErrorCodes.MethodNotFound);
        Assert.Equal(-32602, JsonRpcErrorCodes.InvalidParams);
        Assert.Equal(-32603, JsonRpcErrorCodes.InternalError);
    }
}
