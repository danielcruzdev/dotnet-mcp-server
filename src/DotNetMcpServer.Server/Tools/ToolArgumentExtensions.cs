using System.Text.Json.Nodes;

namespace DotNetMcpServer.Server.Tools;

internal static class ToolArgumentExtensions
{
    public static string? GetString(this JsonObject arguments, string name)
    {
        return arguments[name]?.GetValue<string>();
    }

    public static int GetInt(this JsonObject arguments, string name, int fallback, int min, int max)
    {
        var value = arguments[name]?.GetValue<int?>();
        if (!value.HasValue)
        {
            return fallback;
        }

        return Math.Clamp(value.Value, min, max);
    }
}

