# Changelog - com.dreamy.editor-tools

All notable changes to this package will be documented in this file.

## [Unreleased]

## [1.1.0] - 2026-11-06

- Added in-window clipboard requirements and a Copy Install Command button for Ubuntu Wayland/X11; Windows shows that no extra installation is needed.

- Added Editor-only Game View Screenshot with Copy, Save and Copy + Save modes, realtime countdown/cancel, preview, Copy Image/Save As, menu and configurable shortcut.
- Added Windows DIB image clipboard and Ubuntu wl-copy/xclip adapters with dependency checks and in-memory PNG transfer; Copy-only creates no image files.
- Added frame timeout, lifecycle cleanup, independent output results and Editor regressions; declared ScreenCapture/ImageConversion module dependencies.

- Added configurable Dreamy/Clear Save Data shortcut (Ctrl+Shift+Alt+C; Cmd+Shift+Option+C on macOS), delegating to Datasave with its existing confirmation dialog.

## [1.0.1] - 2026-10-15

- Resume increasing release versions above the highest existing v1.x tag after the 0.2.2 version regression.
- Synchronize package metadata and installation documentation with v1.0.1; preserve all existing tags and historical changelog entries.

### Added

- Inline expandable object/list details directly below their parent row, including JSON stored inside strings
- Nested list pagination, row actions, and vertical field layout for wide items
- Variable row heights and matching drag/drop positions for expanded rows
- Test save reset removes the primary file, runtime recovery backup, temporary file and Editor backup history without creating new backups
- Backup-only save discovery and Play Mode guard for reliable test resets
- Renamed Editor Auto Backup to Backup on Save; production runtime backups remain unchanged
- Editor audio mute and Scene View 2D/3D main toolbar toggles
- Configurable Dreamy shortcuts for compile, Inspector lock, close window,
  save all, and scene navigation

### Improved

- Cached table columns and filters, and limited row drawing to the visible viewport
- Protected text editing from row shortcuts and tracked unsaved changes on window close
- Prevented overwriting externally changed files, reset stale data after load failures,
  and used unique backup names
- Removed broad asset refresh when saving a single config

## [0.2.1] - 2026-06-07

### Fixed

- Replaced ScriptableSingleton build settings with project-scoped EditorPrefs JSON
- Prevented duplicate singleton creation during Package Manager initialization

## [0.2.0] - 2026-06-07

### Added

- Scene Manager for Build Settings scene discovery and ordering
- Package Manager for Git/package install, remove, and resolve workflows
- Build Manager with persisted target, output, debug, profiler, and cache options
- Build Settings and application identifier validation

## [0.1.0] - 2026-06-06

### Added

- PlayerPrefs clear menu item
- Manifest open menu item
- Console clear menu item
- SaveData and GameService script templates

### Changed

- Save folder menu items moved to `com.dreamy.datasave`
