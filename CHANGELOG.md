# Changelog - com.dreamy.editor-tools

All notable changes to this package will be documented in this file.

## [Unreleased]

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
