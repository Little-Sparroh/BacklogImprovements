# PreselectBacklog

A BepInEx mod for MycoPunk that adds automation to the game's directive/backlog system, allowing players to preselect a sequence of directives and automatically progress through them.

## Description

This mod enhances the MycoPunk directive system (commonly called "backlog") by enabling players to predefine a path through the directive tree on each page. Once a path is selected, the mod will automatically claim rewards from completed directives and activate the next directive in the sequence, streamlining progression and reducing manual interactions.

## Features

- **Preselect Path Mode**: Toggle preselection mode with F1 key to select a sequence of directives
- **Visual Feedback**: Preselected directives are highlighted with yellow outlines when in preselect mode
- **Automatic Progression**: After claiming rewards, the next directive in the path activates automatically
- **Per-Page Storage**: Separate paths can be defined for each directive page
- **Client-Side Operation**: Mod operates entirely on the client, preserving game integrity

## Getting Started

### Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
* .NET Framework 4.8
* [HarmonyLib](https://github.com/pardeike/Harmony) (included via NuGet)

### Building/Compiling

1. Clone this repository and customize the following:
   - Rename namespace and class names appropriately
   - Modify PluginGUID to be unique (format: "author.modname")
   - Update PluginName and PluginVersion
   - Add your specific Harmony patches and functionality

2. Add any additional NuGet packages or references needed for your mod

3. Open the solution file in Visual Studio, Rider, or your preferred C# IDE

4. Build the project in Release mode to generate the .dll file

Alternatively, use dotnet CLI:
```bash
dotnet build --configuration Release
```

### Installing

**For distribution as a completed mod:**

**Option 1: Via Thunderstore (Recommended)**
1. Update `thunderstore.toml` with your mod's specific information
2. Publish using Thunderstore CLI or mod manager
3. Users download and install via Thunderstore Mod Manager

**Option 2: Manual Distribution**
1. Package the built .dll, any config files, and README
2. Users place the .dll in their `<MycoPunk Directory>/BepInEx/plugins/` folder

**Note:** This template is not meant to be installed directly - customize it first for your specific mod functionality.

### Executing program

Once customized and built, the mod will automatically load through BepInEx when the game starts. Check the BepInEx console for loading confirmation messages.

### Mod Development Structure

- **Plugin.cs:** Main plugin class with Awake method and Harmony initialization
- **thunderstore.toml:** Publishing configuration for Thunderstore
- **CSPROJECT.csproj:** Build configuration with proper references
- **Resources:** Icon and documentation placeholders

## Help

* **First time modding?** Check BepInEx documentation and MycoPunk modding resources
* **Harmony patches failing?** Ensure method signatures match the game's IL
* **Dependency issues?** Update NuGet packages and verify .NET runtime version
* **Thunderstore publishing?** Update all metadata in thunderstore.toml before publishing
* **Plugin not loading?** Check BepInEx logs for errors and verify GUID uniqueness

## Authors

* Sparroh (MycoPunk mod collection maintainer)
* funlennysub (original BepInEx template)
* [@DomPizzie](https://twitter.com/dompizzie) (README template)

## License

* This project is licensed under the MIT License - see the LICENSE.md file for details
