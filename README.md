# BacklogImprovements

A BepInEx QoL mod for Mycopunk that combines backlog (directive) improvements into one package:

- **Preselect path** — plan a directive sequence and auto-progress through it
- **Free next page** — no resource cost to generate the next backlog page
- **Reroll** — reroll one directive or the whole page for gats

This mod **replaces** the separate packages `PreselectBacklog`, `FreeBacklogPages`, and `RerollBacklog`. Uninstall those three if you still have them installed to avoid double-patching.

## Features

### Preselect path
* **Edit Path** mode — click directives to build a planned sequence (one per tier)
* **UI path lines** — stable UI-space connectors with order badges
* **Auto-claim** — completed path nodes claim rewards automatically
* **Auto-activate next** — after a path node is claimed, the next path node activates
* **Clear path** — remove the planned path for the current page
* **Persistence** — paths are saved per backlog page across sessions
* **Debug force-complete** — configurable key (default F2) to complete the active directive for testing

### Free next page
* Completing a backlog page no longer requires paying a percentage of held resources
* Next-page hold prompt no longer shows a resource cost

### Reroll
* **Reroll Page** — regenerate every not-started directive on the current page
* **Reroll One** — enter selection mode, click a not-started directive, confirm to reroll it
* Active and completed directives are never changed
* Configurable cost (default **50 gats** per directive)

### Unified toolbar
All controls live on a single bottom toolbar in the directive window. Path-edit and reroll-select modes are mutually exclusive.

## Dependencies

* Mycopunk
* [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.2403+
* [SparrohUILib](https://thunderstore.io) (`Sparroh-SparrohUILib`)

## Installation

**Thunderstore / r2modman (recommended):** install `Sparroh-BacklogImprovements` (pulls SparrohUILib). Remove the old standalone mods if present.

**Manual:** place `BacklogImprovements.dll` in `BepInEx/plugins/` and ensure SparrohUILib is installed.

## Usage

1. Open the backlog / directive window.
2. **Path:** click **Edit Path**, select directives (same tier replaces; re-click removes), then **Done**.
3. When a path directive completes, rewards are claimed and the next path node activates.
4. **Reroll:** use **Reroll Page (cost)** or **Reroll One (cost)**; confirm the gats spend.
5. **Clear** removes the path for the current page.
6. Press **F2** (configurable) to force-complete the active directive while testing.

## Configuration

`BepInEx/config/sparroh.backlogimprovements.cfg`

| Setting | Default | Description |
|---|---|---|
| `Features.EnablePreselect` | `true` | Path preselect, auto-claim, auto-activate |
| `Features.EnableReroll` | `true` | Reroll page / reroll one |
| `Features.EnableFreePages` | `true` | Free next backlog page |
| `Reroll.CostPerDirective` | `50` | Gats charged per rerolled directive |
| `Debug.ForceCompleteKey` | `F2` | Force-complete active directive (`None` to disable) |

Paths are stored in:

`BepInEx/config/sparroh.backlogimprovements.txt`

Format (legacy-compatible): `page,index` per line.

On first load, paths are migrated automatically from `sparroh.preselectbacklog.txt` if present.

## Building

```bash
dotnet build --configuration Release
```

## Authors

- Sparroh
- funlennysub (BepInEx template)

## License

MIT — see LICENSE
