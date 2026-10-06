import assert from "node:assert/strict";
import { once } from "node:events";
import { spawn } from "node:child_process";
import test from "node:test";
import { connectIpc, IpcConnection, IpcProtocolMismatchError, IpcUnavailableError } from "../dist/index.js";

const tick = () => new Promise(resolve => setTimeout(resolve, 0));
const until = async predicate => { for (let i = 0; i < 150 && !predicate(); i++) await new Promise(resolve => setTimeout(resolve, 10)); assert.ok(predicate(), "condition did not settle"); };
const packet = (kind, name, payload = {}, namespace = "test-mod", correlationId) => ({ kind, name, id: `server-${Math.random()}`, namespace, correlationId, payload });
class Transport {
  messages = new Set(); closes = new Set(); sent = []; closed = 0;
  constructor({ fail = false, protocol = 3, status = "ready", onSend } = {}) { Object.assign(this, { fail, protocol, status, onSend }); }
  async connect() {
    if (this.fail) throw new Error("connection refused");
    queueMicrotask(() => this.receive(packet("welcome", "connection.welcome", { server: "AdofaiIpc", protocolVersion: this.protocol, serverVersion: "1.0.0", peerId: "peer1", connectionId: "connection1" }, undefined)));
  }
  send(text) { const msg = JSON.parse(text); this.sent.push(msg); if (msg.kind === "subscribe") queueMicrotask(() => this.receive(packet("namespace", "namespace.changed", { status: this.status }, msg.namespace))); this.onSend?.(msg, this); }
  receive(msg) { for (const listener of [...this.messages]) listener(typeof msg === "string" ? msg : JSON.stringify(msg)); }
  disconnect() { for (const listener of [...this.closes]) listener(new Error("socket closed")); }
  close() { this.closed++; }
  onMessage(listener) { this.messages.add(listener); return () => this.messages.delete(listener); }
  onClose(listener) { this.closes.add(listener); return () => this.closes.delete(listener); }
}
async function setup(options = {}) { const transport = new Transport(options); const connection = await connectIpc({ startPort: 32145, endPort: 32145, connectTimeoutMs: 30, transportFactory: () => transport }); const channel = connection.namespace("test-mod"); return { transport, connection, channel }; }

