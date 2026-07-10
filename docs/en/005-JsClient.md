# AdofaiIpc Developer Guide - JavaScript/TypeScript Client

[Guide index](000-DevelopGuide.md) | [Korean](../005-JsClient.md)

1. [Package](#1-package)
2. [Automatic port discovery](#2-automatic-port-discovery)
3. [Calling namespaces](#3-calling-namespaces)
4. [Building the package](#4-building-the-package)

---

## 1. Package

The client is published as `@adofai-ipc/client` and is maintained in
`packages/client` inside this repository.

```bash
npm install @adofai-ipc/client
```

The package includes TypeScript declarations and supports both ESM and CommonJS consumers.
It requires a runtime with `fetch` and `AbortController`, such as a modern browser or Node.js 18
or later.

---

## 2. Automatic port discovery

AdofaiIpc starts at port `32145` and may fall back through `32155`. Use `tryConnect` to find the
active listener by calling `/ipc/health` on each candidate port.

```ts
import { tryConnect } from "@adofai-ipc/client";

const client = await tryConnect();
console.log(client.baseUrl);
```

Discovery options can override the host, port range, fetch implementation, and timeout.

```ts
const client = await tryConnect({
  host: "127.0.0.1",
  startPort: 32145,
  endPort: 32155,
  timeoutMs: 750
});
```

`tryConnect` throws `IpcConnectionError` if it cannot find an AdofaiIpc server.

---

## 3. Calling namespaces

Call a method by passing its namespace, method, and parameters:

```ts
const result = await client.call({
  namespace: "example-mod",
  method: "level.open-from-id",
  params: {
    id: "1234"
  }
});
```

Create a namespace-scoped client when several calls target the same mod:

```ts
const exampleMod = client.namespace("example-mod");

await exampleMod.call("level.open-from-id", {
  id: "1234"
});
```

The client also exposes discovery helpers:

```ts
await client.health();
await client.listNamespaces();
await client.getNamespace("example-mod");
```

Protocol errors are reported as `IpcResponseError`, while non-successful HTTP responses are
reported as `IpcHttpError`.

---

## 4. Building the package

Build only the client package from the repository root:

```bash
pnpm --filter @adofai-ipc/client build
```

Run TypeScript checks for the workspace:

```bash
pnpm check
```
