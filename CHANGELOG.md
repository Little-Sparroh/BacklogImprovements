# Changelog

## 2.1.2

### Fixes
* **EnableFreePages** hot-reloads immediately in both directions (on→off and off→on)
* Free pages no longer mutates the vanilla next-page cost array — Harmony intercepts unlock UI + charge using a tracked next-page button and the live config value
* Reloads config from disk when the `.cfg` changes (fallback if a settings UI only writes the file)
* Pair with **ModSettingsMenu 2.0.1+** so toggles update the live `ConfigEntry` and fire `SettingChanged`


## 2.1.0

### Merge
* Combined **PreselectBacklog**, **FreeBacklogPages**, and **RerollBacklog** into **BacklogImprovements**
* Single SparrohUILib toolbar for path + reroll controls
* Mutual exclusion between path-edit and reroll-select modes
* Feature toggles: `EnablePreselect`, `EnableReroll`, `EnableFreePages`
* Reroll UI/copy uses **gats** (in-game currency name)
* Migrates saved paths from legacy `sparroh.preselectbacklog.txt`
* New GUID `sparroh.backlogimprovements` — uninstall the three old mods

### From PreselectBacklog 2.0.0
* Path preselect, UI lines/badges, auto-claim, auto-activate, force-complete

### From FreeBacklogPages 1.0.0
* Free next backlog page (no resource cost)

### From RerollBacklog 1.0.0
* Reroll page / reroll one for configurable gats cost
