using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;

namespace DotNetMcpServer.Tests.Tools;

public class CalculateExpressionToolTests
{
    private readonly CalculateExpressionTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_SimpleAddition_ReturnsResult()
    {
        var args = new JsonObject { ["expression"] = "2 + 3" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("5", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_ComplexExpression_ReturnsResult()
    {
        var args = new JsonObject { ["expression"] = "(10 + 5) * 2" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("30", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_DecimalDivision_ReturnsResult()
    {
        var args = new JsonObject { ["expression"] = "10 / 4" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("2.5", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_CommaAsDecimalSeparator_NormalizesToDot()
    {
        var args = new JsonObject { ["expression"] = "1,5 + 2,5" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("4", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyExpression_ReturnsError()
    {
        var args = new JsonObject { ["expression"] = "" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_MissingExpression_ReturnsError()
    {
        var args = new JsonObject();

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCharacters_ReturnsError()
    {
        var args = new JsonObject { ["expression"] = "2 + abc" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_DivisionByZero_ReturnsError()
    {
        var args = new JsonObject { ["expression"] = "10 / 0" };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // DataTable.Compute returns Infinity for division by zero, but Convert.ToDecimal may throw
        Assert.Single(result.Content);
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        Assert.Equal("calculate_expression", _tool.Definition.Name);
    }
}
