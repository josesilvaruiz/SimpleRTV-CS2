# SimpleRTV for CS2 — Surf fork

Fork of [SimpleRTV-CS2](https://github.com/josesilvaruiz/SimpleRTV-CS2) for a surf-only server fed by a single Workshop collection. Differences: no default map and no idle reset; one flat map pool (`MapsInVote` slots, no minigame/surf categories); the server boots on any stock map and the first player to join triggers an immediate map vote (random pool map if nobody votes).

A Rock The Vote plugin for Counter-Strike 2 built with [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

## Features

- **RTV voting** — players vote with `!rtv`; once the threshold is reached, a map vote starts automatically
- **Automatic map vote** — triggers a few minutes before the timelimit expires (configurable)
- **WASD menu** — interactive in-game menu with live vote counts next to each map option, navigated with W/S/E/R keys
- **Chat vote mode** — players can switch to typing a number in chat with `!votemode`; preference is saved per-player in SQLite
- **Map nominations** — players nominate maps with `!nominate`; nominated maps get priority in the vote
- **Live vote scoreboard** — real-time vote counts visible during the vote (in the WASD menu and as a center-screen overlay for chat-mode players)
- **Vote result screen** — winning map displayed on screen for 6 seconds when the vote ends
- **Workshop map support** — handles `changelevel`, `host_workshop_map`, and `ds_workshop_changelevel`
- **Workshop Collection auto-populate** — reads your server's active Workshop Collection from Steam API and builds the map list automatically; results are cached locally to avoid hitting the API on every restart
- **Multi-language** — English, Spanish, French, German, Russian, Portuguese, Polish, Italian, Dutch, Turkish

## Installation

1. Download the latest release and extract it into your **server root** — files land in the correct folders automatically:
   - Plugin DLL + SQLite libs → `addons/counterstrikesharp/plugins/SimpleRTV/`
   - Map list template → `addons/counterstrikesharp/configs/plugins/SimpleRTV/rtv_maps.json`

2. Edit `addons/counterstrikesharp/configs/plugins/SimpleRTV/rtv_maps.json` with your map pool (see format below), or leave it empty and use Workshop Collection auto-populate.

3. Restart the server. The config file `SimpleRTV.json` will be generated automatically in `addons/counterstrikesharp/configs/plugins/SimpleRTV/`.

> **Linux servers:** The native SQLite library (`libe_sqlite3.so`) is bundled in the zip and placed in the plugin folder. The plugin loads it automatically on startup. No manual copy is needed.

## Workshop Collection auto-populate

If your server runs a Workshop Collection, SimpleRTV can build the map list automatically without manual `rtv_maps.json` maintenance.

**Option A — auto-detect (recommended):** launch your server with `+host_workshop_collection <id>`. SimpleRTV will read that convar automatically.

**Option B — set it in the config:**
```json
"WorkshopCollectionId": "3736332535"
```

On each map start, the plugin checks a local cache (`workshop_cache.json` in the configs folder). If the cache is older than `WorkshopCacheHours` (default 24h), it re-fetches from Steam API. No API key required.

> Maps defined in `rtv_maps.json` take precedence over workshop-fetched maps (useful for adding official maps like `de_dust2` alongside workshop content).

## Map list format (`rtv_maps.json`)

File location: `addons/counterstrikesharp/configs/plugins/SimpleRTV/rtv_maps.json`

```json
{
  "de_dust2":           { "ws": false, "display": "Dust 2",            "mapid": "" },
  "de_mirage":          { "ws": false, "display": "Mirage",            "mapid": "" },
  "mg_simpsons_course": { "ws": true,  "display": "Simpsons Course",   "mapid": "3070447697" },
  "mg_lego_islands":    { "ws": true,  "display": "Lego Islands",      "mapid": "3558345146" }
}
```

| Field     | Type    | Description                                                    |
|-----------|---------|----------------------------------------------------------------|
| `ws`      | boolean | `true` for Workshop maps, `false` for official/built-in maps  |
| `display` | string  | Name shown in vote menus and chat                              |
| `mapid`   | string  | Workshop item ID — required when `ws` is `true`               |

The map **key** (e.g. `mg_simpsons_course`) must match the exact map name the server uses (`Server.MapName`). For Workshop maps this is the filename without extension, as reported by the game after the map loads.

## Configuration

Located at `addons/counterstrikesharp/configs/plugins/SimpleRTV/SimpleRTV.json`:

```json
{
  "RtvThreshold": 0.6,
  "VoteSeconds": 30,
  "RtvDelaySeconds": 90,
  "MapsInVote": 6,
  "MapsFile": "rtv_maps.json",
  "TriggerSecondsBeforeEnd": 120,
  "WorkshopCollectionId": "",
  "WorkshopCacheHours": 24
}
```

| Key                       | Default          | Description                                                                                      |
|---------------------------|------------------|--------------------------------------------------------------------------------------------------|
| `RtvThreshold`            | `0.6`            | Fraction of players needed to trigger a vote (60%)                                               |
| `VoteSeconds`             | `30`             | Duration of the map vote in seconds                                                              |
| `RtvDelaySeconds`         | `90`             | Seconds after map start before RTV is allowed                                                    |
| `MapsInVote`              | `6`              | Number of map options shown in the vote                                                          |
| `MapsFile`                | `rtv_maps.json`  | Map list filename — resolved relative to `configs/plugins/SimpleRTV/` (this plugin's config folder) |
| `TriggerSecondsBeforeEnd` | `120`            | Seconds before timelimit to start the automatic vote                                             |
| `WorkshopCollectionId`    | `""`             | Workshop Collection ID to auto-populate maps (empty = auto-detect from `host_workshop_collection`) |
| `WorkshopCacheHours`      | `24`             | Hours before the workshop map cache is refreshed from Steam API                                  |

The timelimit does not start counting on an empty map — it only starts when the first player joins, so it never votes or changes the map while the server has 0 human players.

The plugin never turns `sv_hibernate_when_empty` on by itself (it only ever forces it back to 0): waking from hibernation made CS2 issue a changelevel to an empty map name, leaving nobody able to join a team.
