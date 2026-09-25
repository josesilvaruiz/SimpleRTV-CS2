using System.Runtime.InteropServices;
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

public class SimpleRtvPlugin : BasePlugin, IPluginConfig<RtvConfig>
{
    public override string ModuleName => "SimpleRTV";
    public override string ModuleVersion => "1.2.0";
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
    private WorkshopService _workshop = null!;

    private bool _rtvAllowed = false;
    private DateTime _mapStartTime = DateTime.MinValue;
    private readonly Random _rng = new();

    private string? _pendingMap = null;
    private bool _changeScheduled = false;
    private bool _voteIsAuto = false;
    private Timer? _scoreboardTimer = null;
    private readonly HashSet<int> _chatModeSlots = new();

    // Bug conocido de CS2 (no específico de nuestros plugins, sin fix publicado — ver
    // github.com/kus/cs2-modded-server/issues/290): a veces, tras un cambio de mapa, un jugador
    // no puede unirse a ningún equipo aunque estén libres ("equipo lleno" con 1 solo jugador).
    // Lo detectamos por patrón: varios intentos de "jointeam" seguidos sin que el equipo
    // realmente cambie, y recargamos el mapa actual solos en vez de esperar a que alguien avise.
    private readonly Dictionary<int, List<DateTime>> _joinTeamAttempts = new();
    private bool _teamsStuckHealTriggered = false;

    // Reset por inactividad: desde cuándo el servidor está sin jugadores humanos en este mapa.
    // null = hay alguien (o aún no se ha comprobado). Se mide por reloj y no por número de
    // comprobaciones para que no dependa del intervalo del timer.
    private DateTime? _emptySince = null;
    private const float IdleCheckIntervalSeconds = 30f;

    // Mapas ya mostrados como opción de voto recientemente, por categoría — para no repetir
    // los mismos mapas votación tras votación. Se vacía sola cuando el pool restante de esa
    // categoría ya no llega para rellenar los huecos (en vez de quedarse sin mapas que ofrecer).
    private readonly HashSet<string> _recentMinigame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recentSurf = new(StringComparer.OrdinalIgnoreCase);

    // Los mapas que vienen de la sincronización de la colección de Workshop están indexados
    // por su ID numérico (no por el nombre del mapa) — el nombre real solo está en
    // MapInfo.Display, así que la categoría hay que mirarla ahí, no en la clave.
    private static bool IsSurfMap(MapInfo info) =>
        info.Display.StartsWith("surf_", StringComparison.OrdinalIgnoreCase);

