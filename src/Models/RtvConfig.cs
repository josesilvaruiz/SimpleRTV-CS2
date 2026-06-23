using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace SimpleRTV;

/// <summary>
/// Configuración del plugin. Se guarda automáticamente en
/// configs/plugins/SimpleRTV/SimpleRTV.json
/// </summary>
public class RtvConfig : BasePluginConfig
{
    /// <summary>Porcentaje de jugadores necesario para activar el voto (0.6 = 60%)</summary>
    [JsonPropertyName("RtvThreshold")]
    public float RtvThreshold { get; set; } = 0.6f;

    /// <summary>Segundos que dura la votación de mapa</summary>
    [JsonPropertyName("VoteSeconds")]
    public int VoteSeconds { get; set; } = 30;

    /// <summary>Segundos tras el inicio del mapa antes de que RTV esté disponible</summary>
    [JsonPropertyName("RtvDelaySeconds")]
    public int RtvDelaySeconds { get; set; } = 90;

    /// <summary>Número de mapas que aparecen en el menú de votación</summary>
    [JsonPropertyName("MapsInVote")]
    public int MapsInVote { get; set; } = 5;

    /// <summary>Ruta del archivo de mapas relativa a csgo/ (formato GGMCmaps.json)</summary>
    [JsonPropertyName("MapsFile")]
    public string MapsFile { get; set; } = "cfg/rtv_maps.json";

    /// <summary>Segundos antes del fin del timelimit para lanzar la votación automática (0 = desactivado)</summary>
    [JsonPropertyName("TriggerSecondsBeforeEnd")]
    public int TriggerSecondsBeforeEnd { get; set; } = 120;
}
