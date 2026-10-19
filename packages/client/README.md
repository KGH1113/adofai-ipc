# ADOFAI-IPC JavaScript/TypeScript SDK

The SDK connects external applications to ADOFAI mods on the same computer.
It handles local server discovery, WebSocket subscriptions, readiness and reconnection.
Mods and applications exchange named commands, results and state updates.

- [English client guide](../../docs/en/005-JsClient.md)
- [한국어 클라이언트 가이드](../../docs/kor/005-JsClient.md)
- [Migrating from v0 to v2](../../docs/en/008-Migration.md)

## Include the SDK in an application

The current v2 development workflow uses a source snapshot rather than an npm release.
Run from the ADOFAI-IPC repository root:

```bash
./scripts/run.sh client-sync /path/to/your-app/src/vendor/adofai-ipc
```

Commit the generated `src/`, `LICENSE`, `README.md` and `source.json` in the application.
The manifest records the upstream commit, dirty-source marker, SDK/wire versions and SHA-256 per source file.
Update by changing upstream and regenerating the snapshot.

Import from `./vendor/adofai-ipc/src` in an application file such as `src/ipc-demo.ts`.
The source needs no tsup-specific version substitution. Compile it with the application.
If preserving an existing import alias, update both TypeScript and bundler aliases.
Remove the old registry dependency and update the lockfile when migrating.

## First connection

The [client guide](../../docs/en/005-JsClient.md#2-connecting-and-sending-a-command) contains a complete counter example
paired with the [C# mod example](../../docs/en/002-ModApi.md#2-registering-a-feature).

Create an `IpcConnection`, obtain `namespace("example-mod", 1)`, and install listeners before calling `start()`.
Wait for the channel's `whenReady()` before sending commands.
The second namespace argument must match the mod's feature message major.

`send()` returns a command ID. Receive named result events with `on()` and match their `correlationId`.
Reconnection restores subscriptions without replaying commands. Use fresh snapshots to recover state.
Call `close()` when the connection is no longer needed.

## Develop the SDK

```bash
./scripts/run.sh client
```

This builds, type-checks and tests the SDK without publishing it.
