# SayserTarkovTracker

A Windows desktop map companion for **Escape from Tarkov**. It displays interactive tactical maps with live player tracking from in-game screenshots, plus extracts, quests, spawns, bosses, hazards, and more — styled with a tactical HUD interface.

Built with **WPF** (.NET 10) and **WebView2**.

---

## Features

### Interactive maps
- SVG maps for all supported locations (Customs, Factory, Interchange, Labs, Lighthouse, Reserve, Shoreline, Streets of Tarkov, Woods, Ground Zero, Terminal, Labyrinth, and others)
- Pan and zoom the map with mouse drag and scroll wheel
- Click any marker to view details (name, type, conditions, coordinates)

### Live player tracking (screenshots)
- Monitors your **Escape from Tarkov Screenshots** folder (or a custom folder you choose)
- Parses coordinates and facing direction from screenshot filenames automatically
- Plots your position and bearing on the map in real time
- LCD-style readout for X, Y, Z, and bearing in the sidebar

### Map markers
Toggle marker layers individually or with **Show All**:

| Category | Examples |
|----------|----------|
| Location names | Map labels (e.g. building names) |
| Extracts | PMC, Scav, Shared |
| Transits | Map-to-map transit points |
| Quests | Quest items and objectives |
| Spawns | PMC, Scav, Boss spawn zones |
| Hazards | Mortars, danger zones |
| Switches | Power switches and similar |

Markers use the same icon set as [tarkov.dev](https://tarkov.dev). Quest item markers can show the actual item icon when available.

### Map levels (floors)
- Per-map floor/layer toggles (e.g. Factory basement, Ground Floor, 2nd Floor)
- Matches tarkov.dev behavior: optional levels overlay the base map; the base layer dims when extra floors are active
- **Base** layer toggle plus **All** / **None** for optional levels

### Overlay window
- Separate always-on-top **Glass HUD** overlay
- Semi-transparent, resizable (drag edges/corners)
- Adjustable opacity slider
- Syncs map, markers, levels, filters, and player position with the main window

### UI
- Dark tactical HUD theme (amber accents, monospace coordinates, terminal-style status bar)

---

## Requirements

- **Windows 10/11**
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (target: `net10.0-windows`)
- **[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)** (usually already installed on Windows 11)

---

## Build and run

```powershell
cd TarkovTracker
dotnet build
dotnet run
```

Or open `TarkovTracker.sln` in Visual Studio and run (F5).

---

## Usage

1. **Select a map** from the dropdown in the top bar.
2. **Set your screenshot folder** via **Screenshot Folder** (default: `Documents\Escape from Tarkov\Screenshots`).
3. Take an in-game screenshot — the app watches the folder and updates your position automatically.
4. Use **Read Latest** to manually parse the newest screenshot.
5. Toggle **Markers** and **Levels** in the right sidebar.
6. Open **Overlay** for a floating map on top of the game.

---

## Project structure

```
TarkovTracker/
├── Assets/           # Map viewer CSS/JS (WebView2)
├── Config/           # Runtime JSON (maps, markers, levels, extracts, etc.)
├── Maps/             # SVG map files and interactive marker icons
├── Models/           # Data transfer objects
├── Services/         # Map data loading and HTML builder
├── Themes/           # WPF tactical UI styles
├── tools/            # Dev scripts to refresh data from tarkov.dev (not used at runtime)
├── MainWindow.xaml   # Main application window
└── OverlayWindow.xaml
```

Map configuration and marker data live in `Config/`. Maintenance scripts in `tools/` pull fresh data from the tarkov.dev API — see [`tools/README.md`](tools/README.md).

---

## Data sources and credits

This project is a **fan-made companion tool**. It is **not** affiliated with Battlestate Games.

### [tarkov.dev](https://tarkov.dev)

We rely heavily on the tarkov.dev project and community data:

| Used from tarkov.dev | Purpose |
|----------------------|---------|
| **SVG map graphics** | Interactive map backgrounds ([tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)) |
| **Map metadata & coordinates** | Bounds, transforms, floor/layer definitions |
| **Game data API** | Extracts, spawns, transits, hazards, switches, labels, quests, boss spawns |
| **Interactive marker icons** | Extract, spawn, quest, hazard, switch, transit, and boss icons (`Maps/interactive/`, from [tarkov-dev](https://github.com/the-hideout/tarkov-dev)) |
| **Item icons** | Quest item marker images via `assets.tarkov.dev` |

Thank you to the [tarkov.dev](https://tarkov.dev) team and contributors for maintaining maps and open game data.

### Escape from Tarkov

*Escape from Tarkov* is a trademark of Battlestate Games Limited. This project is unofficial and for personal/educational use.

---

## Updating map data

To refresh quest markers, boss spawns, or map levels from the latest tarkov.dev API:

```powershell
cd tools
.\build_quest_markers_from_api.ps1
.\build_boss_spawn_markers.ps1
.\build_map_levels.ps1
```

See [`tools/README.md`](tools/README.md) for the full list of scripts.

---

## License

Personal use only.

---

## Disclaimer

Use at your own risk. Map data may lag behind game patches. Screenshot parsing depends on Escape from Tarkov's screenshot filename format; game updates could break it.
