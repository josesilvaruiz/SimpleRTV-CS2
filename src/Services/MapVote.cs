using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace SimpleRTV;

public class MapVote
{
    private readonly Dictionary<string, int> _votes = new();
    private readonly HashSet<int> _voters = new();
    private List<KeyValuePair<string, MapInfo>> _candidates = new();
    private readonly Dictionary<string, WasdMenuOption> _optionByMap = new();
    private WasdMenu? _activeMenu;
    private Timer? _timer = null;

    public bool IsInProgress { get; private set; }
    public IReadOnlyDictionary<string, int> Votes => _votes;
    public IReadOnlyList<KeyValuePair<string, MapInfo>> Candidates => _candidates;
    public WasdMenu? ActiveMenu => _activeMenu;

    public bool HasVoted(int slot) => _voters.Contains(slot);

    /// <summary>Registers a vote for a map. Returns false if the player already voted or the map key is invalid.</summary>
    public bool TryVote(int slot, string mapKey)
    {
        if (_voters.Contains(slot)) return false;
        if (!_votes.ContainsKey(mapKey)) return false;
        _voters.Add(slot);
        _votes[mapKey]++;
        if (_optionByMap.TryGetValue(mapKey, out var opt))
            opt.Count = _votes[mapKey];
        return true;
    }

    private static string Prefix => $" {ChatColors.Green}[RTV]{ChatColors.Default}";

    public void Start(
        List<KeyValuePair<string, MapInfo>> candidates,
        int voteSeconds,
        Func<float, Action, TimerFlags?, Timer> addTimer,
        IEnumerable<CCSPlayerController> menuPlayers,
        IStringLocalizer localizer,
        WasdMenuManager wasdManager,
        Action<string?> onVoteEnd)
    {
        if (IsInProgress) return;

        IsInProgress = true;
        _votes.Clear();
        _voters.Clear();
        _optionByMap.Clear();
        _candidates = candidates;

        foreach (var kv in candidates)
            _votes[kv.Key] = 0;

        var menu = wasdManager.CreateMenu(localizer["vote.menu_title"]);
        menu.DisplayOptionsCount = true;
        _activeMenu = menu;

        foreach (var kv in candidates)
        {
            string mapKey = kv.Key;
            string label = string.IsNullOrEmpty(kv.Value.Display) ? kv.Key : kv.Value.Display;

            menu.Add(label, (player, opt) =>
            {
                if (!TryVote(player.Slot, mapKey)) return;
                opt.Count = _votes[mapKey];
                wasdManager.CloseMenu(player);
                using (new WithTemporaryCulture(player.GetLanguage()))
                    player.PrintToChat($"{Prefix} {localizer["rtv.voted_for", label]}");
            });

            // keep a reference to update the count when other players vote
            _optionByMap[mapKey] = menu.Options.Last!.Value;
        }

        foreach (var player in menuPlayers)
            wasdManager.OpenMenu(player, menu);

        _timer = addTimer(voteSeconds, () =>
        {
            string? winner = PickWinner();
            Reset();
            onVoteEnd(winner);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Reset()
    {
        _votes.Clear();
        _voters.Clear();
        _optionByMap.Clear();
        _candidates = new();
        _activeMenu = null;
        _timer?.Kill();
        _timer = null;
        IsInProgress = false;
    }

    private string? PickWinner()
    {
        if (_votes.Count == 0 || _votes.Values.All(v => v == 0))
            return null;

        return _votes.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>Returns the map currently in the lead while a vote is in progress, or null if nobody has voted yet.</summary>
    public string? CurrentLeader() => PickWinner();
}
