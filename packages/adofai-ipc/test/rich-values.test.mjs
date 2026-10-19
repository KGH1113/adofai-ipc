import assert from "node:assert/strict";
import test from "node:test";
import { createIpc, defineContract, IpcValidationError } from "../dist/index.js";

const rich = { object: {
  labels: { array: "string" },
  maybeLabel: { nullable: "string" },
  omitted: { optional: "string" },
  optionalNullable: { optional: { nullable: "string" } },
  ratio: "float64", count: "safeint",
  phase: { enum: ["pending", "done"], name: "Phase" },
  literal: { enum: ["fixed"] },
} };
const contract = defineContract({
  id: "rich-values", hash: "f".repeat(64), models: { Entry: rich },
  mod: {
    echo: { wireName: "rpc.mod.echo", input: { object: { entry: { model: "Entry" } } }, output: { model: "Entry" } },
    noOp: { wireName: "rpc.mod.noop", input: { object: {} }, output: "void" },
  },
  app: { updated: { wireName: "rpc.app.updated", input: { object: { entry: { model: "Entry" } } }, output: "void" } },
});
const entry = () => ({ labels: ["한글", ""], maybeLabel: null, ratio: 1.25, count: Number.MAX_SAFE_INTEGER, phase: "done", literal: "fixed" });
const plain = value => JSON.parse(JSON.stringify(value));
const pause = () => new Promise(resolve => setTimeout(resolve, 0));

class Transport {
  messages = new Set(); closes = new Set(); sent = []; response;
  connect() {
    queueMicrotask(() => this.receive({ kind: "welcome", name: "connection.welcome", payload: {
      server: "AdofaiIpc", protocolVersion: 3, peerId: "peer", connectionId: "connection",
    } }));
    return Promise.resolve();
  }
  send(text) {
    const message = JSON.parse(text); this.sent.push(message);
    if (message.kind === "subscribe") queueMicrotask(() => this.receive({ kind: "namespace", name: "namespace.changed", payload: { status: "ready", featureProtocolMajor: 1 } }));
    if (message.name === "rpc.register") this.result(message, { appId: message.payload.appId, sessionId: "session" });
    if (message.name === "rpc.mod.echo") this.result(message, this.response ? this.response(message.payload.entry) : message.payload.entry);
    if (message.name === "rpc.mod.noop") this.result(message, this.response ? this.response() : null);
  }
  result(request, payload) { this.receive({ kind: "event", name: "rpc.result", correlationId: request.id, payload }); }
  receive(message) { for (const listener of [...this.messages]) listener(JSON.stringify({ id: "server-message", namespace: contract.id, ...message })); }
  onMessage(listener) { this.messages.add(listener); return () => this.messages.delete(listener); }
  onClose(listener) { this.closes.add(listener); return () => this.closes.delete(listener); }
  close() {}
}
function setup(t, handler = () => {}) {
  const transport = new Transport();
  const app = createIpc({ transportFactory: () => transport }).registerApp({ displayName: "Rich UI" });
  t.after(() => app.dispose());
  return { transport, app, channel: app.use(contract, { updated: handler }) };
}

test("arrays, nullable fields, enums and numeric bounds round-trip without coercion", async t => {
  const { channel } = setup(t);
  for (const count of [Number.MIN_SAFE_INTEGER, Number.MAX_SAFE_INTEGER, 0]) {
    for (const ratio of [Number.MAX_VALUE, Number.MIN_VALUE, -2.75, 0]) {
      const input = { ...entry(), count, ratio };
      assert.deepEqual(plain(await channel.echo({ entry: input })), input);
    }
  }
  assert.deepEqual(plain(await channel.echo({ entry: { ...entry(), maybeLabel: "present", labels: [] } })), { ...entry(), maybeLabel: "present", labels: [] });
});

