# PreselectBacklog

A BepInEx mod for MycoPunk that adds automation to the game's directive/backlog system, allowing players to preselect a sequence of directives and automatically progress through them.

## Features

* Preselect mode for selecting directive sequences
* Visual indicators for preselected directives (yellow outlines and connecting lines)
* Automatic progression through selected directive sequences
* Automatic reward claiming for completed preselected directives
* F2 key binding for force-completing the current directive

## Installation

### Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible

### Setup

1. Install BepInEx for MycoPunk
2. Download the mod from Thunderstore or build from source
3. Place `PreselectBacklog.dll` in your `<MycoPunk Directory>/BepInEx/plugins/` folder
4. Launch the game - the mod will load automatically

## Usage

1. Open the directive/backlog window in-game
2. Click the "Enable Preselect" button at the bottom of the directive window to enter preselect mode
3. Click on directives to add them to your preselected sequence (they will be highlighted with yellow outlines)
4. Yellow lines will connect the selected directives in order
5. Click "Disable Preselect" to exit preselect mode and begin automatic progression
6. The mod will automatically activate the first directive in your sequence
7. Upon completion of a preselected directive, rewards will be claimed automatically and the next directive in the sequence will activate
8. Press F2 to force-complete the currently active directive (for testing purposes)

Preselected sequences are saved per directive page and persist between game sessions.

## Configuration

The mod saves preselected directive sequences to a configuration file located at:
`<MycoPunk Directory>/BepInEx/config/sparroh.preselectbacklog.txt`

The file format is comma-separated values: `page,index` where page is the directive page number and index is the directive position on that page.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history and changes.

## Authors

- Sparroh
- ToeKneeRED (original MycoModList)
- funlennysub (BepInEx template)
- [@DomPizzie](https://twitter.com/dompizzie) (README template)

## License

This project is licensed under the MIT License - see the LICENSE file for details
