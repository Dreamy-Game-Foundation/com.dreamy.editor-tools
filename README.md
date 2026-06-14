# com.dreamy.editor-tools

Editor-only utilities for Dreamy internal Unity projects.

## Scene Tools

- Open `Tools/Dreamy/Scene/Scene Manager`.
- Find all project scenes or add the open/selected scene.
- Enable, disable, reorder, remove, and open Build Settings scenes.
- Put the bootstrap scene first and remove missing scene entries.
- Use the Dreamy controls beside Unity's Play controls for previous, next,
  reload, scene selection, Play From Bootstrap, and Time Scale.

## Package Tools

- Open `Tools/Dreamy/Package/Package Manager`.
- View and search direct dependencies from `manifest.json`.
- Add package IDs or Git URLs through Unity Package Manager.
- Remove packages, request dependency resolution, and open manifest/lock files.

## Build Tools

- Open `Tools/Dreamy/Build/Build Manager`.
- Configure product version, Android version code, and iOS build number.
- Select target and output directory.
- Toggle development, debugging, profiler, deep profiling, and clean cache.
- Validate enabled scenes, then Build or Build & Run.
- Project-specific build options are stored in EditorPrefs using a project key.

## General Tools

- `Tools/Dreamy/PlayerPrefs/Clear All`
- `Tools/Dreamy/Project/Open manifest.json`
- `Tools/Dreamy/Data Debugger` for JSON config validation and save-file inspection
- `Tools/Dreamy/Project/Clear Console`
- `Assets/Create/Dreamy/Script/Save Data`
- `Assets/Create/Dreamy/Script/Game Service`

## Hotkeys

- `F5`: compile project with a clean script cache
- `Ctrl/Cmd+L`: toggle Inspector lock
- `Ctrl/Cmd+W`: close the focused Editor window
- `Ctrl/Cmd+Shift+Alt+S`: save scene and project
- `Alt+PageUp`: previous enabled Build Settings scene
- `Alt+PageDown`: next enabled Build Settings scene
- `Alt+R`: reload current scene

All shortcuts can be reassigned from `Edit > Shortcuts > Dreamy`.

This package has no runtime assembly.

Save data editor menu items live in `com.dreamy.datasave`.
