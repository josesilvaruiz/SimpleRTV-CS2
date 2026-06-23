using System;
using System.Collections.Generic;

namespace SimpleRTV;

public class RtvTracker
{
    private readonly HashSet<int> _voters = new();

    public int VoteCount => _voters.Count;
    public bool HasVoted(int slot) => _voters.Contains(slot);

    /// <summary>Registers a player's RTV vote. Returns false if already voted.</summary>
    public bool AddVote(int slot) => _voters.Add(slot);

    /// <summary>Removes a player's vote, called on disconnect.</summary>
    public void RemoveVote(int slot) => _voters.Remove(slot);

    public void Reset() => _voters.Clear();

    /// <summary>Returns how many more votes are needed to reach the threshold. Zero or negative means it's already met.</summary>
    public int NeededVotes(int totalPlayers, float threshold)
    {
        if (totalPlayers == 0) return 1;
        int required = (int)Math.Ceiling(totalPlayers * threshold);
        return required - _voters.Count;
    }
}
