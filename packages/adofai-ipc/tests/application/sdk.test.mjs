import assert from "node:assert/strict";
import test from "node:test";
import { createIpc, defineContract, IpcValidationError } from "../../dist/index.js";
import { Phase, TypedTestContract } from "../../../typespec/tests/fixtures/typed/generated/index.js";

const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function until(predicate, milliseconds = 5000) {
  const end = Date.now() + milliseconds;
  while (!predicate() && Date.now() < end) await pause(10);
  assert.ok(predicate(), "condition did not settle");
}
const value = result => JSON.parse(JSON.stringify(result));
const handlers = { confirm: ({ message }) => message === "Continue?", revision: () => 7, updated: () => {} };

class Transport {
  messages = new Set(); closes = new Set(); sent = [];
  constructor(connectionId = "connection") { this.connectionId = connectionId; }
  connect() {
    queueMicrotask(() => this.receive({ kind: "welcome", name: "connection.welcome", payload: {
      server: "AdofaiIpc", protocolVersion: 3, peerId: "peer", connectionId: this.connectionId,
    } }));
    return Promise.resolve();
  }
  send(text) {
    const message = JSON.parse(text); this.sent.push(message);
    if (message.kind === "subscribe") queueMicrotask(() => this.receive({ kind: "namespace", name: "namespace.changed", namespace: message.namespace, payload: { status: "ready", featureProtocolMajor: 1 } }));
    if (message.name === "rpc.register") this.receive({ kind: "event", name: "rpc.result", namespace: message.namespace, correlationId: message.id, payload: { appId: message.payload.appId, sessionId: `session-${this.connectionId}` } });
  }
  receive(message) { for (const listener of this.messages) listener(JSON.stringify({ id: "server-message", ...message })); }
  disconnect() { for (const listener of [...this.closes]) listener(); }
  close() {}
  onMessage(listener) { this.messages.add(listener); return () => this.messages.delete(listener); }
  onClose(listener) { this.closes.add(listener); return () => this.closes.delete(listener); }
}

test("bad inputs fail before sending and synchronous registration responses are retained", async t => {
  const transport = new Transport();
  const app = createIpc({ transportFactory: () => transport }).registerApp({ displayName: "Test" });
  t.after(() => app.dispose());
  const channel = app.use(TypedTestContract, handlers);
  await Promise.all([channel.ready(), channel.ready()]);
  assert.equal(transport.sent.filter(message => message.name === "rpc.register").length, 1);
  const sent = transport.sent.length;
  for (const input of [{ left: "1", right: 2 }, { left: 1.5, right: 0 }, { left: NaN, right: 0 },
    { left: Infinity, right: 0 }, { left: 2147483648, right: 0 }, { left: 1 }, { left: 1, right: 2, extra: 3 }])
    await assert.rejects(channel.add(input), IpcValidationError);
  const getters = { right: 0 };
  Object.defineProperty(getters, "left", { enumerable: true, get() { throw new Error("getter ran"); } });
  await assert.rejects(channel.add(getters), IpcValidationError);
  assert.equal(transport.sent.length, sent);
  assert.throws(() => app.use(TypedTestContract, handlers), /already uses/);
  app.dispose();
  await assert.rejects(channel.ready(), error => error.code === "disposed");
  assert.equal(transport.messages.size, 0); assert.equal(transport.closes.size, 0);
});

test("use registers eagerly, publishes session readiness and registers again after reconnect", async t => {
  const transports = [];
  const app = createIpc({ transportFactory: () => {
    const transport = new Transport(`connection-${transports.length}`); transports.push(transport); return transport;
  } }).registerApp({ displayName: "Push UI" });
  t.after(() => app.dispose());
  const channel = app.use(TypedTestContract, handlers);
  const statuses = [];
  const off = channel.onStatus((status, error) => { statuses.push([status, error?.code]); });
  await until(() => channel.status === "ready");
  assert.equal(transports[0].sent.filter(message => message.name === "rpc.register").length, 1);
  assert.equal(channel.sessionId, "session-connection-0");
  await Promise.all([channel.ready(), channel.ready()]);
  assert.equal(transports[0].sent.filter(message => message.name === "rpc.register").length, 1);
  assert.deepEqual(statuses.map(([status]) => status), ["unavailable", "initializing", "ready"]);
  transports[0].disconnect();
  assert.equal(channel.status, "unavailable"); assert.equal(channel.sessionId, undefined);
  await until(() => channel.status === "ready" && transports.length === 2);
  assert.equal(channel.sessionId, "session-connection-1");
  assert.equal(transports[1].sent.filter(message => message.name === "rpc.register").length, 1);
  assert.equal(transports[1].sent.find(message => message.name === "rpc.register").payload.appId, app.appId);
  assert.equal(transports[1].sent.filter(message => message.name.startsWith("rpc.mod.")).length, 0);
  off();
});

