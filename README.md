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
- **Multi-language** — English, Spanish, French, German, Russian, Portuguese, Polish, Italian, Dutch, Turkish

## Installation

1. Download the latest release and extract it into your server's `addons/counterstrikesharp/plugins/` folder.
2. Copy `rtv_maps.json` to `csgo/cfg/rtv_maps.json` on your server and edit it with your map list.
3. Restart the server. A default config file will be generated at `addons/counterstrikesharp/configs/plugins/SimpleRTV/SimpleRTV.json`.

## Map list format (`rtv_maps.json`)

```json
{
  "de_dust2":    { "ws": false, "display": "Dust 2",      "mapid": "" },
  "de_mirage":   { "ws": false, "display": "Mirage",      "mapid": "" },
  "workshop_map":{ "ws": true,  "display": "My WS Map",   "mapid": "1234567890" }
}
```

| Field     | Type    | Description                                      |
|-----------|---------|--------------------------------------------------|
| `ws`      | boolean | `true` for workshop maps, `false` for official   |
| `display` | string  | Name shown in vote menus and chat                |
| `mapid`   | string  | Workshop map ID (only used when `ws` is `true`)  |

## Configuration

Located at `configs/plugins/SimpleRTV/SimpleRTV.json`:

```json
{
  "RtvThreshold": 0.6,
  "VoteSeconds": 30,
  "RtvDelaySeconds": 90,
  "MapsInVote": 5,
  "MapsFile": "cfg/rtv_maps.json",
  "TriggerSecondsBeforeEnd": 120
}
```

| Key                      | Default             | Description                                               |
|--------------------------|---------------------|-----------------------------------------------------------|
| `RtvThreshold`           | `0.6`               | Fraction of players needed to trigger a vote (60%)        |
| `VoteSeconds`            | `30`                | Duration of the map vote in seconds                       |
| `RtvDelaySeconds`        | `90`                | Seconds after map start before RTV is allowed             |
| `MapsInVote`             | `5`                 | Number of map options shown in the vote                   |
| `MapsFile`               | `cfg/rtv_maps.json` | Path to map list file relative to the `csgo/` folder      |
| `TriggerSecondsBeforeEnd`| `120`               | Seconds before timelimit to start the automatic vote      |

## Commands

| Command       | Access  | Description                              |
|---------------|---------|------------------------------------------|
| `!rtv`        | Players | Vote to change the map                   |
| `!votemode`   | Players | Toggle between WASD menu and chat voting |
| `!nominate`   | Players | Open nomination menu                     |
| `!nomlist`    | Players | Show current nominations                 |
| `!timeleft`   | Players | Show remaining time on the current map   |
| `!css_frtv`   | Root    | Force a map vote immediately             |

## Map change timing

- **Manual RTV** — map changes immediately after the vote (5-second delay)
- **Automatic vote** — map changes at the end of the current round

## Requirements

- CounterStrikeSharp `>= 1.0.367`
- .NET 8

## License

MIT
