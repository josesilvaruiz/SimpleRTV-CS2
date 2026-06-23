using System.Text.Json.Serialization;

namespace SimpleRTV;

public class MapInfo
{
    [JsonPropertyName("ws")]
    public bool WS { get; set; } = false;

    [JsonPropertyName("display")]
    public string Display { get; set; } = "";

    [JsonPropertyName("mapid")]
    public string MapId { get; set; } = "";
}
