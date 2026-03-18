using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;

namespace DotNetMcpServer.Tests.Tools;

public class GetCurrentDateTimeToolTests
{
    private readonly GetCurrentDateTimeTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_NoTimezone_ReturnsUtc()
    {
        var args = new JsonObject();

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("UTC", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyTimezone_ReturnsUtc()
    {
        var args = new JsonObject { ["timezone"] = "" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("UTC", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_ValidTimezone_ReturnsConvertedTime()
    {
        var args = new JsonObject { ["timezone"] = "UTC" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidTimezone_ReturnsError()
    {
        var args = new JsonObject { ["timezone"] = "Invalid/Timezone_XYZ" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Invalid/Timezone_XYZ", result.Content[0].Text);
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        Assert.Equal("get_current_datetime", _tool.Definition.Name);
    }
}
