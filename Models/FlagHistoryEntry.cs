using System.Text.Json.Serialization;

namespace FlagInjector;

sealed class FlagHistoryEntry
{
    [JsonPropertyName("ts")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("ov")] public string OldValue   { get; set; } = "";
    [JsonPropertyName("nv")] public string NewValue   { get; set; } = "";
}