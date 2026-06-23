using System.Collections.Generic;
using System.Linq;

namespace SimpleRTV;

/// <summary>
/// Lleva la cuenta de qué mapa ha nominado cada jugador.
/// Los mapas nominados tienen prioridad en el menú de votación.
/// Cada jugador solo puede tener una nominación activa a la vez.
/// </summary>
public class NominateService
{
    // slot del jugador → clave del mapa nominado
    private readonly Dictionary<int, string> _nominations = new();

    public IReadOnlyDictionary<int, string> Nominations => _nominations;

    /// <summary>Devuelve la nominación actual del jugador, o null si no tiene.</summary>
    public string? GetNomination(int slot) =>
        _nominations.TryGetValue(slot, out var map) ? map : null;

    /// <summary>
    /// Registra o actualiza la nominación del jugador.
    /// Devuelve true si estaba cambiando una nominación previa.
    /// </summary>
    public bool Nominate(int slot, string mapKey)
    {
        bool isChange = _nominations.ContainsKey(slot);
        _nominations[slot] = mapKey;
        return isChange;
    }

    /// <summary>Elimina la nominación del jugador (al desconectarse).</summary>
    public void Remove(int slot) => _nominations.Remove(slot);

    public void Reset() => _nominations.Clear();

    /// <summary>Lista de mapas nominados (sin duplicados, en orden de llegada).</summary>
    public List<string> GetNominatedMaps() =>
        _nominations.Values.Distinct().ToList();
}
