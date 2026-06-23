# SimpleRTV for CS2

A Rock The Vote plugin for Counter-Strike 2 built with [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

## Features

- **RTV voting** — players vote with `!rtv`; once the threshold is reached, a map vote starts automatically
- **Automatic map vote** — triggers a few minutes before the timelimit expires (configurable)
- **WASD menu** — interactive in-game menu navigated with W/S/E/R keys
- **Chat vote mode** — players can switch to typing a number in chat with `!votemode`; preference is saved per-player in SQLite
- **Map nominations** — players nominate maps with `!nominate`; nominated maps get priority in the vote
- **Live vote scoreboard** — players who have already voted see real-time vote counts on screen
- **Workshop map support** — handles `changelevel`, `host_workshop_map`, and `ds_workshop_changelevel`
- **Workshop Collection auto-populate** — reads your server's active Workshop Collection from Steam API and builds the map list automatically; results are cached locally to avoid hitting the API on every restart
- **Multi-language** — English, Spanish, French, German, Russian, Portuguese, Polish, Italian, Dutch, Turkish

## Installation

1. Download the latest release and extract it into your server root — files land in the correct folders automatically.
2. Copy `rtv_maps.json` to `addons/counterstrikesharp/configs/plugins/SimpleRTV/rtv_maps.json` and edit it with your maps (or leave it empty and let the Workshop auto-populate do the work).
3. Restart the server. The config file `SimpleRTV.json` will be generated in the same folder.

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

```json
{
  "de_dust2":     { "ws": false, "display": "Dust 2",    "mapid": "" },
  "mg_mymap":     { "ws": true,  "display": "My WS Map", "mapid": "1234567890" }
}
```

| Field     | Type    | Description                                      |
|-----------|---------|--------------------------------------------------|
| `ws`      | boolean | `true` for workshop maps, `false` for official   |
| `display` | string  | Name shown in vote menus and chat                |
| `mapid`   | string  | Workshop item ID (only used when `ws` is `true`) |

## Configuration

Located at `configs/plugins/SimpleRTV/SimpleRTV.json`:

```json
{
  "RtvThreshold": 0.6,
  "VoteSeconds": 30,
  "RtvDelaySeconds": 90,
  "MapsInVote": 5,
  "MapsFile": "rtv_maps.json",
  "TriggerSecondsBeforeEnd": 120,
  "WorkshopCollectionId": "",
  "WorkshopCacheHours": 24
}
```

| Key                       | Default          | Description                                                        |
|---------------------------|------------------|--------------------------------------------------------------------|
| `RtvThreshold`            | `0.6`            | Fraction of players needed to trigger a vote (60%)                 |
| `VoteSeconds`             | `30`             | Duration of the map vote in seconds                                |
| `RtvDelaySeconds`         | `90`             | Seconds after map start before RTV is allowed                      |
| `MapsInVote`              | `5`              | Number of map options shown in the vote                            |
| `MapsFile`                | `rtv_maps.json`  | Map list filename (relative to this plugin's configs folder)       |
| `TriggerSecondsBeforeEnd` | `120`            | Seconds before timelimit to start the automatic vote               |
| `WorkshopCollectionId`    | `""`             | Workshop Collection ID to auto-populate maps (empty = auto-detect) |
| `WorkshopCacheHours`      | `24`             | Hours before the workshop map cache is refreshed from Steam API    |

## Commands

| Command     | Access  | Description                              |
|-------------|---------|------------------------------------------|
| `!rtv`      | Players | Vote to change the map                   |
| `!votemode` | Players | Toggle between WASD menu and chat voting |
| `!nominate` | Players | Open nomination menu                     |
| `!nomlist`  | Players | Show current nominations                 |
| `!timeleft` | Players | Show remaining time on the current map   |
| `!css_frtv` | Root    | Force a map vote immediately             |

## Map change timing

- **Manual RTV** — map changes immediately after the vote ends (5-second delay)
- **Automatic vote** — map changes at the end of the current round, never mid-round

## Requirements

- CounterStrikeSharp `>= 1.0.367`
- .NET 8
- `Microsoft.Data.Sqlite` — **bundled** in the release zip (includes native `e_sqlite3` for Windows x64 and Linux x64)

## License

MIT
