using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace SimpleRTV;

/// <summary>
/// Carga la lista de mapas desde el JSON y ejecuta el cambio de mapa
/// con el comando correcto según si es workshop o no.
/// </summary>
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
            _logger.LogWarning("[SimpleRTV] Archivo de mapas no encontrado: {Path}", filePath);
            return;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, MapInfo>>(json);

            if (parsed == null || parsed.Count == 0)
            {
                _logger.LogError("[SimpleRTV] El archivo de mapas está vacío o tiene formato incorrecto.");
                return;
            }

            _maps = parsed;
            _logger.LogInformation("[SimpleRTV] {Count} mapas cargados.", _maps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError("[SimpleRTV] Error leyendo el archivo de mapas: {Error}", ex.Message);
        }
    }

    /// <summary>Devuelve el nombre a mostrar del mapa, o la clave si no tiene display.</summary>
    public string GetDisplayName(string mapKey)
    {
        if (_maps.TryGetValue(mapKey, out var info) && !string.IsNullOrEmpty(info.Display))
            return info.Display;
        return mapKey;
    }

    /// <summary>Cambia el mapa usando el comando adecuado (changelevel, host_workshop_map, ds_workshop_changelevel).</summary>
    public void ChangeMap(string mapKey)
    {
        if (!_maps.TryGetValue(mapKey, out var info))
        {
            _logger.LogError("[SimpleRTV] No se encontró el mapa '{Map}' en la lista.", mapKey);
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