test("optional omission, undefined and explicit null remain distinct", async t => {
  const { channel, transport } = setup(t);
  const absent = await channel.echo({ entry: entry() });
  assert.equal(Object.hasOwn(absent, "omitted"), false);
  assert.equal(Object.hasOwn(absent, "optionalNullable"), false);
  const undefinedFields = await channel.echo({ entry: { ...entry(), omitted: undefined, optionalNullable: undefined } });
  assert.equal(Object.hasOwn(undefinedFields, "omitted"), false);
  assert.equal(Object.hasOwn(undefinedFields, "optionalNullable"), false);
  const explicitNull = await channel.echo({ entry: { ...entry(), omitted: "", optionalNullable: null } });
  assert.equal(Object.hasOwn(explicitNull, "optionalNullable"), true);
  assert.equal(explicitNull.optionalNullable, null);
  assert.equal(explicitNull.omitted, "");
  const sent = transport.sent.length;
  await assert.rejects(channel.echo({ entry: { ...entry(), omitted: null } }), IpcValidationError);
  await assert.rejects(channel.echo({ entry: { ...entry(), maybeLabel: undefined } }), IpcValidationError);
  assert.equal(transport.sent.length, sent);
});

test("invalid richer values and array accessors fail before transmission", async t => {
  const { channel, transport } = setup(t);
  await channel.ready();
  const invalid = [
    { labels: null }, { labels: [1] }, { labels: [undefined] }, { labels: new Array(1) },
    { maybeLabel: 0 }, { omitted: null }, { optionalNullable: false },
    { ratio: NaN }, { ratio: Infinity }, { ratio: -Infinity }, { ratio: "1.5" },
    { count: Number.MAX_SAFE_INTEGER + 1 }, { count: Number.MIN_SAFE_INTEGER - 1 }, { count: 1.5 },
    { phase: "Pending" }, { phase: 0 }, { literal: "other" },
  ];
  const getterArray = ["value"];
  Object.defineProperty(getterArray, "0", { enumerable: true, get() { throw new Error("getter ran"); } });
  invalid.push({ labels: getterArray });
  const customArray = ["value"]; customArray.toJSON = () => ["different"];
  invalid.push({ labels: customArray });
  const sent = transport.sent.length;
  for (const patch of invalid) await assert.rejects(channel.echo({ entry: { ...entry(), ...patch } }), IpcValidationError);
  assert.equal(transport.sent.length, sent);
});

test("invalid wire results are rejected and void returns expose undefined", async t => {
  const { channel, transport } = setup(t);
  assert.equal(await channel.noOp(), undefined);
  transport.response = value => ({ ...value, phase: "unknown" });
  await assert.rejects(channel.echo({ entry: entry() }), IpcValidationError);
  transport.response = () => ({});
  await assert.rejects(channel.noOp(), IpcValidationError);
});

test("void app handlers reply with explicit wire null and strict argument validation", async t => {
  const received = [];
  const { channel, transport } = setup(t, input => { received.push(input); });
  await channel.ready();
  transport.receive({ kind: "command", name: "rpc.app.updated", payload: { callId: "push", sessionId: channel.sessionId, args: { entry: { ...entry(), optionalNullable: null } } } });
  await pause();
  assert.equal(received.length, 1);
  assert.equal(received[0].entry.optionalNullable, null);
  const reply = transport.sent.find(message => message.correlationId === "push");
  assert.equal(reply.name, "rpc.result"); assert.equal(reply.payload, null);
  transport.receive({ kind: "command", name: "rpc.app.updated", payload: { callId: "invalid", sessionId: channel.sessionId, args: { entry: { ...entry(), labels: [3] } } } });
  await pause();
  assert.equal(received.length, 1);
  assert.equal(transport.sent.find(message => message.correlationId === "invalid").payload.code, "invalid_request");
});

test("void app handlers reject unexpected return values", async t => {
  const { channel, transport } = setup(t, () => "unexpected");
  await channel.ready();
  transport.receive({ kind: "command", name: "rpc.app.updated", payload: { callId: "invalid-result", sessionId: channel.sessionId, args: { entry: entry() } } });
  await pause();
  assert.equal(transport.sent.find(message => message.correlationId === "invalid-result").payload.code, "invalid_result");
});
