# com.dreamy.editor-tools

Editor-only utilities for Dreamy internal Unity projects.

## Scene Tools

- Open `Tools/Dreamy/Scene/Scene Manager`.
- Find all project scenes or add the open/selected scene.
- Enable, disable, reorder, remove, and open Build Settings scenes.
- Put the bootstrap scene first and remove missing scene entries.

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
- `Tools/Dreamy/Project/Clear Console`
- `Assets/Create/Dreamy/Script/Save Data`
- `Assets/Create/Dreamy/Script/Game Service`

This package has no runtime assembly.

Save data editor menu items live in `com.dreamy.datasave`.
