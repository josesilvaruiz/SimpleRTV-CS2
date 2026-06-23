using System.Text;
using CounterStrikeSharp.API;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace SimpleRTV;

/// <summary>
/// Plugin principal. Registra los comandos y listeners,
/// y coordina los servicios (MapService, RtvTracker, MapVote).
/// </summary>
public class SimpleRtvPlugin : BasePlugin, IPluginConfig<RtvConfig>
{
    public override string ModuleName => "SimpleRTV";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "josea";
    public override string ModuleDescription => "Simple RTV for CS2";

    public RtvConfig Config { get; set; } = new();

    private readonly IStringLocalizer<SimpleRtvPlugin> _localizer;
    private MapService _mapService = null!;
    private RtvTracker _rtvTracker = null!;
    private MapVote _mapVote = null!;
    private NominateService _nominate = null!;

    private readonly WasdMenuManager _wasdMenu = new();
    private PlayerPrefsDb _db = null!;

    private bool _rtvAllowed = false;
    private DateTime _mapStartTime = DateTime.MinValue;
    private readonly Random _rng = new();

    private string? _pendingMap = null;
    private bool _voteIsAuto = false;
    private Timer? _scoreboardTimer = null;
    private readonly HashSet<int> _chatModeSlots = new();

    private static string Prefix => $" {ChatColors.Green}[RTV]{ChatColors.Default}";

    public SimpleRtvPlugin(IStringLocalizer<SimpleRtvPlugin> localizer)
    {
        _localizer = localizer;
    }

    public void OnConfigParsed(RtvConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        _mapService = new MapService(Logger);
        _rtvTracker = new RtvTracker();
        _mapVote = new MapVote();
        _nominate = new NominateService();

        string dbPath = Path.Combine(ModuleDirectory, "..", "..", "data", "SimpleRTV", "prefs.db");
        _db = new PlayerPrefsDb(dbPath);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);
        RegisterListener<Listeners.OnTick>(_wasdMenu.OnTick);
        RegisterEventHandler<EventPlayerActivate>(OnPlayerActivate);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        AddCommandListener("say", OnPlayerSay);
        AddCommandListener("say_team", OnPlayerSay);