test("mod callbacks are installed before registration and stale callback results are discarded", async t => {
  const transport = new Transport();
  let finish;
  const pending = new Promise(resolve => { finish = resolve; });
  const originalSend = transport.send.bind(transport);
  transport.send = text => {
    originalSend(text);
    const message = JSON.parse(text);
    if (message.name === "rpc.register") transport.receive({ kind: "command", name: "rpc.app.confirm", namespace: "typed-test", payload: { callId: "initial-push", sessionId: "session-connection", args: { message: "Push on registration" } } });
  };
  let received = 0;
  const app = createIpc({ transportFactory: () => transport }).registerApp({ displayName: "Push UI" });
  t.after(() => app.dispose());
  const channel = app.use(TypedTestContract, { ...handlers, confirm: () => { received++; return pending; } });
  await until(() => received === 1 && channel.status === "ready");
  transport.receive({ kind: "command", name: "rpc.app.confirm", namespace: "typed-test", payload: { callId: "old-session", sessionId: "expired-session", args: { message: "Stale push" } } });
  await pause(0);
  assert.equal(received, 1);
  assert.equal(transport.sent.some(message => message.correlationId === "old-session"), false);
  transport.receive({ kind: "namespace", name: "namespace.changed", namespace: "typed-test", payload: { status: "initializing" } });
  finish(true); await pause(0);
  assert.equal(transport.sent.some(message => message.correlationId === "initial-push"), false);
});

test("a rejected eager registration publishes an error once without retrying in a loop", async t => {
  const transport = new Transport();
  const send = transport.send.bind(transport);
  transport.send = text => {
    const message = JSON.parse(text);
    if (message.name !== "rpc.register") { send(text); return; }
    transport.sent.push(message);
    transport.receive({ kind: "rejected", name: "command.rejected", namespace: message.namespace, correlationId: message.id, payload: { code: "contract_mismatch", message: "Update both packages." } });
  };
  const app = createIpc({ transportFactory: () => transport }).registerApp({ displayName: "Old UI" });
  t.after(() => app.dispose());
  const channel = app.use(TypedTestContract, handlers);
  let failure;
  channel.onStatus((status, error) => { if (status === "error") failure = error; });
  await until(() => failure);
  assert.equal(failure.code, "contract_mismatch");
  await assert.rejects(channel.ready(), error => error.code === "contract_mismatch");
  await pause(30);
  assert.equal(transport.sent.filter(message => message.name === "rpc.register").length, 1);
});

test("app handlers are required and reverse arguments and results use strict validation", async t => {
  const transport = new Transport();
  const app = createIpc({ transportFactory: () => transport }).registerApp({ displayName: "Test" });
  t.after(() => app.dispose());
  assert.throws(() => app.use(TypedTestContract, {}), /Missing app handler/);
  assert.throws(() => app.use(TypedTestContract, { ...handlers, typo() {} }), /Unknown app handler/);
  const channel = app.use(TypedTestContract, { ...handlers, revision: () => "7" });
  await channel.ready();
  const command = (name, callId, args) => transport.receive({ kind: "command", name, namespace: "typed-test", payload: { callId, sessionId: channel.sessionId, args } });
  command("rpc.app.confirm", "bad-request", { message: 1 });
  await until(() => transport.sent.some(message => message.correlationId === "bad-request"));
  assert.equal(transport.sent.find(message => message.correlationId === "bad-request").payload.code, "invalid_request");
  command("rpc.app.revision", "bad-result", {});
  await until(() => transport.sent.some(message => message.correlationId === "bad-result"));
  assert.equal(transport.sent.find(message => message.correlationId === "bad-result").payload.code, "invalid_result");
});

test("disposing rejects pending calls and removes transport listeners", async t => {
  const transport = new Transport();
  const app = createIpc({ transportFactory: () => transport, requestTimeoutMs: 5000 }).registerApp({ displayName: "Test" });
  t.after(() => app.dispose());
  const channel = app.use(TypedTestContract, handlers); await channel.ready();
  const pending = channel.increment();
  await until(() => transport.sent.some(message => message.name === "rpc.mod.increment"));
  const rejected = assert.rejects(pending, error => error.code === "disposed");
  app.dispose(); await rejected;
  assert.equal(transport.messages.size, 0); assert.equal(transport.closes.size, 0);
});

test("a pushed mod error rejects in-flight calls with its original code", async t => {
  const transport = new Transport();
  const app = createIpc({ transportFactory: () => transport }).registerApp({ displayName: "Test" });
  t.after(() => app.dispose());
  const channel = app.use(TypedTestContract, handlers); await channel.ready();
  const pending = channel.increment();
  await until(() => transport.sent.some(message => message.name === "rpc.mod.increment"));
  const rejected = assert.rejects(pending, error => error.code === "maintenance");
  transport.receive({ kind: "namespace", name: "namespace.changed", namespace: "typed-test", payload: { status: "error", error: { code: "maintenance", message: "Try later." } } });
  await rejected;
  await assert.rejects(channel.ready(), error => error.code === "maintenance");
});
