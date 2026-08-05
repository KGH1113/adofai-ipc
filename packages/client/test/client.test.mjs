import assert from "node:assert/strict";
import test from "node:test";

import {
  AdofaiIpcClient,
  IpcConnectionError,
  IpcHttpError,
  IpcTimeoutError,
  isIpcUnavailable,
  tryConnect
} from "../dist/index.js";

const jsonResponse = (body, init = {}) => new Response(JSON.stringify(body), {
  headers: { "Content-Type": "application/json" },
  ...init
});

const waitForAbort = (_input, init) => new Promise((_resolve, reject) => {
  init.signal.addEventListener("abort", () => reject(init.signal.reason), { once: true });
});

test("every request surface reports a typed timeout", async (t) => {
  const operations = {
    health: (client) => client.health(),
    listNamespaces: (client) => client.listNamespaces(),
    getNamespace: (client) => client.getNamespace("example-mod"),
    call: (client) => client.call({ namespace: "example-mod", method: "ping" }),
    namespaceCall: (client) => client.namespace("example-mod").call("ping")
  };

  for (const [name, invoke] of Object.entries(operations)) {
    await t.test(name, async () => {
      const client = new AdofaiIpcClient({ fetch: waitForAbort, timeoutMs: 5 });

      await assert.rejects(invoke(client), (error) => {
        assert.ok(error instanceof IpcTimeoutError);
        assert.ok(error instanceof IpcConnectionError);
        assert.equal(error.name, "IpcTimeoutError");
        assert.equal(error.code, "TIMEOUT");
        assert.equal(error.timeoutMs, 5);
        assert.equal(error.message, "AdofaiIpc request timed out after 5 ms.");
        return true;
      });
    });
  }
});

test("connection failures preserve Error details and cause", async () => {
  const original = new Error("socket unavailable");
  const client = new AdofaiIpcClient({ fetch: async () => Promise.reject(original) });

  await assert.rejects(client.health(), (error) => {
    assert.ok(error instanceof IpcConnectionError);
    assert.equal(error.code, "UNAVAILABLE");
    assert.equal(error.message, original.message);
    assert.equal(error.cause, original);
    return true;
  });
});

test("non-Error rejections become connection errors and preserve cause", async () => {
  const original = { reason: "offline" };
  const client = new AdofaiIpcClient({ fetch: async () => Promise.reject(original) });

  await assert.rejects(client.health(), (error) => {
    assert.ok(error instanceof IpcConnectionError);
    assert.equal(error.code, "UNAVAILABLE");
    assert.equal(error.message, "Could not connect to AdofaiIpc.");
    assert.equal(error.cause, original);
    return true;
  });
});

test("HTTP failures remain IpcHttpError instances", async () => {
  const client = new AdofaiIpcClient({
    fetch: async () => new Response("service unavailable", { status: 503 })
  });

  await assert.rejects(client.health(), (error) => {
    assert.ok(error instanceof IpcHttpError);
    assert.equal(error.status, 503);
    assert.equal(error.message, "service unavailable");
    return true;
  });
});

test("tryConnect swallows a failed probe and returns the next healthy port", async () => {
  const probedPorts = [];
  const client = await tryConnect({
    startPort: 32145,
    endPort: 32146,
    fetch: async (input) => {
      const port = Number(new URL(String(input)).port);
      probedPorts.push(port);
      if (port === 32145) throw new Error("offline");
      return jsonResponse({ ok: true, server: "AdofaiIpc", protocolVersion: 1, port });
    }
  });

  assert.equal(client.baseUrl, "http://127.0.0.1:32146");
  assert.deepEqual(probedPorts, [32145, 32146]);
});

test("tryConnect reports UNAVAILABLE after all probes fail", async () => {
  await assert.rejects(
    tryConnect({
      startPort: 32145,
      endPort: 32146,
      fetch: async () => Promise.reject(new Error("offline"))
    }),
    (error) => {
      assert.ok(error instanceof IpcConnectionError);
      assert.equal(error.code, "UNAVAILABLE");
      assert.equal(error.message, "Could not connect to AdofaiIpc on 127.0.0.1:32145-32146.");
      return true;
    }
  );
});

test("isIpcUnavailable recognizes connection errors and timeouts", () => {
  assert.equal(isIpcUnavailable(new IpcConnectionError()), true);
  assert.equal(isIpcUnavailable(new IpcTimeoutError(500)), true);
  assert.equal(isIpcUnavailable(new IpcHttpError(500, "failed")), false);
  assert.equal(isIpcUnavailable(new Error("failed")), false);
  assert.equal(isIpcUnavailable(null), false);
});
