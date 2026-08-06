import assert from "node:assert/strict";
import test from "node:test";

import {
  AdofaiIpcClient,
  IpcConnectionError,
  IpcHttpError,
  IpcResponseError,
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

test("browser AbortError messages are normalized to IpcTimeoutError", async () => {
  const client = new AdofaiIpcClient({
    requestTimeoutMs: 5,
    fetch: (_input, init) => new Promise((_resolve, reject) => {
      init.signal.addEventListener(
        "abort",
        () => reject(new DOMException("The operation was aborted", "AbortError")),
        { once: true }
      );
    })
  });

  await assert.rejects(client.health(), (error) => {
    assert.ok(error instanceof IpcTimeoutError);
    assert.equal(error.code, "TIMEOUT");
    assert.equal(error.timeoutMs, 5);
    assert.notEqual(error.message, "The operation was aborted");
    return true;
  });
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

test("IPC error responses preserve protocol error codes", async () => {
  const client = new AdofaiIpcClient({
    fetch: async () => jsonResponse({
      ok: false,
      error: {
        code: "namespace_not_found",
        message: "Namespace not found: example-mod"
      }
    }, { status: 404 })
  });

  await assert.rejects(client.getNamespace("example-mod"), (error) => {
    assert.ok(error instanceof IpcResponseError);
    assert.equal(error.code, "namespace_not_found");
    assert.equal(error.message, "Namespace not found: example-mod");
    return true;
  });
});

test("request timeout can be overridden per call", async () => {
  const client = new AdofaiIpcClient({
    requestTimeoutMs: 5,
    fetch: async () => {
      await new Promise((resolve) => setTimeout(resolve, 15));
      return jsonResponse({ ok: true, result: "pong" });
    }
  });

  const result = await client.call({
    namespace: "example-mod",
    method: "ping",
    timeoutMs: 50
  });

  assert.equal(result, "pong");
});

test("namespace client accepts a per-call timeout override", async () => {
  const client = new AdofaiIpcClient({
    requestTimeoutMs: 5,
    fetch: async () => {
      await new Promise((resolve) => setTimeout(resolve, 15));
      return jsonResponse({ ok: true, result: "pong" });
    }
  });

  const result = await client.namespace("example-mod").call(
    "ping",
    {},
    { timeoutMs: 50 }
  );

  assert.equal(result, "pong");
});

test("waitForNamespace waits for registration", async () => {
  let attempts = 0;
  const client = new AdofaiIpcClient({
    fetch: async () => {
      attempts++;
      if (attempts < 3) {
        return jsonResponse({
          ok: false,
          error: {
            code: "namespace_not_found",
            message: "Namespace not found: example-mod"
          }
        }, { status: 404 });
      }

      return jsonResponse({
        namespace: "example-mod",
        displayName: "Example Mod",
        version: "1.0.0",
        status: "initializing",
        methods: ["ping"]
      });
    }
  });

  const namespace = await client.waitForNamespace("example-mod", {
    timeoutMs: 100,
    pollIntervalMs: 1
  });

  assert.equal(namespace.namespace, "example-mod");
  assert.equal(attempts, 3);
});

test("waitForNamespace can wait until the namespace is ready", async () => {
  let attempts = 0;
  const client = new AdofaiIpcClient({
    fetch: async () => {
      attempts++;
      return jsonResponse({
        namespace: "example-mod",
        displayName: "Example Mod",
        version: "1.0.0",
        status: attempts < 3 ? "initializing" : "ready",
        error: null,
        methods: ["ping"]
      });
    }
  });

  const namespace = await client.waitForNamespace("example-mod", {
    status: "ready",
    timeoutMs: 100,
    pollIntervalMs: 1
  });

  assert.equal(namespace.status, "ready");
  assert.equal(attempts, 3);
});

test("waitForNamespace reports namespace_error while waiting for ready", async () => {
  const client = new AdofaiIpcClient({
    fetch: async () => jsonResponse({
      namespace: "example-mod",
      displayName: "Example Mod",
      version: "1.0.0",
      status: "error",
      error: {
        code: "database_failed",
        message: "Could not load the activity database."
      },
      methods: ["ping"]
    })
  });

  await assert.rejects(
    client.waitForNamespace("example-mod", { status: "ready" }),
    (error) => {
      assert.ok(error instanceof IpcResponseError);
      assert.equal(error.code, "namespace_error");
      assert.equal(error.message, "Could not load the activity database.");
      return true;
    }
  );
});

test("waitForNamespace reports namespace_initializing when ready wait expires", async () => {
  const client = new AdofaiIpcClient({
    fetch: async () => jsonResponse({
      namespace: "example-mod",
      displayName: "Example Mod",
      version: "1.0.0",
      status: "initializing",
      error: null,
      methods: ["ping"]
    })
  });

  await assert.rejects(
    client.waitForNamespace("example-mod", {
      status: "ready",
      timeoutMs: 5,
      pollIntervalMs: 1
    }),
    (error) => {
      assert.ok(error instanceof IpcResponseError);
      assert.equal(error.code, "namespace_initializing");
      return true;
    }
  );
});

test("waitForNamespace reports namespace_not_found when registration never appears", async () => {
  const client = new AdofaiIpcClient({
    fetch: async () => jsonResponse({
      ok: false,
      error: {
        code: "namespace_not_found",
        message: "Namespace not found: example-mod"
      }
    }, { status: 404 })
  });

  await assert.rejects(
    client.waitForNamespace("example-mod", { timeoutMs: 5, pollIntervalMs: 1 }),
    (error) => {
      assert.ok(error instanceof IpcResponseError);
      assert.equal(error.code, "namespace_not_found");
      return true;
    }
  );
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

test("tryConnect applies probeTimeoutMs while scanning ports", async () => {
  const probedPorts = [];
  const client = await tryConnect({
    startPort: 32145,
    endPort: 32146,
    probeTimeoutMs: 5,
    fetch: (input, init) => {
      const port = Number(new URL(String(input)).port);
      probedPorts.push(port);
      if (port === 32145) return waitForAbort(input, init);
      return Promise.resolve(jsonResponse({
        ok: true,
        server: "AdofaiIpc",
        protocolVersion: 1,
        port
      }));
    }
  });

  assert.equal(client.baseUrl, "http://127.0.0.1:32146");
  assert.deepEqual(probedPorts, [32145, 32146]);
});

test("tryConnect uses a short probe timeout without applying it to later requests", async () => {
  const client = await tryConnect({
    startPort: 32145,
    endPort: 32145,
    probeTimeoutMs: 5,
    requestTimeoutMs: 50,
    fetch: async (input) => {
      const url = new URL(String(input));
      if (url.pathname === "/ipc/health") {
        return jsonResponse({
          ok: true,
          server: "AdofaiIpc",
          protocolVersion: 1,
          port: 32145
        });
      }

      await new Promise((resolve) => setTimeout(resolve, 15));
      return jsonResponse({ ok: true, result: "pong" });
    }
  });

  const result = await client.call({ namespace: "example-mod", method: "ping" });
  assert.equal(result, "pong");
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

test("isIpcUnavailable only recognizes unavailable connection errors", () => {
  assert.equal(isIpcUnavailable(new IpcConnectionError()), true);
  assert.equal(isIpcUnavailable(new IpcTimeoutError(500)), false);
  assert.equal(isIpcUnavailable(new IpcHttpError(500, "failed")), false);
  assert.equal(isIpcUnavailable(new Error("failed")), false);
  assert.equal(isIpcUnavailable(null), false);
});
