using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlagInjector;

sealed class FlagDto
{
    [JsonPropertyName("n")] public string Name    { get; set; } = "";
    [JsonPropertyName("v")] public string Value   { get; set; } = "";
    [JsonPropertyName("t")] public string Type    { get; set; } = "String";
    [JsonPropertyName("e")] public bool   Enabled { get; set; } = true;
    [JsonPropertyName("m")] public string Mode    { get; set; } = "OnJoin";
    [JsonPropertyName("h")] public List<FlagHistoryEntry> History { get; set; } = new();
}