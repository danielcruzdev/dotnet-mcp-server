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
    [McpServerTool(
        Name = "get_current_datetime",
        ReadOnly = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the current date and time, optionally converted to a specific timezone.")]
    public static CurrentDateTime GetCurrentDateTime(
        [Description("IANA or Windows timezone id, e.g. America/Sao_Paulo. Omit for UTC.")] string? timezone = null)
    {
        return Describe(DateTimeOffset.UtcNow, timezone);
    }

    /// <summary>
    /// The tool's logic, with the clock passed in. Tests call this directly.
    /// </summary>
    internal static CurrentDateTime Describe(DateTimeOffset utcNow, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return new CurrentDateTime(
                "UTC",
                utcNow.ToString("O", CultureInfo.InvariantCulture),
                utcNow.ToString("dddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture));
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

        return new CurrentDateTime(
            timezoneInfo.Id,
            converted.ToString("O", CultureInfo.InvariantCulture),
            converted.ToString("dddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// What <c>get_current_datetime</c> returns: the same instant in a machine-readable form and a
/// human-readable one, plus the zone it was resolved in.
/// </summary>
/// <remarks>
/// Both renderings are returned deliberately. A model reading <c>Formatted</c> and a caller
/// parsing <c>Iso8601</c> want different things from the same answer, and returning only the
/// prose version is what forces the caller to parse it back.
/// </remarks>
public sealed record CurrentDateTime(
    [property: Description("The timezone the time was resolved in, e.g. UTC or America/Sao_Paulo.")] string TimeZone,
    [property: Description("The instant in ISO 8601 round-trip form.")] string Iso8601,
    [property: Description("The same instant, written for a person to read.")] string Formatted);
