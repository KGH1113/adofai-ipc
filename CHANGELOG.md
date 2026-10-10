# Changelog

All notable changes to this project are documented in this file.

## [2.0.0] - Unreleased

- Bundle contract, local loader and runtime DLLs in each consumer mod.
- Select one compatible stable runtime for the game process; keep it alive across consumer toggles.
- Separate product version, fixed contract ABI, wire major and feature protocol major.
- Remove the common UMM mod, automatic installation, dependency activation, shims, updater,
  launcher, migration, dependency dialogs and their scripts/tests.
- Preserve WebSocket wire 3, local peers, snapshots, cancellation and streamed downloads.
- Replace unsupported asynchronous delegate calls in the embedded transport with ThreadPool work.
- Add cross-version loader fixtures and independent feature-lifetime WebSocket tests.

## [0.1.0] - 2026-07-10

Initial public release.

### Added

- A single localhost HTTP listener shared by multiple ADOFAI mods.
- Namespace and method registration with discovery endpoints.
- Per-namespace origin policies.
- Unity main-thread handler registration.
- Port fallback from `32145` through `32155`.
- JavaScript and TypeScript client package documentation.

### Changed

- AdofaiIpc now loads directly through UnityModManager without JALib.

[0.1.0]: https://github.com/KGH1113/adofai-ipc/releases/tag/v0.1.0
