import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const sdk = require("../../dist/index.cjs");
const transport = require("../../dist/transport.cjs");

test("CommonJS entry points share transport error identities", async t => {
  const messages = new Set();
  let mismatch;
  const observed = new Promise(resolve => { mismatch = resolve; });
  const app = sdk.createIpc({
    startPort: 32145,
    endPort: 32145,
    connectTimeoutMs: 100,
    onProtocolMismatch: error => mismatch(error),
    transportFactory: () => ({
      onMessage(listener) { messages.add(listener); return () => messages.delete(listener); },
      onClose() { return () => {}; },
      connect() {
        queueMicrotask(() => {
          const text = JSON.stringify({ kind: "welcome", name: "connection.welcome", id: "welcome",
            payload: { server: "AdofaiIpc", protocolVersion: 999 } });
          for (const listener of messages) listener(text);
        });
        return Promise.resolve();
      },
      send() {},
      close() {},
    }),
  }).registerApp({ displayName: "CommonJS test" });
  t.after(() => app.dispose());
  app.use(sdk.defineContract({ id: "cjs-test", hash: "0".repeat(64), models: {}, mod: {}, app: {} }), {});
  const error = await observed;
  assert.ok(error instanceof transport.IpcProtocolMismatchError);
});
