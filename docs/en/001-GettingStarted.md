# Getting started

[Guide](000-DevelopGuide.md) | [한국어](../kor/001-GettingStarted.md)

The runtime requires ADOFAI and UnityModManager. It targets netstandard2.1 and embeds an
isolated managed WebSocket implementation. The game's Mono HttpListener WebSocket upgrade
API is not used because its implementation throws NotImplementedException.

During v1 preparation, build the local source with `./scripts/run.sh build`. This validates
assemblies and tests, then installs under the configured `Mods/AdofaiIpc` directory. Set
`ADOFAIIPC_SKIP_INSTALL=1` to validate without installing. `./scripts/run.sh package` produces
and verifies the mod ZIP and checksum. These commands do not publish a release.

The server binds IPv4 loopback only. It tries ports 32145 through 32155. Connect to
`ws://127.0.0.1:<port>/ipc/ws` with subprotocol `adofai-ipc.v3`. The SDK scans those WebSocket
ports directly and validates the welcome message. `GET /ipc/health` remains a diagnostic
endpoint and returns server, serverVersion, protocolVersion and port.

Reference the built `build/AdofaiIpc/AdofaiIpc.dll` in a consuming mod, require IPC1.0.0 in its
bootstrap manifest, and ship the pinned dependency artifacts. The TypeScript SDK is currently
consumed as a source snapshot; see [the SDK guide](005-JsClient.md). Do not publish it to npm
as part of this preparation.
