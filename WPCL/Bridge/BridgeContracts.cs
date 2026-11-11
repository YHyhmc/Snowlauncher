using System.Text.Json;
using System.Text.Json.Serialization;

namespace WPCL.Bridge;

public sealed record BridgeRequest(string Id, string Action, JsonElement? Payload);

public sealed record BridgeResponse(string Type, string Id, bool Ok, object? Data, string? Error);

public sealed record BridgeEvent(string Type, string Event, object? Data);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
