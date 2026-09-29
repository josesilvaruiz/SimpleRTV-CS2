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
    public int MapsInVote { get; set; } = 5;

    /// <summary>How many "minigame" (non-surf) maps to always include in each vote.</summary>
    [JsonPropertyName("MinigameSlotsInVote")]
    public int MinigameSlotsInVote { get; set; } = 4;

    /// <summary>How many surf maps (key starts with "surf_") to always include in each vote.</summary>
    [JsonPropertyName("SurfSlotsInVote")]
    public int SurfSlotsInVote { get; set; } = 2;

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

    /// <summary>
    /// If the server boots on this map (the vanilla CS2 fallback when no valid map is configured,
    /// e.g. after a crash/restart), auto-switch to DefaultFallbackMap instead. Leave either field
    /// empty to disable. Replaces the external surf-default-watchdog.sh cron approach — this fires
    /// immediately on map start instead of waiting for the next poll.
    /// </summary>
    [JsonPropertyName("DefaultFallbackTriggerMap")]
    public string DefaultFallbackTriggerMap { get; set; } = "de_dust2";

    /// <summary>Map key (must exist in rtv_maps.json / the synced workshop list) to switch to when DefaultFallbackTriggerMap loads.</summary>
    [JsonPropertyName("DefaultFallbackMap")]
    public string DefaultFallbackMap { get; set; } = "surf_utopia_njv";

    /// <summary>
    /// Minutes the server must stay with 0 human players before switching to IdleResetMap,
    /// so the next player to join lands on the default map instead of whatever was left
    /// over. 0 disables it. Requires sv_hibernate_when_empty 0 — a hibernating server
    /// doesn't run plugin timers, so the check never fires.
    /// </summary>
    [JsonPropertyName("IdleResetMinutes")]
    public int IdleResetMinutes { get; set; } = 15;

    /// <summary>Map name (rtv_maps.json key or workshop map name) to switch to after IdleResetMinutes empty. Empty disables it.</summary>
    [JsonPropertyName("IdleResetMap")]
    public string IdleResetMap { get; set; } = "surf_utopia_njv";
}
