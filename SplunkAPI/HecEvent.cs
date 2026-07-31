using System.Text.Json;
using System.Text.Json.Serialization;
namespace SplunkAPI;

class HecEvent
{
    [JsonPropertyName("event")]
    public required JsonElement Event { get; init; }

    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Index { get; init; }

    [JsonPropertyName("sourcetype")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sourcetype { get; init; }

    [JsonPropertyName("time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Time { get; init; }
}