test("discovery closes failed probes and binds one persistent connection", async () => {
  const attempts = [];
  const connection = await connectIpc({ startPort: 32145, endPort: 32147, connectTimeoutMs: 30, transportFactory: (url, protocol) => { assert.equal(protocol, "adofai-ipc.v3"); const transport = new Transport({ fail: attempts.length < 2 }); attempts.push({ url, transport }); return transport; } });
  assert.deepEqual(attempts.map(item => item.url), [32145, 32146, 32147].map(port => `ws://127.0.0.1:${port}/ipc/ws`));
  for (const { transport } of attempts.slice(0, 2)) { assert.equal(transport.closed, 1); assert.equal(transport.messages.size, 0); assert.equal(transport.closes.size, 0); }
  assert.equal(connection.state, "connected"); connection.close(); assert.equal(attempts[2].transport.messages.size, 0);
});
test("initially unavailable and protocol mismatch are distinct terminal outcomes", async () => {
  await assert.rejects(connectIpc({ startPort: 32145, endPort: 32145, transportFactory: () => new Transport({ fail: true }) }), IpcUnavailableError);
  const bad = new Transport({ protocol: 2 }); let mismatch;
  await assert.rejects(connectIpc({ startPort: 32145, endPort: 32145, transportFactory: () => bad, onProtocolMismatch: error => { mismatch = error; } }), IpcProtocolMismatchError);
  assert.equal(mismatch.code, "INCOMPATIBLE"); assert.equal(bad.closed, 1); assert.equal(bad.messages.size, 0);
});
test("readiness follows pushed lifecycle; timeout and abort release their listeners", async () => {
  const { connection, transport, channel } = await setup({ status: "initializing" });
  await tick(); const ready = channel.whenReady({ timeoutMs: 100 }); transport.receive(packet("namespace", "namespace.changed", { status: "ready" })); await ready;
  transport.receive(packet("namespace", "namespace.changed", { status: "initializing" }));
  await assert.rejects(channel.whenReady({ timeoutMs: 5 }), error => error.code === "TIMEOUT");
  const abort = new AbortController(); const pending = channel.whenReady({ signal: abort.signal }); abort.abort(); await assert.rejects(pending, error => error.name === "AbortError");
  transport.receive(packet("namespace", "namespace.changed", { status: "error", error: { code: "broken_index", message: "Rebuild library." } }));
  await assert.rejects(channel.whenReady(), error => error.code === "broken_index"); connection.close();
});
test("commands return IDs and named results and rejections retain correlation", async () => {
  const { connection, transport, channel } = await setup(); await channel.whenReady();
  const outcomes = []; const off = channel.on("level.opened", (payload, message) => outcomes.push({ payload, message }));
  const id = channel.send("level.open", { levelId: "abc" }, { id: "open1" }); assert.equal(id, "open1");
  transport.receive(packet("event", "level.opened", { levelId: "abc" }, "test-mod", id)); assert.equal(outcomes[0].message.correlationId, id);
  let rejection; channel.on("command.rejected", (payload, message) => { rejection = { payload, message }; });
  transport.receive(packet("rejected", "command.rejected", { code: "bad_level", message: "Pick another level." }, "test-mod", id));
  assert.equal(rejection.payload.code, "bad_level"); assert.equal(rejection.message.correlationId, id);
  off(); transport.receive(packet("event", "level.opened", {})); assert.equal(outcomes.length, 1); connection.close();
});
test("server commands are answered by correlated peer events and callbacks are isolated", async () => {
  const { connection, transport, channel } = await setup(); await channel.whenReady(); let updates = 0;
  channel.on("progress.changed", () => { throw new Error("consumer bug"); }); channel.on("progress.changed", () => updates++);
  channel.onCommand("render.frame", (_payload, msg) => channel.reply(msg.id, "render.frame.ready", { path: "/tmp/frame.png" }));
  transport.receive(packet("event", "progress.changed", { revision: 3 })); assert.equal(updates, 1);
  const command = packet("command", "render.frame"); transport.receive(command);
  const reply = transport.sent.at(-1); assert.equal(reply.kind, "event"); assert.equal(reply.correlationId, command.id); assert.equal(reply.name, "render.frame.ready");
  transport.receive(packet("ping", "connection.ping", {}, undefined)); assert.equal(transport.sent.at(-1).kind, "pong"); connection.close();
});
test("reconnect invalidates state and resubscribes without replaying commands", async () => {
  const instances = [];
  const connection = await connectIpc({ startPort: 32145, endPort: 32145, connectTimeoutMs: 30, transportFactory: () => { const value = new Transport(); instances.push(value); return value; } });
  const channel = connection.namespace("test-mod"); await channel.whenReady(); channel.send("dangerous.work", {}, { id: "one-side-effect" });
  instances[0].disconnect(); assert.equal(connection.state, "reconnecting"); assert.equal(channel.status, "unavailable");
  await until(() => connection.state === "connected"); await channel.whenReady();
  assert.equal(instances[1].sent.filter(msg => msg.kind === "subscribe").length, 1); assert.equal(instances[1].sent.filter(msg => msg.kind === "command").length, 0);
  assert.equal(instances[0].messages.size, 0); connection.close(); await tick(); assert.equal(instances[1].messages.size, 0);
});
test("outgoing control limits, IDs and closed state fail before transmission", async () => {
  const { connection, transport, channel } = await setup(); await channel.whenReady(); const count = transport.sent.length;
  assert.throws(() => channel.send("level.open", { blob: "x".repeat(2 * 1024 * 1024) }), RangeError);
  assert.throws(() => channel.send("level.open", {}, { id: "x".repeat(129) }), TypeError);
  assert.equal(transport.sent.length, count); connection.close(); assert.throws(() => channel.send("level.open", {}), IpcUnavailableError);
});
test("channels created before start can install snapshot listeners first", async () => {
  const transport = new Transport({ onSend(msg, tx) { if (msg.kind === "subscribe") queueMicrotask(() => tx.receive(packet("event", "state.snapshot", { revision: 1 }, msg.namespace))); } });
  const connection = new IpcConnection({ startPort: 32145, endPort: 32145, transportFactory: () => transport });
  const channel = connection.namespace("test-mod"); let snapshot; channel.on("state.snapshot", payload => { snapshot = payload; });
  await connection.start(); await channel.whenReady(); assert.equal(snapshot.revision, 1); connection.close();
});
test("real C# gateway and browser WebSocket SDK interoperate in both directions", { skip: !process.env.ADOFAIIPC_PROTOCOL_FIXTURE || !globalThis.WebSocket }, async t => {
  const child = spawn(process.env.DOTNET_EXE ?? `${process.env.HOME}/.dotnet/dotnet`, [process.env.ADOFAIIPC_PROTOCOL_FIXTURE, "--serve"], { stdio: ["ignore", "pipe", "pipe"] });
  let connection;
  t.after(() => { connection?.close(); child.kill("SIGTERM"); });
  let output = ""; child.stdout.on("data", value => { output += value; });
  await until(() => /IPC_TEST_SERVER_PORT=([0-9]+)/.test(output)); const port = Number(output.match(/IPC_TEST_SERVER_PORT=([0-9]+)/)[1]);
  connection = new IpcConnection({ startPort: port, endPort: port, connectTimeoutMs: 1000 });
  const channel = connection.namespace("test-mod"); const snapshots = []; channel.on("state.snapshot", payload => snapshots.push(payload));
  await connection.start(); await channel.whenReady(); await until(() => snapshots.length === 1);
  let opened; channel.on("level.opened", (payload, msg) => { opened = { payload, msg }; }); const id = channel.send("level.open", { levelId: "live" });
  await until(() => opened); assert.equal(opened.msg.correlationId, id); assert.equal(opened.payload.levelId, "live");
  let peerVerified; channel.onCommand("peer.answer", (payload, msg) => channel.reply(msg.id, "peer.answered", { output: payload.input * 2 }));
  channel.on("peer.verified", payload => { peerVerified = payload; }); channel.send("peer.challenge", {}); await until(() => peerVerified); assert.equal(peerVerified.output, 84);
  let ticket; channel.on("download.ready", payload => { ticket = payload; }); channel.send("level.download", {}); await until(() => ticket);
  const response = await fetch(ticket.url); assert.equal(await response.text(), "a level file"); assert.equal(response.headers.get("x-byte-length"), "12");
  connection.close(); child.kill("SIGTERM"); await once(child, "exit");
});
