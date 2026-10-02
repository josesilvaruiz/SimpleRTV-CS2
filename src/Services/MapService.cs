using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace SimpleRTV;

public class MapService
{
    private readonly ILogger _logger;
    private Dictionary<string, MapInfo> _maps = new();

    // Maps known to crash the server (physics/collision hangs, etc). Checked against
    // both the rtv_maps.json key (map name) and MapInfo.MapId (workshop id), because
    // workshop-sourced entries are keyed by id while static entries are keyed by name
    // — a single list can't rely on key shape alone.
    private HashSet<string> _blacklistIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _blacklistNames = new(StringComparer.OrdinalIgnoreCase);

    public MapService(ILogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyDictionary<string, MapInfo> Maps => _maps;
    public bool HasMaps => _maps.Count > 0;

    /// <summary>Última clave a la que cambió este plugin: el nombre real del mapa cargado puede diferir del título del Workshop (surf_anime vs surf_anime_fun).</summary>
    public string? LastRequestedKey { get; private set; }

    public void SetBlacklist(IEnumerable<string> workshopIds, IEnumerable<string> mapNames)
    {
        _blacklistIds = new HashSet<string>(workshopIds, StringComparer.OrdinalIgnoreCase);
        _blacklistNames = new HashSet<string>(mapNames, StringComparer.OrdinalIgnoreCase);
    }

    private bool IsBlacklisted(string key, MapInfo info) =>
        _blacklistNames.Contains(key) ||
        (!string.IsNullOrEmpty(info.MapId) && _blacklistIds.Contains(info.MapId));

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

            int removed = RemoveKeys(parsed, k => IsBlacklisted(k, parsed[k]));
            if (removed > 0)
                _logger.LogInformation("[SimpleRTV] {Count} blacklisted map(s) filtered out of rtv_maps.json.", removed);

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
    /// Blacklisted workshop ids are skipped entirely — this is the entry point that
    /// re-adds crashed maps every map change if they're still in the Steam collection,
    /// so filtering here (not just in rtv_maps.json) is what actually keeps them out.
    /// </summary>
    public void MergeWorkshopMaps(Dictionary<string, MapInfo> workshopMaps)
    {
        int added = 0, skipped = 0;
        foreach (var kv in workshopMaps)
        {
            if (IsBlacklisted(kv.Key, kv.Value))
            {
                skipped++;
                continue;
            }
            if (!_maps.ContainsKey(kv.Key))
            {
                _maps[kv.Key] = kv.Value;
                added++;
            }
        }
        if (added > 0 || skipped > 0)
            _logger.LogInformation("[SimpleRTV] Merged {Count} workshop maps ({Skipped} blacklisted skipped).", added, skipped);
    }

    private static int RemoveKeys(Dictionary<string, MapInfo> dict, Func<string, bool> predicate)
    {
        var toRemove = dict.Keys.Where(predicate).ToList();
        foreach (var k in toRemove) dict.Remove(k);
        return toRemove.Count;
    }

    /// <summary>Returns the display name for a map key, falling back to the key itself.</summary>
    public string GetDisplayName(string mapKey)
    {
        if (_maps.TryGetValue(mapKey, out var info) && !string.IsNullOrEmpty(info.Display))
            return info.Display;
        return mapKey;
    }

    /// <summary>
    /// Finds the list key for a map name. Static rtv_maps.json entries are keyed by name,
    /// but workshop-synced ones are keyed by id with the name only in Display — so a
    /// config value like "surf_utopia_njv" has to be matched against both.
    /// </summary>
    public string? ResolveKey(string mapName)
    {
        if (_maps.ContainsKey(mapName)) return mapName;
        return _maps.FirstOrDefault(kv =>
            kv.Key.Equals(mapName, StringComparison.OrdinalIgnoreCase) ||
            kv.Value.Display.Equals(mapName, StringComparison.OrdinalIgnoreCase)).Key;
    }

    /// <summary>Changes the map using the appropriate command (changelevel, host_workshop_map, ds_workshop_changelevel).</summary>
    public void ChangeMap(string mapKey)
    {
        if (!_maps.TryGetValue(mapKey, out var info))
        {
            _logger.LogError("[SimpleRTV] Map '{Map}' not found in the list.", mapKey);
            return;
        }

        LastRequestedKey = mapKey;

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
