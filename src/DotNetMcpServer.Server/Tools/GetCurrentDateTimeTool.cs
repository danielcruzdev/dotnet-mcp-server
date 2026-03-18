using System.Globalization;
using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server.Tools;

public sealed class GetCurrentDateTimeTool : IMcpTool
{
    public McpToolDefinition Definition => new()
    {
        Name = "get_current_datetime",
        Description = "Retorna a data e hora atual, opcionalmente convertida para um timezone específico.",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["timezone"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Timezone IANA/Windows. Ex.: America/Sao_Paulo"
                }
            }
        }
    };

    public Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var timezone = arguments.GetString("timezone");

        if (string.IsNullOrWhiteSpace(timezone))
        {
            return Task.FromResult(McpToolCallResult.Success($"UTC agora: {nowUtc:O}"));
        }

        try
        {
            var timezoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var converted = TimeZoneInfo.ConvertTime(nowUtc, timezoneInfo);
            var formatted = converted.ToString("dddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
            return Task.FromResult(McpToolCallResult.Success($"{timezoneInfo.Id}: {formatted}"));
        }
        catch (TimeZoneNotFoundException)
        {
            return Task.FromResult(McpToolCallResult.Fail($"Timezone '{timezone}' não encontrado no sistema."));
        }
        catch (InvalidTimeZoneException)
        {
            return Task.FromResult(McpToolCallResult.Fail($"Timezone '{timezone}' está inválido."));
        }
    }
}

