using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace SimpleRTV;

public class RtvConfig : BasePluginConfig
{
    [JsonPropertyName("RtvThreshold")]
    public float RtvThreshold { get; set; } = 0.6f;

    [JsonPropertyName("VoteSeconds")]
    public int VoteSeconds { get; set; } = 30;

    [JsonPropertyName("RtvDelaySeconds")]
    public int RtvDelaySeconds { get; set; } = 90;

    [JsonPropertyName("MapsInVote")]
    public int MapsInVote { get; set; } = 5;

    [JsonPropertyName("MapsFile")]
    public string MapsFile { get; set; } = "rtv_maps.json";

    [JsonPropertyName("TriggerSecondsBeforeEnd")]
    public int TriggerSecondsBeforeEnd { get; set; } = 120;
}
