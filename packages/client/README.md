# TypeScript SDK

[English](../../docs/en/005-JsClient.md) | [한국어](../../docs/kor/005-JsClient.md)

The v1 SDK is not published to npm during this preparation. Generate a reproducible snapshot:

```bash
./scripts/run.sh client-sync /path/to/consumer/vendor/adofai-ipc
```

Commit the generated src, LICENSE, README.md and source.json in the consumer. The manifest records
the upstream commit, dirty-source marker, SDK/wire version and SHA-256 per source file. Regenerate
after changing upstream. Avoid absolute file dependencies and manual copies of the transport.
Point Vite/TypeScript aliases at the raw source if keeping an existing import alias; remove the
registry dependency and update lockfiles. Raw source requires no tsup-only version definition.

```ts
import { IpcConnection } from "./vendor/adofai-ipc/src";

// Register listeners before connecting so the initial snapshot cannot be lost.
const connection = new IpcConnection();
const channel = connection.namespace("example-mod");
channel.on<{ revision: number }>("library.snapshot", snapshot => projectLibrary(snapshot));
channel.on<{ revision: number }>("library.changed", change => projectLibrary(change));
channel.on("level.opened", (payload, message) => applyOpenedLevel(payload, message.correlationId));
channel.on("command.rejected", (error, message) => showFailure(error, message.correlationId));
await connection.start();
await channel.whenReady({ timeoutMs: 15_000 });
const commandId = channel.send("level.open", { levelId: "abc" });

channel.onCommand("render.frame", (frame, message) => {
    // Perform the domain operation; report its actual completion.
    channel.reply(message.id, "render.frame.ready", { path: "/tmp/frame.png" });
});
// On application shutdown:
connection.close();
```

`connectIpc(options)` creates and starts a connection. `new IpcConnection(options)` lets you create
channels and install listeners first. Defaults: host127.0.0.1, ports32145..32155, timeout1500ms per
probe. Options include transportFactory and onProtocolMismatch. A browser uses global WebSocket;
Node without global WebSocket and native applications inject an IpcTransport factory.

The transport port exposes connect, send, close, onMessage and onClose. send/close may be async.
Its onMessage callback supplies UTF-8 JSON strings. It must support the supplied WebSocket
subprotocol and dispose probe connections/listeners on failure.

Connection: namespace(name), onState(listener), state, peerId, connectionId, close(). States:
disconnected, connected, reconnecting, closed, incompatible. Reconnect uses bounded jittered
backoff and resubscribes without replay. onState immediately reports the current state.
Initial server absence also enters reconnecting: start() completes after the first discovery
round, while channel.whenReady() waits for the actual mod. Concurrent start calls share discovery.
checkConnection() probes a retained socket or wakes a pending retry. Correlated ping/pong checks
replace silently dead sockets (heartbeatIntervalMs defaults to 10000, heartbeatTimeoutMs to 15000).
The browser transport releases its socket on pagehide and treats an error after open as a lost
connection even if no close event arrives. close() cancels active discovery and heartbeat timers.

Channel: on(name,callback), onCommand(name,callback), send(name,payload,{id,signal}), emit(name,
payload), reply(correlationId,name,payload), onStatus(callback), status, error, whenReady({timeoutMs,
signal}). send returns an ID immediately, not a Promise of a generic result. An AbortSignal only
guards a send before transmission; cancel an already running operation with a domain command.
Unsubscribe functions release handlers. The application owns correlation, domain timeouts,
disconnection rejection, retry policy and state projection.

Wire mismatch throws IpcProtocolMismatchError(codeINCOMPATIBLE) and stops probing/retrying.
whenReady is event driven; default timeout15000ms, IpcTimeoutError(codeTIMEOUT), or
IpcNamespaceError(code from the mod). A readiness timeout does not stop discovery: applications
must observe channel.onStatus and recover their read-only projections when the mod becomes ready.
`isIpcUnavailable` matches UNAVAILABLE from sends without a connection or an explicitly closed
connection. Mutating commands are never replayed after reconnecting.
