# ADOFAI-IPC v2 developer guide

[한국어](../kor/000-DevelopGuide.md)

ADOFAI-IPC 2.0.0 is a process-owned local WebSocket gateway. Each consumer mod bundles DLLs,
registers its feature, and disposes only that registration on disable. The first caller does
not own the server. The host remains alive until Unity quits or unloads the application.
The runtime has no UnityModManager, JALib, installer, updater, launcher or migration dependency.

Runtime product version 2.0.0, contract major 1 (assembly identity 1.0.0.0), wire major 3,
and each feature's message protocol major are independent version axes.

1. [Getting started](001-GettingStarted.md)
2. [Mod API](002-ModApi.md)
3. [WebSocket message protocol](003-MessageProtocol.md)
4. [Lifecycle and threading](004-Lifecycle.md)
5. [TypeScript SDK](005-JsClient.md)
6. [Bundled runtime and version contract](006-BundledRuntime.md)
7. [Streaming downloads](007-Downloads.md)

Keep domain services independent of IPC. Inject an IPC adapter at the mod entry point.
