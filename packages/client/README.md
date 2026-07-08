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

Defaults:

- host: `127.0.0.1`
- startPort: `32145`
- endPort: `32155`
- timeoutMs: `500`

### `new AdofaiIpcClient(options?)`

Creates a client for a known AdofaiIpc base URL.

```ts
const client = new AdofaiIpcClient({
  baseUrl: "http://127.0.0.1:32145"
});
```

### `client.call(options)`

Calls a namespace method through `POST /ipc`.

### `client.namespace(name)`

Creates a namespace-bound helper.

### `client.health()`

Calls `GET /ipc/health`.

### `client.listNamespaces()`

Calls `GET /ipc/namespaces`.

### `client.getNamespace(name)`

Calls `GET /ipc/namespaces/{name}`.

## Notes

This package uses the global `fetch` API. Node.js 18 or newer is recommended.

Browser requests are still subject to AdofaiIpc namespace Origin policy.
