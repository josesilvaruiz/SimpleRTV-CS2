using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SimpleRTV;

public class WorkshopService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ILogger _logger;

    public WorkshopService(ILogger logger) => _logger = logger;

    /// <summary>
    /// Returns workshop maps for the given collection, using a disk cache to avoid
    /// hitting the Steam API on every server restart.
    /// </summary>
    public async Task<Dictionary<string, MapInfo>> FetchAndCacheAsync(string collectionId, string cachePath, int cacheHours)
    {
        if (TryLoadCache(cachePath, cacheHours, out var cached))
        {
            _logger.LogInformation("[SimpleRTV] Workshop cache hit ({Count} maps).", cached.Count);
            return cached;
        }

        _logger.LogInformation("[SimpleRTV] Fetching workshop collection {Id} from Steam API...", collectionId);
        var maps = await FetchFromSteamAsync(collectionId);

        if (maps.Count > 0)
            SaveCache(cachePath, maps);

        return maps;
    }

    private async Task<Dictionary<string, MapInfo>> FetchFromSteamAsync(string collectionId)
    {
        // Step 1 — collection children
        var fileIds = await GetCollectionChildrenAsync(collectionId);
        if (fileIds.Count == 0) return new();

        // Step 2 — details for each map
        return await GetPublishedFileDetailsAsync(fileIds);
    }

    private async Task<List<string>> GetCollectionChildrenAsync(string collectionId)
    {
        try
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("collectioncount", "1"),
                new KeyValuePair<string, string>("publishedfileids[0]", collectionId)
            });

            var resp = await _http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", body);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var details = doc.RootElement
                .GetProperty("response")
                .GetProperty("collectiondetails")[0];

            if (!details.TryGetProperty("children", out var children))
                return new();

            var ids = new List<string>();
            foreach (var child in children.EnumerateArray())
                if (child.TryGetProperty("publishedfileid", out var id))
                    ids.Add(id.GetString()!);

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError("[SimpleRTV] Error fetching collection details: {Error}", ex.Message);
            return new();
        }
    }

    private async Task<Dictionary<string, MapInfo>> GetPublishedFileDetailsAsync(List<string> fileIds)
    {
        try
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new("itemcount", fileIds.Count.ToString())
            };
            for (int i = 0; i < fileIds.Count; i++)
                fields.Add(new($"publishedfileids[{i}]", fileIds[i]));

            var body = new FormUrlEncodedContent(fields);
            var resp = await _http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var files = doc.RootElement
                .GetProperty("response")
                .GetProperty("publishedfiledetails");

            var maps = new Dictionary<string, MapInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.EnumerateArray())
            {
                string id = file.GetProperty("publishedfileid").GetString()!;
                string title = file.TryGetProperty("title", out var t) ? t.GetString() ?? id : id;

                maps[id] = new MapInfo { WS = true, Display = title, MapId = id };
            }

            _logger.LogInformation("[SimpleRTV] Fetched {Count} maps from Steam API.", maps.Count);
            return maps;
        }
        catch (Exception ex)
        {
            _logger.LogError("[SimpleRTV] Error fetching file details: {Error}", ex.Message);
            return new();
        }
    }

    // ── Cache helpers ─────────────────────────────────────────────────────────

    private record CacheFile(DateTime FetchedAt, Dictionary<string, MapInfo> Maps);

    private bool TryLoadCache(string path, int maxAgeHours, out Dictionary<string, MapInfo> maps)
    {
        maps = new();
        if (!File.Exists(path)) return false;

        try
        {
            string json = File.ReadAllText(path);
            var cache = JsonSerializer.Deserialize<CacheFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (cache == null) return false;
            if ((DateTime.UtcNow - cache.FetchedAt).TotalHours > maxAgeHours) return false;

            maps = cache.Maps;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveCache(string path, Dictionary<string, MapInfo> maps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(
                new CacheFile(DateTime.UtcNow, maps),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[SimpleRTV] Could not save workshop cache: {Error}", ex.Message);
        }
    }
}
