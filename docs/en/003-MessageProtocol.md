# WebSocket message protocol

[Guide](000-DevelopGuide.md) | [한국어](../kor/003-MessageProtocol.md)

Endpoint: `ws://127.0.0.1:32145/ipc/ws` (fallback ports through32155).
Subprotocol: `adofai-ipc.v3`. Product version1.0.0; wire version3. SDK/runtime compatibility
uses the wire version; individual namespaces negotiate their own domain protocol in payload.

Every control message is one UTF-8 JSON text message:

```json
{"kind":"command","namespace":"example-mod","name":"level.open","id":"c1","payload":{"levelId":"abc"}}
```

```json
{"kind":"event","namespace":"example-mod","name":"level.opened","id":"e1","correlationId":"c1","payload":{"levelId":"abc"}}
```

The server first sends `welcome` / `connection.welcome` with server=`AdofaiIpc`, serverVersion,
protocolVersion, peerId and connectionId. The peer sends `hello` with protocolVersion=3,
then `subscribe` for each namespace. Each subscription produces `subscribed`, then a pushed
`namespace` / `namespace.changed` snapshot. A missing namespace is unavailable; it becomes
initializing and ready when registered and initialized later. Mod snapshots follow ready.

| Kind | Meaning |
| --- | --- |
| command | Either peer asks the other to perform a named domain operation. Payload is an object. |
| accepted | Router admitted a command. Named `command.accepted`, correlated to the command. |
| event | Named result, snapshot or unsolicited state change; optional correlationId. |
| rejected | Router or domain rejection: `command.rejected`, payload code/message. |
| namespace | Namespace lifecycle status/version/error snapshot. |
| ping/pong | Transport liveness. No game or application state query. |

Subscribe before sending commands or peer events. Duplicate IDs within the recent1024 command
window of one connection are rejected to avoid repeating side effects. Reconnection does not
replay commands. Applications reconcile their current state with fresh snapshots and domain IDs.

Limits:2MiB per control message/frame, JSON depth32,64 peers,64 namespaces per peer,256 admitted
commands,256 queued messages and8MiB queued bytes per WebSocket. Slow peers are disconnected and
must rehydrate on reconnect. Heartbeats run every10s; stale peers close after45s. File/audio/pixel
data belongs in streams or existing file/native transports, outside JSON control messages.

`GET /ipc/health` and single-use `GET /ipc/download/<ticket>` remain. `POST /ipc`, HTTP namespace
discovery, method discovery and generic result envelopes are removed. Domain job/lease/revision
fields belong in payload and are validated by the owning application.
