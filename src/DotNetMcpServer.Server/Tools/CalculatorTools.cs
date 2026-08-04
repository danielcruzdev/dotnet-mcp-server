using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Tools;

[McpServerToolType]
public static partial class CalculatorTools
{
    [McpServerTool(
        Name = "calculate_expression",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Evaluates a basic arithmetic expression using +, -, *, / and parentheses.")]
    public static CalculationResult CalculateExpression(
        [Description("The expression to evaluate, e.g. (1200 + 85) / 5")] string expression)
    {
        return Evaluate(expression);
    }

    /// <summary>
    /// The tool's logic, separated from the MCP surface so tests can exercise it directly.
    /// </summary>
    /// <remarks>
    /// Phase 5 replaces <see cref="DataTable.Compute"/> with a hand-written Pratt parser:
    /// it drops the <c>System.Data</c> dependency and makes the server trim/AOT-safe.
    /// The allow-list below is what keeps <c>Compute</c> from seeing anything but arithmetic.
    /// </remarks>
    internal static CalculationResult Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new McpException("'expression' is required.");
        }

        if (!ValidExpressionRegex().IsMatch(expression))
        {
            throw new McpException("Invalid expression. Use only digits, spaces and the operators + - * / ( ).");
        }

        try
        {
            var normalized = expression.Replace(',', '.');
            using var table = new DataTable { Locale = CultureInfo.InvariantCulture };

            var computed = table.Compute(normalized, string.Empty);

            return new CalculationResult(expression, Convert.ToDecimal(computed, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is EvaluateException or SyntaxErrorException or OverflowException or InvalidCastException or FormatException)
        {
            throw new McpException($"Could not evaluate the expression: {exception.Message}");
        }
    }

    [GeneratedRegex(@"^[0-9+\-*/().,\s]+$")]
    private static partial Regex ValidExpressionRegex();
}

/// <summary>
/// What <c>calculate_expression</c> returns, as data rather than as a sentence.
/// </summary>
/// <remarks>
/// The shape is published as the tool's <c>outputSchema</c> and echoed in
/// <c>structuredContent</c>, so a client can read the number without parsing prose — and can
/// tell, before calling, that a number is what it will get.
/// </remarks>
public sealed record CalculationResult(
    [property: Description("The expression as it was evaluated.")] string Expression,
    [property: Description("The value the expression evaluates to.")] decimal Result);
