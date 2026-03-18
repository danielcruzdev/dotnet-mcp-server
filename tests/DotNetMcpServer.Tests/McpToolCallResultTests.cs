using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Tests;

public class McpToolCallResultTests
{
    [Fact]
    public void Success_ShouldCreateNonErrorResult()
    {
        var result = McpToolCallResult.Success("ok");

        Assert.False(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("ok", result.Content[0].Text);
    }

    [Fact]
    public void Fail_ShouldCreateErrorResult()
    {
        var result = McpToolCallResult.Fail("erro");

        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("erro", result.Content[0].Text);
    }
}

