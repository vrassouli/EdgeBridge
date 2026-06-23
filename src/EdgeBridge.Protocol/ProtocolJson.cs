using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeBridge.Protocol;

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static JsonElement ToJsonElement<T>(T value)
    {
        return JsonSerializer.SerializeToElement(value, Options);
    }

    public static T? ReadPayload<T>(JsonElement payload)
    {
        return payload.Deserialize<T>(Options);
    }
}