        if (hotReload)
        {
            _mapService.Load(GetMapsFilePath());
            _rtvAllowed = true;
            foreach (var p in GetValidPlayers())
                _wasdMenu.RegisterPlayer(p);
        }
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);
        RemoveListener<Listeners.OnTick>(_wasdMenu.OnTick);
        DeregisterEventHandler<EventPlayerActivate>(OnPlayerActivate);
        DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RemoveCommandListener("say", OnPlayerSay, HookMode.Pre);
        RemoveCommandListener("say_team", OnPlayerSay, HookMode.Pre);
        _wasdMenu.CloseAll();
    }

    // ── Listeners / Eventos ────────────────────────────────────────────────────

    private HookResult OnPlayerActivate(EventPlayerActivate @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        _wasdMenu.RegisterPlayer(player);

        string steamId = player.SteamID.ToString();
        int slot = player.Slot;
        _db.LoadChatModeAsync(steamId).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result)
                Server.NextFrame(() => _chatModeSlots.Add(slot));
        });

        return HookResult.Continue;
    }

    private void OnMapStart(string _)
    {
        _rtvTracker.Reset();
        _mapVote.Reset();
        _nominate.Reset();
        _rtvAllowed = false;
        _pendingMap = null;
        _mapStartTime = DateTime.Now;

        _mapService.Load(GetMapsFilePath());

        if (Config.RtvDelaySeconds > 0)
            AddTimer(Config.RtvDelaySeconds, () => _rtvAllowed = true, TimerFlags.STOP_ON_MAPCHANGE);
        else
            _rtvAllowed = true;

        // Leer mp_timelimit con un pequeño delay para que el mapa haya terminado de cargar
        AddTimer(3f, ScheduleTimeLimitTimers, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ScheduleTimeLimitTimers()
    {
        var mpTimelimit = ConVar.Find("mp_timelimit");
        float timeLimitMinutes = mpTimelimit?.GetPrimitiveValue<float>() ?? 0f;
        Logger.LogInformation("[SimpleRTV] mp_timelimit leído: {Val}", timeLimitMinutes);

        if (timeLimitMinutes <= 0 || Config.TriggerSecondsBeforeEnd <= 0) return;

        float totalSeconds = timeLimitMinutes * 60f;
        float autoVoteDelay = totalSeconds - Config.TriggerSecondsBeforeEnd;

        if (autoVoteDelay > 0)
            AddTimer(autoVoteDelay, StartAutoVote, TimerFlags.STOP_ON_MAPCHANGE);

        // Fuerza el cambio de mapa cuando el timelimit llega a 0
        AddTimer(totalSeconds, ForceMapChange, TimerFlags.STOP_ON_MAPCHANGE);

        Logger.LogInformation("[SimpleRTV] Timelimit: {Min}min. Auto-vote en {Delay}s, forced change en {Total}s.",
            timeLimitMinutes, autoVoteDelay, totalSeconds);
    }

    private void OnClientDisconnect(int slot)
    {
        _wasdMenu.UnregisterPlayer(slot);
        _nominate.Remove(slot);
        _chatModeSlots.Remove(slot);
        bool hadVote = _rtvTracker.HasVoted(slot);
        _rtvTracker.RemoveVote(slot);

        if (hadVote && !_mapVote.IsInProgress && _rtvAllowed)
        {
            int needed = _rtvTracker.NeededVotes(GetValidPlayers().Count(), Config.RtvThreshold);
            if (needed <= 0) StartVote(auto: false);
        }
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (_pendingMap != null)
        {
            string map = _pendingMap;
            _pendingMap = null;
            PrintToAll("rtv.changing_now", _mapService.GetDisplayName(map));
            AddTimer(3f, () => _mapService.ChangeMap(map), TimerFlags.STOP_ON_MAPCHANGE);
        }
        return HookResult.Continue;
    }

    // ── Comandos ───────────────────────────────────────────────────────────────

    [ConsoleCommand("rtv", "Vota para cambiar el mapa")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRtvCommand(CCSPlayerController? caller, CommandInfo _)
    {
        if (caller == null || !caller.IsValid) return;

        if (!_rtvAllowed)
        {
            PrintToPlayer(caller, "rtv.not_available");
            return;
        }
        if (_mapVote.IsInProgress)
        {
            PrintToPlayer(caller, "rtv.vote_in_progress");
            return;
        }
        if (_rtvTracker.HasVoted(caller.Slot))
        {
            int needed = _rtvTracker.NeededVotes(GetValidPlayers().Count(), Config.RtvThreshold);
            PrintToPlayer(caller, "rtv.already_voted", needed);
            return;
        }

        _rtvTracker.AddVote(caller.Slot);
        int still = _rtvTracker.NeededVotes(GetValidPlayers().Count(), Config.RtvThreshold);

        if (still <= 0)
            StartVote(auto: false);
        else
            PrintToAll("rtv.player_wants_change", caller.PlayerName, still);
    }

    [ConsoleCommand("timeleft", "Muestra el tiempo restante en el mapa")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTimeleftCommand(CCSPlayerController? caller, CommandInfo _)
    {
        if (caller == null || !caller.IsValid) return;

        var mpTimelimit = ConVar.Find("mp_timelimit");
        float timeLimitMinutes = mpTimelimit?.GetPrimitiveValue<float>() ?? 0f;

        if (timeLimitMinutes <= 0 || _mapStartTime == DateTime.MinValue)
        {
            PrintToPlayer(caller, "timeleft.no_limit");
            return;
        }

        int elapsed = (int)(DateTime.Now - _mapStartTime).TotalSeconds;
        int remaining = (int)(timeLimitMinutes * 60) - elapsed;

        if (remaining <= 0)
        {
            PrintToPlayer(caller, "timeleft.ended");
            return;
        }

        int minutes = remaining / 60;
        int seconds = remaining % 60;
        PrintToPlayer(caller, "timeleft.remaining", minutes, $"{seconds:D2}");
    }

    [ConsoleCommand("nominate", "Nomina un mapa para el próximo voto")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNominateCommand(CCSPlayerController? caller, CommandInfo _)
    {
        if (caller == null || !caller.IsValid) return;

        if (!_rtvAllowed)
        {
            PrintToPlayer(caller, "nominate.unavailable");
            return;
        }
        if (_mapVote.IsInProgress)
        {
            PrintToPlayer(caller, "nominate.vote_started");
            return;
        }

        var available = _mapService.Maps
            .Where(kv => !kv.Key.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (available.Count == 0) return;

        string currentNom = _nominate.GetNomination(caller.Slot) ?? "";

        var menu = _wasdMenu.CreateMenu(_localizer["nominate.menu_title"]);
        foreach (var kv in available)
        {
            string mapKey = kv.Key;
            string label = string.IsNullOrEmpty(kv.Value.Display) ? kv.Key : kv.Value.Display;
            string suffix = mapKey == currentNom ? " ✓" : "";

            menu.Add(label + suffix, (player, _) =>
            {
                bool alreadyNominated = _nominate.Nominations.Values
                    .Any(v => v.Equals(mapKey, StringComparison.OrdinalIgnoreCase)
                              && _nominate.GetNomination(player.Slot) != mapKey);

                if (alreadyNominated)
                {
                    PrintToPlayer(player, "nominate.already", label);
                    return;
                }

                bool isChange = _nominate.Nominate(player.Slot, mapKey);
                PrintToAll(isChange ? "nominate.changed" : "nominate.success", player.PlayerName, label);
                _wasdMenu.CloseMenu(player);
            });
        }
        _wasdMenu.OpenMenu(caller, menu);
    }

    [ConsoleCommand("votemode", "Alterna entre menú WASD y voto por chat")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnVoteModeCommand(CCSPlayerController? caller, CommandInfo _)
    {
        if (caller == null || !caller.IsValid) return;

        if (_mapVote.IsInProgress && _mapVote.HasVoted(caller.Slot))
        {
            PrintToPlayer(caller, "votemode.already_voted");
            return;
        }

        bool switchToChat = !_chatModeSlots.Contains(caller.Slot);

        if (switchToChat)
        {
            _chatModeSlots.Add(caller.Slot);
            PrintToPlayer(caller, "votemode.to_chat");
            if (_mapVote.IsInProgress)
            {
                _wasdMenu.CloseMenu(caller);
                PrintChatVoteOptions(caller);
            }
        }
        else
        {
            _chatModeSlots.Remove(caller.Slot);
            PrintToPlayer(caller, "votemode.to_menu");
            if (_mapVote.IsInProgress && _mapVote.ActiveMenu != null)
                _wasdMenu.OpenMenu(caller, _mapVote.ActiveMenu);
        }

        FireAndForget(_db.SaveChatModeAsync(caller.SteamID.ToString(), switchToChat));
    }

    [ConsoleCommand("css_frtv", "Fuerza el inicio de una votación de mapa (solo root)")]
    [RequiresPermissions("@css/root")]
    public void OnForceRtvCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (_mapVote.IsInProgress)
        {
            if (caller != null)
                PrintToPlayer(caller, "rtv.vote_in_progress");
            else
                info.ReplyToCommand($"[RTV] Vote already in progress.");
            return;
        }

        string name = caller?.PlayerName ?? "Console";
        Server.PrintToChatAll($"{Prefix} Admin {ChatColors.Red}{name}{ChatColors.Default} forced a map vote.");
        StartVote(auto: false);
    }

    [ConsoleCommand("nomlist", "Muestra los mapas nominados")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNomlistCommand(CCSPlayerController? caller, CommandInfo _)
    {
        if (caller == null || !caller.IsValid) return;

        var noms = _nominate.GetNominatedMaps();

        using (new WithTemporaryCulture(caller.GetLanguage()))
        {
            if (noms.Count == 0)
            {
                PrintToPlayer(caller, "nomlist.empty");
                return;
            }

            caller.PrintToChat($"{Prefix} {_localizer["nomlist.header"]}");
            foreach (var mapKey in noms)
                caller.PrintToChat($"  {ChatColors.Yellow}{_mapService.GetDisplayName(mapKey)}{ChatColors.Default}");
        }
    }

    // ── Lógica de votación ─────────────────────────────────────────────────────

    private void StartAutoVote()
    {
        if (_mapVote.IsInProgress || _pendingMap != null) return;
        PrintToAll("rtv.auto_vote_started");
        StartVote(auto: true);
    }

    private void StartVote(bool auto)
    {
        if (_mapVote.IsInProgress) return;
        _voteIsAuto = auto;

        if (!_mapService.HasMaps)
        {
            PrintToAll("rtv.no_maps");
            return;
        }

        // Nominados tienen prioridad; el resto se rellena aleatoriamente
        var nominatedKeys = _nominate.GetNominatedMaps()
            .Where(k => !k.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase))
            .Take(Config.MapsInVote)
            .ToList();

        var nominatedCandidates = nominatedKeys
            .Where(k => _mapService.Maps.ContainsKey(k))
            .Select(k => new KeyValuePair<string, MapInfo>(k, _mapService.Maps[k]))
            .ToList();

        var randomCandidates = _mapService.Maps
            .Where(kv => !kv.Key.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase)
                         && !nominatedKeys.Contains(kv.Key))
            .OrderBy(_ => _rng.Next())
            .Take(Config.MapsInVote - nominatedCandidates.Count)
            .ToList();

        var candidates = nominatedCandidates.Concat(randomCandidates).ToList();

        if (candidates.Count == 0)
            candidates = _mapService.Maps.OrderBy(_ => _rng.Next()).Take(Config.MapsInVote).ToList();

        if (!auto)
            PrintToAll("rtv.vote_started", Config.VoteSeconds);

        var allPlayers = GetValidPlayers().ToList();
        var menuPlayers = allPlayers.Where(p => !_chatModeSlots.Contains(p.Slot));
        var chatPlayers = allPlayers.Where(p => _chatModeSlots.Contains(p.Slot));

        _mapVote.Start(
            candidates,
            Config.VoteSeconds,
            AddTimer,
            menuPlayers,
            _localizer,
            _wasdMenu,
            OnVoteEnd);

        foreach (var p in chatPlayers)
            PrintChatVoteOptions(p);

        _scoreboardTimer?.Kill();
        _scoreboardTimer = AddTimer(1.0f, UpdateVoteScoreboard, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OnVoteEnd(string? winnerKey)
    {
        _scoreboardTimer?.Kill();
        _scoreboardTimer = null;
        ClearScoreboardForAll();

        if (winnerKey == null)
        {
            PrintToAll("rtv.nobody_voted");
            return;
        }

        string display = _mapService.GetDisplayName(winnerKey);

        if (_voteIsAuto)
        {
            // Votación automática: guardar y cambiar al final de ronda (o cuando expire el timelimit)
            _pendingMap = winnerKey;
            PrintToAll("rtv.vote_ended", display);
        }
        else
        {
            // Votación manual (RTV): cambiar inmediatamente
            PrintToAll("rtv.changing_now", display);
            AddTimer(5f, () => _mapService.ChangeMap(winnerKey), TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    /// <summary>
    /// Se llama cuando el timelimit llega a 0.
    /// Si hay un mapa votado, cambia a ese. Si no, elige uno aleatorio.
    /// </summary>
    private void ForceMapChange()
    {
        // Si ya hay un mapa pendiente (de la votación automática), OnRoundEnd lo cambiará
        if (_pendingMap != null) return;

        // Sin votación: elegir mapa aleatorio y esperar a OnRoundEnd
        var random = _mapService.Maps
            .Where(kv => !kv.Key.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => _rng.Next())
            .FirstOrDefault();

        if (random.Key == null) return;

        _pendingMap = random.Key;
        PrintToAll("rtv.timelimit_no_vote");
    }

    // ── Voto por chat ─────────────────────────────────────────────────────────

    private HookResult OnPlayerSay(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null || !caller.IsValid) return HookResult.Continue;
        if (!_mapVote.IsInProgress) return HookResult.Continue;
        if (!_chatModeSlots.Contains(caller.Slot)) return HookResult.Continue;
        if (_mapVote.HasVoted(caller.Slot)) return HookResult.Continue;

        string message = info.GetArg(1).Trim('"', ' ');
        if (!int.TryParse(message, out int num)) return HookResult.Continue;

        var candidates = _mapVote.Candidates;
        int idx = num - 1;
        if (idx < 0 || idx >= candidates.Count) return HookResult.Continue;

        string mapKey = candidates[idx].Key;
        string label = string.IsNullOrEmpty(candidates[idx].Value.Display)
            ? candidates[idx].Key
            : candidates[idx].Value.Display;

        if (_mapVote.TryVote(caller.Slot, mapKey))
        {
            PrintToPlayer(caller, "rtv.voted_for", label);
            return HookResult.Handled; // no mostrar el número en el chat público
        }

        return HookResult.Continue;
    }

    private void PrintChatVoteOptions(CCSPlayerController player)
    {
        PrintToPlayer(player, "votemode.options_header");
        var candidates = _mapVote.Candidates;
        for (int i = 0; i < candidates.Count; i++)
        {
            string label = string.IsNullOrEmpty(candidates[i].Value.Display)
                ? candidates[i].Key
                : candidates[i].Value.Display;
            PrintToPlayer(player, "votemode.option", i + 1, label);
        }
    }

    // ── Scoreboard en vivo ────────────────────────────────────────────────────

    private void UpdateVoteScoreboard()
    {
        if (!_mapVote.IsInProgress) return;
        string html = BuildScoreboardHtml();
        foreach (var player in GetValidPlayers())
            if (!_wasdMenu.HasActiveMenu(player.Slot))
                player.PrintToCenterHtml(html);
    }

    private string BuildScoreboardHtml()
    {
        var votes = _mapVote.Votes;
        int maxVotes = votes.Values.DefaultIfEmpty(0).Max();

        var sb = new StringBuilder();
        sb.Append("<div>");
        sb.Append("<b><font color='#ff4444' class='fontSize-m'>Votación de mapa</font></b><br>");

        foreach (var kv in votes.OrderByDescending(v => v.Value))
        {
            string display = _mapService.GetDisplayName(kv.Key);
            int count = kv.Value;
            string bar = count > 0 ? new string('█', Math.Min(count * 3, 12)) : "░░░░░░";
            string nameColor = count == maxVotes && count > 0 ? "#ffcc00" : "white";
            sb.Append($"<font color='{nameColor}' class='fontSize-m'>{display}</font>  <font color='#88ff88'>{bar}</font>  <font color='white'>{count}</font><br>");
        }

        int totalVotes = votes.Values.Sum();
        sb.Append($"<br><font color='gray' class='fontSize-sm'>Ya votaste ✓  |  Votos: {totalVotes}</font>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private void ClearScoreboardForAll()
    {
        foreach (var player in GetValidPlayers())
            if (!_wasdMenu.HasActiveMenu(player.Slot))
                player.PrintToCenterHtml(" ");
    }

    // ── Helpers de localización ────────────────────────────────────────────────

    private void PrintToPlayer(CCSPlayerController player, string key, params object[] args)
    {
        using (new WithTemporaryCulture(player.GetLanguage()))
            player.PrintToChat($"{Prefix} {_localizer[key, args]}");
    }

    private void PrintToAll(string key, params object[] args)
    {
        foreach (var player in GetValidPlayers())
            PrintToPlayer(player, key, args);
    }

    // ── Helpers generales ──────────────────────────────────────────────────────

    private static void FireAndForget(Task _) { }

    private string GetMapsFilePath() =>
        Path.Combine(Server.GameDirectory, "csgo", Config.MapsFile);

    private IEnumerable<CCSPlayerController> GetValidPlayers() =>
        Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV);
}
