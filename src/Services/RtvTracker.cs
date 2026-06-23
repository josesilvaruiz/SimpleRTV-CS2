using System;
using System.Collections.Generic;

namespace SimpleRTV;

/// <summary>
/// Lleva la cuenta de qué jugadores pidieron RTV.
/// Calcula cuántos votos faltan para alcanzar el porcentaje necesario.
/// </summary>
public class RtvTracker
{
    private readonly HashSet<int> _voters = new();

    public int VoteCount => _voters.Count;
    public bool HasVoted(int slot) => _voters.Contains(slot);

    /// <summary>Añade el voto del jugador. Devuelve false si ya había votado.</summary>
    public bool AddVote(int slot) => _voters.Add(slot);

    /// <summary>Elimina el voto del jugador (cuando se desconecta).</summary>
    public void RemoveVote(int slot) => _voters.Remove(slot);

    public void Reset() => _voters.Clear();

    /// <summary>
    /// Calcula cuántos votos más hacen falta para llegar al threshold.
    /// Devuelve 0 o negativo si ya se alcanzó.
    /// </summary>
    public int NeededVotes(int totalPlayers, float threshold)
    {
        if (totalPlayers == 0) return 1;
        int required = (int)Math.Ceiling(totalPlayers * threshold);
        return required - _voters.Count;
    }
}
