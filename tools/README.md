# TarkovTracker maintenance scripts

Scripts in this folder refresh map data from tarkov.dev. They are **not** used at runtime.

## Paths

- **Config output:** `../Config/` (JSON files copied into the app build)
- **Dev data:** `./data/` (large source files not shipped with the app)

## Common tasks

| Task | Script |
|------|--------|
| Refresh quest markers (recommended) | `build_quest_markers_from_api.ps1` |
| Refresh boss spawn markers | `build_boss_spawn_markers.ps1` |
| Refresh map level toggles | `build_map_levels.ps1` |
| Compare quest markers vs API | `compare_quest_markers.ps1` |
| Update Terminal map assets | `update_terminal_map.ps1` |

## Legacy

- `regenerate_quest_markers.ps1` — uses `data/tarkov_tasks_raw.json`; prefer `build_quest_markers_from_api.ps1`
- `terminal_*.json` in `data/` — intermediate outputs from Terminal setup scripts
