# Changelog

All notable changes to this project are documented in this file.

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
