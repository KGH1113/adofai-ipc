# Getting started

[Guide](000-DevelopGuide.md) | [한국어](../kor/001-GettingStarted.md)

Build with `./scripts/run.sh build`. This builds the DLLs and runs tests without installing
anything into the game. `./scripts/run.sh package` produces a bundle ZIP and SHA-256 checksum.
The runtime targets netstandard2.1 and embeds its managed WebSocket transport.

Reference AdofaiIpc.Contracts.dll and AdofaiIpc.Loader.dll in your mod project.
Do not reference AdofaiIpc.Runtime.dll: it must be loaded only after candidate selection.
Copy the bundle contents into your own mod's directory:

```text
Mods/ExampleMod/
  Info.json                       # Your mod's own entry point
  ExampleMod.dll
  AdofaiIpc.Contracts.dll
  AdofaiIpc.Loader.dll
  ipc/
    manifest.json
    AdofaiIpc.Runtime.dll
```

The contract and loader are next to your mod assembly so the mod loader can resolve references.
Use the game's Newtonsoft.Json; do not bundle another copy. The runtime uses only UnityEngine.CoreModule
and Newtonsoft.Json from the game. There is no separate Mods/AdofaiIpc installation.

Call IpcLoader.GetRuntime(modsDirectory, requirements) on the Unity game thread, where
modsDirectory is the parent directory containing all installed mod directories.
Register your feature and MarkReady after its domain state is prepared.

The server binds IPv4 loopback ports 32145..32155. Connect to
`ws://127.0.0.1:<port>/ipc/ws` with subprotocol `adofai-ipc.v3`.
`GET /ipc/health` reports server, serverVersion, protocolVersion and port.
See [version selection](006-BundledRuntime.md) and [the mod API](002-ModApi.md).
