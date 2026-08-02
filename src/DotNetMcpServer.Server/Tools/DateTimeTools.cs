using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Tools;

/// <summary>
/// Clock tools. Kept free of ambient state so the behaviour is reproducible in tests.
/// </summary>
[McpServerToolType]
public static class DateTimeTools
{
    [McpServerTool(Name = "get_current_datetime")]
    [Description("Returns the current date and time, optionally converted to a specific timezone.")]
    public static string GetCurrentDateTime(
        [Description("IANA or Windows timezone id, e.g. America/Sao_Paulo. Omit for UTC.")] string? timezone = null)
    {
        return Describe(DateTimeOffset.UtcNow, timezone);
    }

    /// <summary>
    /// The tool's logic, with the clock passed in. Tests call this directly.
    /// </summary>
    internal static string Describe(DateTimeOffset utcNow, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return $"UTC now: {utcNow:O}";
        }

        TimeZoneInfo timezoneInfo;
        try
        {
            timezoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new McpException($"Timezone '{timezone}' was not found on this system.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new McpException($"Timezone '{timezone}' is invalid.");
        }

        var converted = TimeZoneInfo.ConvertTime(utcNow, timezoneInfo);
        var formatted = converted.ToString("dddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);

        return $"{timezoneInfo.Id}: {formatted}";
    }
}
