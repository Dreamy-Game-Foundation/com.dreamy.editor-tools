# Changelog - com.dreamy.editor-tools

All notable changes to this package will be documented in this file.

## [Unreleased]

### Added

- Configurable Dreamy shortcuts for compile, Inspector lock, close window,
  save all, and scene navigation

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
