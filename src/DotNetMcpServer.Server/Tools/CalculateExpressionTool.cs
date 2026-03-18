using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server.Tools;

public sealed partial class CalculateExpressionTool : IMcpTool
{
    public McpToolDefinition Definition => new()
    {
        Name = "calculate_expression",
        Description = "Calcula expressões matemáticas básicas com +, -, *, / e parênteses.",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["expression"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Expressão matemática. Ex.: (1200 + 85) / 5"
                }
            },
            ["required"] = new JsonArray("expression")
        }
    };

    public Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var expression = arguments.GetString("expression");
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Task.FromResult(McpToolCallResult.Fail("O campo 'expression' é obrigatório."));
        }

        if (!ValidExpressionRegex().IsMatch(expression))
        {
            return Task.FromResult(McpToolCallResult.Fail("Expressão inválida. Use apenas números, espaços e operadores + - * / ( )."));
        }

        try
        {
            var normalizedExpression = expression.Replace(',', '.');
            var table = new DataTable
            {
                Locale = CultureInfo.InvariantCulture
            };

            var result = table.Compute(normalizedExpression, string.Empty);
            var numericResult = Convert.ToDecimal(result, CultureInfo.InvariantCulture);
            return Task.FromResult(McpToolCallResult.Success($"Resultado: {numericResult.ToString(CultureInfo.InvariantCulture)}"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(McpToolCallResult.Fail($"Falha ao calcular expressão: {exception.Message}"));
        }
    }

    [GeneratedRegex("^[0-9+\\-*/().,\\s]+$")]
    private static partial Regex ValidExpressionRegex();
}

