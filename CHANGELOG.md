# Changelog

## 1.0.0

First stable release.

- Added collapsible technology details panel.
- Reworked Search / Filters into a compact flyout beside the vanilla top tabs.
- Replaced filter cycling with explicit Matrix and Combat selectors.
- Added White Matrix-only compact upgrade layout.
- Added Infinite-only filtering for upgrades.
- Added direct `Q+` research queue action to technology nodes.
- Added improved selected-path, prerequisite and immediate-descendant highlighting.
- Added partial-research percentages and repeatable-upgrade level presentation.
- Added per-save persistence for page, filters, zoom and pan through DSPModSave.
- Preserved vanilla right-click queue removal behaviour.
- Fixed graph panning so dragging empty space no longer clears the current selection.
- Improved inferred dependency handling for practical recipe/item unlock requirements.
- Audited research, prerequisite, matrix and queue handling against DSP 0.10.34 decompiled source and switched several paths to vanilla APIs/constants where available.
- Tested multiple resolutions and UI scales.
- Sanity-tested English, Simplified Chinese and Japanese UI layouts.
- General release cleanup and regression testing.
