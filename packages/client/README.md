# @adofai-ipc/client

TypeScript client for the AdofaiIpc local HTTP IPC gateway.

## Install

```bash
npm install @adofai-ipc/client
```

## Usage

```ts
import { tryConnect } from "@adofai-ipc/client";

const client = await tryConnect();

const result = await client.call({
  namespace: "tufhelper2",
  method: "level.open-from-id",
  params: {
    id: "1234"
  }
});
```

You can also bind calls to a namespace.

```ts
const tufhelper = client.namespace("tufhelper2");

await tufhelper.call("level.open-from-id", {
  id: "1234"
});
```

## API

### `tryConnect(options?)`

Finds a running AdofaiIpc server by probing `/ipc/health`.

The client and server product versions must match exactly. A mismatch throws
`IpcVersionMismatchError` and stops port probing. Use `onVersionMismatch` to display application UI;
the callback runs at most once per `tryConnect` call and does not suppress the typed error.

```ts
await tryConnect({
  onVersionMismatch(error) {
    if (error.direction === "client_outdated") location.reload();
    else openAdofaiIpcDownloadNotice(error);
  }
});
```

`server_outdated` means the mod must be updated, `client_outdated` means the web bundle must be
updated, and `legacy_server` means the server did not provide a valid product version. React
StrictMode and consumer retry loops can call `tryConnect` more than once, so applications should
store mismatch as a terminal connection state and deduplicate their own modal or banner.

Defaults:

- host: `127.0.0.1`
- startPort: `32145`
- endPort: `32155`
- probeTimeoutMs: `500`
- requestTimeoutMs: `10000`

`tryConnect` only confirms that the AdofaiIpc listener is running. It does not mean that a target
namespace is registered or that the owning mod has finished initializing.

```ts
const client = await tryConnect({
  probeTimeoutMs: 500,
  requestTimeoutMs: 10_000
});
```

The legacy `timeoutMs` option remains as a deprecated alias for both values.

### `new AdofaiIpcClient(options?)`

Creates a client for a known AdofaiIpc base URL.

```ts
const client = new AdofaiIpcClient({
  baseUrl: "http://127.0.0.1:32145"
});
```

### `client.call(options)`

Calls a namespace method through `POST /ipc`.

```ts
await client.call({
  namespace: "tufhelper2",
  method: "activity.get",
  timeoutMs: 30_000
});
```

### `client.namespace(name)`

Creates a namespace-bound helper.

### `client.health()`

Calls `GET /ipc/health`.

### `client.listNamespaces()`

Calls `GET /ipc/namespaces`.

### `client.getNamespace(name)`

Calls `GET /ipc/namespaces/{name}`.

### `client.waitForNamespace(name, options?)`

Polls namespace discovery until the target namespace is registered. Set `status: "ready"` to also
wait until the namespace owner explicitly marks initialization complete.

```ts
await client.waitForNamespace("tufhelper2", {
  status: "ready",
  timeoutMs: 15_000,
  pollIntervalMs: 100
});
```

AdofaiIpc namespaces have the strict states `initializing`, `ready`, and `error`. A ready wait that
expires reports `namespace_initializing`; an initialization failure reports `namespace_error`.

## Error handling

Connection failures are reported as `IpcConnectionError` with code `UNAVAILABLE`. Request
timeouts use the more specific `IpcTimeoutError`, which extends `IpcConnectionError` and has code
`TIMEOUT`. Protocol failures, including `namespace_not_found`, are reported as
`IpcResponseError`. `isIpcUnavailable` only matches the `UNAVAILABLE` state, not timeouts.

```ts
import {
  IpcTimeoutError,
  isIpcUnavailable,
  tryConnect
} from "@adofai-ipc/client";

try {
  const client = await tryConnect();
  await client.health();
} catch (error) {
  if (error instanceof IpcTimeoutError) {
    console.warn(`AdofaiIpc timed out after ${error.timeoutMs} ms.`);
  } else if (isIpcUnavailable(error)) {
    console.warn("AdofaiIpc is unavailable.");
  }
}
```

`tryConnect` treats individual probe failures as expected and only throws an `UNAVAILABLE`
`IpcConnectionError` after every candidate port has failed. A successful probe does not guarantee
that any specific namespace or mode feature is ready.

## Notes

This package uses the global `fetch` API. Node.js 18 or newer is recommended.

Browser requests are still subject to AdofaiIpc namespace Origin policy.