    // Por la misma razón, comparar la clave contra Server.MapName solo funciona para las
    // entradas estáticas de rtv_maps.json — para las de Workshop hay que mirar el Display.
    private bool IsCurrentMap(string mapKey) =>
        mapKey.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase) ||
        (_mapService.Maps.TryGetValue(mapKey, out var info) &&
         info.Display.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase));

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

        PreloadSqliteNative();
        string dbPath = Path.Combine(ModuleDirectory, "..", "..", "data", "SimpleRTV", "prefs.db");
        _db = new PlayerPrefsDb(dbPath, Logger);
        _workshop = new WorkshopService(Logger);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);
        RegisterListener<Listeners.OnTick>(_wasdMenu.OnTick);
        RegisterEventHandler<EventPlayerActivate>(OnPlayerActivate);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeamChanged);
        AddCommandListener("say", OnPlayerSay);
        AddCommandListener("say_team", OnPlayerSay);
        AddCommandListener("jointeam", OnJoinTeamAttempt);

        if (hotReload)
        {
            _mapService.SetBlacklist(Config.BlacklistedWorkshopIds, Config.BlacklistedMapNames);
            _mapService.Load(GetMapsFilePath());
            _rtvAllowed = true;
            foreach (var p in GetValidPlayers())
                _wasdMenu.RegisterPlayer(p);
            StartIdleResetTimer();
        }
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);
        RemoveListener<Listeners.OnTick>(_wasdMenu.OnTick);
        DeregisterEventHandler<EventPlayerActivate>(OnPlayerActivate);
        DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        DeregisterEventHandler<EventPlayerTeam>(OnPlayerTeamChanged);
        RemoveCommandListener("say", OnPlayerSay, HookMode.Pre);
        RemoveCommandListener("say_team", OnPlayerSay, HookMode.Pre);
        RemoveCommandListener("jointeam", OnJoinTeamAttempt, HookMode.Pre);
        _wasdMenu.CloseAll();
    }

    // ── Auto-recuperación: jugador atascado sin poder unirse a ningún equipo ───

    private HookResult OnJoinTeamAttempt(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || _teamsStuckHealTriggered)
            return HookResult.Continue;

        var now = DateTime.Now;
        if (!_joinTeamAttempts.TryGetValue(player.Slot, out var attempts))
        {
            attempts = new List<DateTime>();
            _joinTeamAttempts[player.Slot] = attempts;
        }

        attempts.RemoveAll(t => (now - t).TotalSeconds > 20);
        attempts.Add(now);

        // 3 intentos de unirse a un equipo en 20s sin éxito (OnPlayerTeamChanged habría
        // limpiado la lista si alguno hubiera funcionado) — patrón claro del bug conocido.
        if (attempts.Count >= 3)
        {
            _teamsStuckHealTriggered = true;
            Logger.LogWarning(
                "[SimpleRTV] Auto-heal: {Player} no consigue unirse a ningún equipo tras {N} intentos en 20s. Recargando mapa actual ({Map}).",
                player.PlayerName, attempts.Count, Server.MapName);
            Server.PrintToChatAll($"{Prefix} {ChatColors.Red}Detectado un bug de equipos — recargando el mapa, un momento...");
            AddTimer(1.5f, () => _mapService.ChangeMap(Server.MapName), TimerFlags.STOP_ON_MAPCHANGE);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeamChanged(EventPlayerTeam @event, GameEventInfo info)
    {
        // Team > 1 = se unió de verdad a T o CT (0 = None, 1 = Spectator).
        if (@event.Team > 1)
        {
            var player = @event.Userid;
            if (player != null && player.IsValid)
                _joinTeamAttempts.Remove(player.Slot);
        }
        return HookResult.Continue;
    }

    // ── Listeners / Events ────────────────────────────────────────────────────

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
        _changeScheduled = false;
        _mapStartTime = DateTime.Now;
        _joinTeamAttempts.Clear();
        _teamsStuckHealTriggered = false;
        _emptySince = null;

        _mapService.SetBlacklist(Config.BlacklistedWorkshopIds, Config.BlacklistedMapNames);
        _mapService.Load(GetMapsFilePath());

        if (Config.RtvDelaySeconds > 0)
            AddTimer(Config.RtvDelaySeconds, () => _rtvAllowed = true, TimerFlags.STOP_ON_MAPCHANGE);
        else
            _rtvAllowed = true;

        // Delay reads to ensure the map has fully loaded
        AddTimer(3f, ScheduleTimeLimitTimers, TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(3f, FetchWorkshopMaps, TimerFlags.STOP_ON_MAPCHANGE);

        // Auto-recover from the vanilla CS2 fallback map (e.g. after a crash/restart with no
        // valid map configured) — fires immediately instead of waiting for an external cron
        // watchdog to poll and catch it.
        if (!string.IsNullOrWhiteSpace(Config.DefaultFallbackTriggerMap) &&
            !string.IsNullOrWhiteSpace(Config.DefaultFallbackMap) &&
            Server.MapName.Equals(Config.DefaultFallbackTriggerMap, StringComparison.OrdinalIgnoreCase))
        {
            AddTimer(3f, () =>
            {
                Logger.LogWarning("[SimpleRTV] Server booted on fallback map '{Trigger}', switching to '{Target}'",
                    Config.DefaultFallbackTriggerMap, Config.DefaultFallbackMap);
                _mapService.ChangeMap(Config.DefaultFallbackMap);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        StartIdleResetTimer();
    }

    // ── Reset por inactividad: servidor vacío → volver al mapa por defecto ─────

    private void StartIdleResetTimer()
    {
        if (Config.IdleResetMinutes <= 0 || string.IsNullOrWhiteSpace(Config.IdleResetMap)) return;
        AddTimer(IdleCheckIntervalSeconds, CheckIdleReset, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CheckIdleReset()
    {
        if (GetValidPlayers().Any())
        {
            _emptySince = null;
            return;
        }

        // Ya estamos en el mapa por defecto, o hay un cambio de mapa en curso: nada que hacer.
        if (_changeScheduled || Server.MapName.Equals(Config.IdleResetMap, StringComparison.OrdinalIgnoreCase))
        {
            _emptySince = null;
            return;
        }

        var now = DateTime.Now;
        _emptySince ??= now;
        if ((now - _emptySince.Value).TotalMinutes < Config.IdleResetMinutes) return;

        // Reinicia la cuenta pase lo que pase: si el mapa no se encuentra, se reintenta en
        // otros IdleResetMinutes en vez de cada 30s llenando el log.
        _emptySince = null;

        string? key = _mapService.ResolveKey(Config.IdleResetMap);
        if (key == null)
        {
            Logger.LogError("[SimpleRTV] Idle reset: map '{Map}' not found in the map list, skipping.", Config.IdleResetMap);
            return;
        }

        Logger.LogInformation("[SimpleRTV] Idle reset: no players for {Min} min on '{Current}', switching to '{Target}'.",
            Config.IdleResetMinutes, Server.MapName, Config.IdleResetMap);
        _mapService.ChangeMap(key);
    }

    private void ScheduleTimeLimitTimers()
    {
        var mpTimelimit = ConVar.Find("mp_timelimit");
        float timeLimitMinutes = mpTimelimit?.GetPrimitiveValue<float>() ?? 0f;
        Logger.LogInformation("[SimpleRTV] mp_timelimit: {Val}", timeLimitMinutes);

        if (timeLimitMinutes <= 0 || Config.TriggerSecondsBeforeEnd <= 0) return;

        float totalSeconds = timeLimitMinutes * 60f;
        // Start the auto-vote early enough that it always finishes before the forced change
        float triggerLead = Math.Max(Config.TriggerSecondsBeforeEnd, Config.VoteSeconds + 10);
        float autoVoteDelay = totalSeconds - triggerLead;

        if (autoVoteDelay > 0)
            AddTimer(autoVoteDelay, StartAutoVote, TimerFlags.STOP_ON_MAPCHANGE);

        AddTimer(totalSeconds, ForceMapChange, TimerFlags.STOP_ON_MAPCHANGE);

        Logger.LogInformation("[SimpleRTV] Timelimit: {Min}min. Auto-vote in {Delay}s, forced change in {Total}s.",
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
            _changeScheduled = true;
            PrintToAll("rtv.changing_now", _mapService.GetDisplayName(map));
            AddTimer(3f, () => _mapService.ChangeMap(map), TimerFlags.STOP_ON_MAPCHANGE);
        }
        return HookResult.Continue;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [ConsoleCommand("rtv", "Vote to change the map")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRtvCommand(CCSPlayerController? caller, CommandInfo _)
    {
        if (caller == null || !caller.IsValid) return;

        if (!_rtvAllowed || _changeScheduled)
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

    [ConsoleCommand("timeleft", "Show remaining time on the current map")]
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

    [ConsoleCommand("nominate", "Nominate a map for the next vote")]
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
            .Where(kv => !IsCurrentMap(kv.Key))
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

    [ConsoleCommand("votemode", "Toggle between WASD menu and chat voting")]
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

    [ConsoleCommand("css_frtv", "Force a map vote (root only)")]
    [RequiresPermissions("@css/root")]
    public void OnForceRtvCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (_mapVote.IsInProgress || _changeScheduled)
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

    [ConsoleCommand("nomlist", "Show current map nominations")]
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

    // ── Vote logic ────────────────────────────────────────────────────────────

    private void StartAutoVote()
    {
        if (_mapVote.IsInProgress || _pendingMap != null || _changeScheduled) return;
        PrintToAll("rtv.auto_vote_started");
        StartVote(auto: true);
    }

    private void StartVote(bool auto)
    {
        if (_mapVote.IsInProgress || _changeScheduled) return;
        _voteIsAuto = auto;

        if (!_mapService.HasMaps)
        {
            PrintToAll("rtv.no_maps");
            return;
        }

        // Nominated maps take priority within their own category; remaining slots per
        // category are filled randomly (see BuildCandidateList).
        var nominatedKeys = _nominate.GetNominatedMaps()
            .Where(k => !IsCurrentMap(k))
            .ToList();

        var candidates = BuildCandidateList(nominatedKeys);

        if (candidates.Count == 0)
        {
            PrintToAll("rtv.no_maps");
            return;
        }

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

    // Compone la votación con cuota fija: Config.MinigameSlotsInVote mapas normales +
    // Config.SurfSlotsInVote de surf. Dentro de cada categoría, las nominaciones tienen
    // prioridad y el resto se rellena al azar evitando repetir lo mostrado en votaciones
    // recientes — hasta que el pool restante de esa categoría no llegue para rellenar los
    // huecos, momento en el que se reinicia el historial de esa categoría en vez de
    // ofrecer menos mapas de los pedidos.
    private List<KeyValuePair<string, MapInfo>> BuildCandidateList(List<string> nominatedKeys)
    {
        var result = new List<KeyValuePair<string, MapInfo>>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void FillCategory(bool wantSurf, int slots)
        {
            if (slots <= 0) return;

            var nominatedForCategory = nominatedKeys
                .Where(k => !usedKeys.Contains(k) && _mapService.Maps.TryGetValue(k, out var info)
                            && IsSurfMap(info) == wantSurf)
                .Take(slots)
                .ToList();

            foreach (var key in nominatedForCategory)
            {
                result.Add(new(key, _mapService.Maps[key]));
                usedKeys.Add(key);
            }

            int remaining = slots - nominatedForCategory.Count;
            if (remaining <= 0) return;

            var recent = wantSurf ? _recentSurf : _recentMinigame;

            var pool = _mapService.Maps
                .Where(kv => !IsCurrentMap(kv.Key)
                             && !usedKeys.Contains(kv.Key)
                             && IsSurfMap(kv.Value) == wantSurf)
                .ToList();

            var freshPool = pool.Where(kv => !recent.Contains(kv.Key)).ToList();

            // Si sin repetir no llega para rellenar los huecos, se reinicia el historial de
            // esta categoría (vuelve a estar disponible el pool entero) en vez de ofrecer
            // menos mapas de los pedidos.
            var sourcePool = freshPool.Count >= remaining ? freshPool : pool;
            if (sourcePool == pool) recent.Clear();

            var picked = sourcePool.OrderBy(_ => _rng.Next()).Take(remaining).ToList();
            foreach (var kv in picked)
            {
                result.Add(kv);
                usedKeys.Add(kv.Key);
            }
        }

        FillCategory(wantSurf: false, Config.MinigameSlotsInVote);
        FillCategory(wantSurf: true, Config.SurfSlotsInVote);

        foreach (var kv in result)
            (IsSurfMap(kv.Value) ? _recentSurf : _recentMinigame).Add(kv.Key);

        return result;
    }

    private void OnVoteEnd(string? winnerKey)
    {
        _scoreboardTimer?.Kill();
        _scoreboardTimer = null;

        if (winnerKey == null)
        {
            ClearScoreboardForAll();
            PrintToAll("rtv.nobody_voted");
            return;
        }

        string display = _mapService.GetDisplayName(winnerKey);
        ShowResultForAll(display);

        if (_voteIsAuto)
        {
            // Auto-vote: store winner and apply at round end
            _pendingMap = winnerKey;
            PrintToAll("rtv.vote_ended", display);
        }
        else
        {
            // Manual RTV: change immediately after a short delay
            _changeScheduled = true;
            PrintToAll("rtv.changing_now", display);
            AddTimer(5f, () => _mapService.ChangeMap(winnerKey), TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    // Called when mp_timelimit reaches 0. Forces the map change immediately instead
    // of waiting for the round to end naturally.
    private void ForceMapChange()
    {
        // A change is already on its way (voted winner or round-end apply) — don't compete with it
        if (_changeScheduled) return;

        string? targetMap = _pendingMap;
        _pendingMap = null;

        if (_mapVote.IsInProgress)
        {
            // Vote is still running past the deadline: take the current leader and cut it short.
            targetMap = _mapVote.CurrentLeader() ?? targetMap;
            _mapVote.Reset();
            _scoreboardTimer?.Kill();
            _scoreboardTimer = null;
            _wasdMenu.CloseAll();
            ClearScoreboardForAll();
        }

        if (targetMap == null)
        {
            // Servidor vacío: no rotar a un mapa aleatorio que nadie ha pedido — el reset
            // por inactividad ya lo devuelve al mapa por defecto.
            if (!GetValidPlayers().Any())
            {
                Logger.LogInformation("[SimpleRTV] Timelimit reached with no players, skipping random map change.");
                return;
            }

            var random = _mapService.Maps
                .Where(kv => !IsCurrentMap(kv.Key))
                .OrderBy(_ => _rng.Next())
                .FirstOrDefault();

            if (random.Key == null) return;

            targetMap = random.Key;
            PrintToAll("rtv.timelimit_no_vote");
        }
        else
        {
            PrintToAll("rtv.changing_now", _mapService.GetDisplayName(targetMap));
        }

        _changeScheduled = true;
        string map = targetMap;
        AddTimer(3f, () => _mapService.ChangeMap(map), TimerFlags.STOP_ON_MAPCHANGE);
    }

    // ── Workshop auto-population ──────────────────────────────────────────────

    private void FetchWorkshopMaps()
    {
        string collectionId = GetCollectionId();
        if (string.IsNullOrEmpty(collectionId)) return;

        string cachePath = Path.GetFullPath(
            Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName, "workshop_cache.json"));

        _workshop.FetchAndCacheAsync(collectionId, cachePath, Config.WorkshopCacheHours)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || t.Result.Count == 0) return;
                Server.NextFrame(() => _mapService.MergeWorkshopMaps(t.Result));
            });
    }

    private string GetCollectionId()
    {
        if (!string.IsNullOrEmpty(Config.WorkshopCollectionId))
            return Config.WorkshopCollectionId;

        // Auto-detect from server launch argument
        var cvar = ConVar.Find("host_workshop_collection");
        return cvar?.StringValue ?? "";
    }

    // ── Chat voting ───────────────────────────────────────────────────────────

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
            return HookResult.Handled; // suppress the number from public chat
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

    // ── Live scoreboard ───────────────────────────────────────────────────────

    private void UpdateVoteScoreboard()
    {
        if (!_mapVote.IsInProgress) return;
        _wasdMenu.RefreshAll(); // update vote counts inside open WASD menus
        foreach (var player in GetValidPlayers())
            if (!_wasdMenu.HasActiveMenu(player.Slot))
                player.PrintToCenterHtml(BuildScoreboardHtml(player.Slot));
    }

    private string BuildScoreboardHtml(int playerSlot)
    {
        var votes = _mapVote.Votes;
        int maxVotes = votes.Values.DefaultIfEmpty(0).Max();
        int totalVotes = votes.Values.Sum();
        bool hasVoted = _mapVote.HasVoted(playerSlot);

        var sb = new StringBuilder();
        sb.Append("<div>");
        sb.Append("<b><font color='#ff4444' class='fontSize-m'>Map Vote</font></b><br>");

        foreach (var kv in votes.OrderByDescending(v => v.Value))
        {
            string display = _mapService.GetDisplayName(kv.Key);
            int count = kv.Value;
            string bar = count > 0 ? new string('█', Math.Min(count * 3, 12)) : "░░░░░░";
            string nameColor = count == maxVotes && count > 0 ? "#ffcc00" : "white";
            sb.Append($"<font color='{nameColor}' class='fontSize-m'>{display}</font>  <font color='#88ff88'>{bar}</font>  <font color='white'>{count}</font><br>");
        }

        string footer = hasVoted
            ? $"<font color='#88ff88'>You voted ✓</font>  <font color='gray'>|  Votes: {totalVotes}</font>"
            : $"<font color='gray'>Type a number to vote  |  Votes: {totalVotes}</font>";
        sb.Append($"<br><font class='fontSize-sm'>{footer}</font>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private void ShowResultForAll(string mapDisplay)
    {
        string html = $"<div><b><font color='#ff4444' class='fontSize-l'>Vote ended!</font></b><br>" +
                      $"<font color='#ffcc00' class='fontSize-l'>{mapDisplay}</font><br>" +
                      $"<font color='gray' class='fontSize-sm'>wins the vote</font></div>";

        foreach (var player in GetValidPlayers())
            player.PrintToCenterHtml(html);

        AddTimer(6f, () =>
        {
            foreach (var player in GetValidPlayers())
                player.PrintToCenterHtml(" ");
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ClearScoreboardForAll()
    {
        foreach (var player in GetValidPlayers())
            player.PrintToCenterHtml(" ");
    }

    // ── Localization helpers ──────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PreloadSqliteNative()
    {
        string libName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "e_sqlite3.dll" : "libe_sqlite3.so";
        string libPath = Path.Combine(ModuleDirectory, libName);
        if (!File.Exists(libPath))
        {
            Logger.LogWarning("[SimpleRTV] Native SQLite library not found at {Path} — SQLite features may fail.", libPath);
            return;
        }
        try
        {
            NativeLibrary.Load(libPath);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("[SimpleRTV] Could not preload {Lib}: {Err}", libName, ex.Message);
        }
    }

    private static void FireAndForget(Task _) { }

    private string GetMapsFilePath()
    {
        if (Path.IsPathRooted(Config.MapsFile))
            return Config.MapsFile;

        string configDir = Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName));
        return Path.Combine(configDir, Config.MapsFile);
    }

    private IEnumerable<CCSPlayerController> GetValidPlayers() =>
        Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV);
}
