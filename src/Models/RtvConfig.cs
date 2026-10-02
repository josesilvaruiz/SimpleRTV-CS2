using System.Collections.Generic;
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
    public int MapsInVote { get; set; } = 6;

    [JsonPropertyName("MapsFile")]
    public string MapsFile { get; set; } = "rtv_maps.json";

    [JsonPropertyName("TriggerSecondsBeforeEnd")]
    public int TriggerSecondsBeforeEnd { get; set; } = 120;

    /// <summary>Steam Workshop Collection ID to auto-populate the map list. Leave empty to use only rtv_maps.json.</summary>
    [JsonPropertyName("WorkshopCollectionId")]
    public string WorkshopCollectionId { get; set; } = "";

    /// <summary>How long (hours) to keep the workshop map cache before re-fetching from Steam API.</summary>
    [JsonPropertyName("WorkshopCacheHours")]
    public int WorkshopCacheHours { get; set; } = 24;

    /// <summary>
    /// Workshop IDs to always exclude from RTV/nominate, regardless of rtv_maps.json
    /// or the workshop collection sync (e.g. maps known to crash the server).
    /// Matched against both the merged workshop entries (keyed by ID) and any
    /// static rtv_maps.json entry whose "mapid" matches.
    /// </summary>
    [JsonPropertyName("BlacklistedWorkshopIds")]
    public List<string> BlacklistedWorkshopIds { get; set; } = new();

    /// <summary>Map names (rtv_maps.json keys) to always exclude, for non-workshop entries.</summary>
    [JsonPropertyName("BlacklistedMapNames")]
    public List<string> BlacklistedMapNames { get; set; } = new();

    /// <summary>Mapa de CS2 en el que arranca el servidor; solo en ese mapa se salta solo a uno aleatorio del pool.</summary>
    [JsonPropertyName("BootMap")]
    public string BootMap { get; set; } = "de_dust2";
}
