# DSP Tech Tree UX

A compact replacement for Dyson Sphere Program's Technology and Upgrades screens, focused on making research requirements, dependencies and current state easier to understand at a glance.

## Features

- Compact technology and upgrade nodes with clear research-state colouring.
- Searchable Technology and Upgrades pages.
- Explicit Matrix and Combat filters.
- White Matrix-only compact upgrade layout, with an optional Infinite-only filter.
- Direct `Q+` action on technology nodes to add research using DSP's normal research queue rules.
- Dependency highlighting that distinguishes the selected technology, its prerequisites/path, immediate downstream unlocks and unrelated nodes.
- Main research progression from Electromagnetism through Mission Completed is highlighted separately.
- Partial research progress and repeatable-upgrade level information shown directly on nodes.
- Collapsible details panel.
- Compact Search / Filters flyout beside the vanilla top tabs.
- Right-click on queued research continues to use the vanilla queue interaction.
- Mouse-friendly graph navigation:
  - drag empty space to pan without clearing the current selection;
  - click empty space to deselect;
  - right-click empty space to close the Technology screen.
- Independent Technology / Upgrades pan and zoom state.
- Per-save persistence for page, filters, zoom and pan through DSPModSave.
- Filtered layouts compact empty space while preserving the overall vanilla-derived tree structure.
- Additional inferred item/recipe unlock dependencies where DSP's visible prerequisite graph does not fully express a practical dependency.

## Configuration

The mod exposes appearance settings through the normal BepInEx configuration file:

- `LineThickness` — thickness of ordinary dependency lines. Default: `5.0`
- `MainLineThickness` — thickness of the main research progression line. Default: `9.0`

## Installation

### Mod manager

Install DSP Tech Tree UX through Thunderstore Mod Manager or r2modman.

### Manual

1. Install BepInEx for Dyson Sphere Program.
2. Install DSPModSave.
3. Copy `DSPTechTreeUX.dll` into:
   `Dyson Sphere Program/BepInEx/plugins/DSPTechTreeUX/`
4. Start the game.

## Compatibility

Built and tested for Dyson Sphere Program 0.10.34.x using BepInEx 5 / HarmonyX.

DSPModSave is required for per-save UI state persistence.

The mod uses DSP's own research queue and technology-state APIs rather than directly editing research progress or queue data.

## Localisation

Vanilla technology names, descriptions and other game-provided strings continue to use DSP's localisation system.

The mod's own UI strings are currently English-only. Chinese and Japanese have been sanity-tested for layout and font compatibility.

## Feedback and bug reports

Feedback, bug reports and suggestions are welcome.

- Dyson Sphere Program Steam forums: Nekogod
- Discord: Nekogod
- GitHub: Nekogod-dev

## Notes

The mod intentionally keeps the vanilla technology positions as its starting layout rather than redesigning the research tree from scratch. Filtering may compact empty rows and recentre visible results.

Normal and implicit DSP prerequisites are used directly. The mod also infers some item/recipe unlock dependencies that are required in practice but are not represented by the vanilla visible dependency graph.

## Building from source

The project should reference the local DSP, Unity, BepInEx, HarmonyX and DSPModSave assemblies. The repository should not redistribute Dyson Sphere Program or Unity game assemblies.
