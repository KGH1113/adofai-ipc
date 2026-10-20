# adofai-ipc

Typed bidirectional IPC between ADOFAI mods and external apps. The SDK handles
connection discovery, reconnection, app registration and contract validation.

## Install

```sh
npm install --save-exact adofai-ipc@2.1.0
```

The ADOFAI mod must include the compatible ADOFAI-IPC 2.1 runtime bundle. This
package contains the JavaScript SDK; it does not install files into the game.

## Use a generated contract

Generate a TypeScript contract from the mod's TypeSpec definition, then import
it alongside the SDK. For a contract with a mod method `echo(text: string): string`:

```ts
import { ipc } from "adofai-ipc";
import { EchoContract } from "./generated/echo/index.js";

const app = ipc.registerApp({ displayName: "Example app" });
const channel = app.use(EchoContract, {});

await channel.ready();
const result = await channel.echo({ text: "Hello" });
console.log(result);

// Dispose when the app or owning UI component is destroyed.
app.dispose();
```

The second argument to `use()` implements the app methods declared by the
contract. Pass `{}` when the contract has no app methods. Inputs and results are
validated against the generated contract; unknown fields and invalid values fail
the call.

`use()` starts registration immediately. `ready()` waits for registration;
`channel.onStatus(listener)` reports `unavailable`, `initializing`, `ready` or
`error` and returns an unsubscribe function. Connections and registrations recover
after a disconnect. Application commands are not replayed automatically.

Import `createIpc` to configure discovery ports and timeouts. Low-level transport
APIs are available from `adofai-ipc/transport`. The package includes ESM, CommonJS
and TypeScript declarations and requires Node.js 18 or newer when used in Node.js.

See the [repository documentation](https://github.com/KGH1113/adofai-ipc/tree/main/docs)
for TypeSpec contracts and the C# mod API.

## Files and browser downloads

A generated method returning `ipc.File` yields a single-use `ReceivedFile`.
For a contract declaring `exportRecording(): ipc.File`, save large recordings
with the browser's download manager:

```ts
const file = await channel.exportRecording();
await file.download();
```

The SDK hands the request to the browser without reading the full file into
JavaScript memory. The browser controls the save location, progress and cancel
action. This promise resolves when the request is handed off, not when saving
finishes; HTTP failures or browser download blocking may not be observable by
the page. Keep the IPC session connected while downloading.

Use `stream({ signal })`, `text()`, `arrayBuffer()` or `blob()` to consume file
contents inside the app instead. Choose one consumption method per file, start
within 60 seconds, and request a new file after reconnecting. `download()` requires
a browser document; stream reading remains available in other environments.

## License

MIT
