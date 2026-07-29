using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Protocol.Handwritten.Json;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }
}

