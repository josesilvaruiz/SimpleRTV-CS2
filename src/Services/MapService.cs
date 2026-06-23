using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace SimpleRTV;

public class MapService
{
    private readonly ILogger _logger;
    private Dictionary<string, MapInfo> _maps = new();

    public MapService(ILogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyDictionary<string, MapInfo> Maps => _maps;
    public bool HasMaps => _maps.Count > 0;

    public void Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("[SimpleRTV] Map file not found: {Path}", filePath);
            return;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, MapInfo>>(json);

            if (parsed == null || parsed.Count == 0)
            {
                _logger.LogError("[SimpleRTV] Map file is empty or has an invalid format.");
                return;
            }

            _maps = parsed;
            _logger.LogInformation("[SimpleRTV] {Count} maps loaded.", _maps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError("[SimpleRTV] Error reading map file: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Merges workshop maps into the current map list.
    /// Existing keys from rtv_maps.json are NOT overwritten (static file takes precedence).
    /// </summary>
    public void MergeWorkshopMaps(Dictionary<string, MapInfo> workshopMaps)
    {
        int added = 0;
        foreach (var kv in workshopMaps)
            if (!_maps.ContainsKey(kv.Key))
            {
                _maps[kv.Key] = kv.Value;
                added++;
            }
        if (added > 0)
            _logger.LogInformation("[SimpleRTV] Merged {Count} workshop maps.", added);
    }

    /// <summary>Returns the display name for a map key, falling back to the key itself.</summary>
    public string GetDisplayName(string mapKey)
    {
        if (_maps.TryGetValue(mapKey, out var info) && !string.IsNullOrEmpty(info.Display))
            return info.Display;
        return mapKey;
    }

    /// <summary>Changes the map using the appropriate command (changelevel, host_workshop_map, ds_workshop_changelevel).</summary>
    public void ChangeMap(string mapKey)
    {
        if (!_maps.TryGetValue(mapKey, out var info))
        {
            _logger.LogError("[SimpleRTV] Map '{Map}' not found in the list.", mapKey);
            return;
        }

        if (info.WS)
        {
            if (!string.IsNullOrEmpty(info.MapId))
                Server.ExecuteCommand($"host_workshop_map {info.MapId}");
            else
                Server.ExecuteCommand($"ds_workshop_changelevel {mapKey}");
        }
        else
        {
            Server.ExecuteCommand($"changelevel {mapKey}");
        }
    }
}
