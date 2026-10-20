import assert from "node:assert/strict";
import test from "node:test";
import { createIpc, defineContract, ReceivedFile, IpcValidationError } from "../../dist/index.js";

const port = 32145;
const descriptor = (patch = {}) => ({ url: `http://127.0.0.1:${port}/ipc/download/${"a".repeat(43)}`, byteLength: 3, fileName: "hello.txt", contentType: "text/plain", ...patch });
const contract = defineContract({
  id: "file-test", hash: "f".repeat(64),
  models: { Bundle: { object: { files: { array: { nullable: "file" } }, optional: { optional: { nullable: "file" } } } } },
  mod: {
    file: { wireName: "rpc.mod.file", input: { object: {} }, output: "file" },
    bundle: { wireName: "rpc.mod.bundle", input: { object: {} }, output: { model: "Bundle" } },
  },
  app: { receive: { wireName: "rpc.app.receive", input: { object: { file: "file" } }, output: "safeint" } },
});
const pause = () => new Promise(resolve => setTimeout(resolve, 0));
class Transport {
  messages = new Set(); closes = new Set(); sent = []; response = descriptor();
  connect() {
    queueMicrotask(() => this.receive({ kind: "welcome", name: "connection.welcome", payload: { server: "AdofaiIpc", protocolVersion: 3, peerId: "peer", connectionId: "connection" } }));
    return Promise.resolve();
  }
  send(text) {
    const message = JSON.parse(text); this.sent.push(message);
    if (message.kind === "subscribe") queueMicrotask(() => this.receive({ kind: "namespace", name: "namespace.changed", payload: { status: "ready", featureProtocolMajor: 1 } }));
    if (message.name === "rpc.register") this.result(message, { appId: message.payload.appId, sessionId: "session" });
    if (message.name.startsWith("rpc.mod.")) this.result(message, this.response);
  }
  result(request, payload) { this.receive({ kind: "event", name: "rpc.result", correlationId: request.id, payload }); }
  receive(message) { for (const listener of [...this.messages]) listener(JSON.stringify({ id: "server-message", namespace: contract.id, ...message })); }
  disconnect() { for (const listener of [...this.closes]) listener(); }
  onMessage(listener) { this.messages.add(listener); return () => this.messages.delete(listener); }
  onClose(listener) { this.closes.add(listener); return () => this.closes.delete(listener); }
  close() {}
}
function setup(t, receive = ({ file }) => file.size, options = {}) {
  const transport = new Transport();
  const app = createIpc({ transportFactory: () => transport, startPort: port, endPort: port, ...options }).registerApp({ displayName: "Files" });
  t.after(() => app.dispose());
  return { transport, app, channel: app.use(contract, { receive }) };
}
function mockFile(t, body = "abc", headers = { "X-Byte-Length": "3" }, status = 200) {
  return t.mock.method(globalThis, "fetch", async () => new Response(body, { status, headers }));
}

test("file metadata is typed and content starts lazily, streams once and uses safe HTTP options", async t => {
  const fetch = mockFile(t);
  const { channel } = setup(t);
  const file = await channel.file();
  assert.ok(file instanceof ReceivedFile);
  assert.equal(file.name, "hello.txt"); assert.equal(file.type, "text/plain"); assert.equal(file.size, 3);
  assert.equal(file.url, undefined); assert.equal(file.used, false); assert.equal(fetch.mock.callCount(), 0);
  const stream = file.stream();
  assert.equal(file.used, true); assert.equal(fetch.mock.callCount(), 0);
  assert.equal(await new Response(stream).text(), "abc");
  assert.throws(() => file.stream(), /already been read/);
  assert.equal(fetch.mock.callCount(), 1);
  const [url, options] = fetch.mock.calls[0].arguments;
  assert.equal(url, descriptor().url);
  assert.equal(options.redirect, "error"); assert.equal(options.credentials, "omit"); assert.equal(options.cache, "no-store");
  assert.equal(options.referrerPolicy, "no-referrer");
});

test("nested file arrays preserve omission and explicit null; blob keeps the MIME type", async t => {
  mockFile(t);
  const { channel, transport } = setup(t);
  transport.response = { files: [descriptor(), null] };
  const bundle = await channel.bundle();
  assert.equal(Object.hasOwn(bundle, "optional"), false); assert.equal(bundle.files[1], null);
  const blob = await bundle.files[0].blob();
  assert.equal(blob.type, "text/plain"); assert.equal(blob.size, 3); assert.equal(await blob.text(), "abc");
  transport.response = { files: [], optional: null };
  assert.equal((await channel.bundle()).optional, null);
});

test("localhost discovery accepts numeric loopback files only on the connected port", async t => {
  mockFile(t);
  const { channel, transport } = setup(t, undefined, { host: "localhost", startPort: port + 1, endPort: port + 1 });
  await assert.rejects(channel.file(), IpcValidationError);
  transport.response = descriptor({ url: descriptor().url.replace(":32145", ":32146") });
  assert.equal(await (await channel.file()).text(), "abc");
});

test("mod-initiated app calls receive typed files before reading or acknowledging", async t => {
  const fetch = mockFile(t);
  let received;
  const { channel, transport } = setup(t, ({ file }) => { received = file; return file.size; });
  await channel.ready();
  transport.receive({ kind: "command", name: "rpc.app.receive", payload: { callId: "push", sessionId: channel.sessionId, args: { file: descriptor() } } });
  await pause();
  assert.ok(received instanceof ReceivedFile); assert.equal(fetch.mock.callCount(), 0);
  assert.equal(transport.sent.find(message => message.correlationId === "push").payload, 3);
  assert.equal(await received.text(), "abc");
});

test("untrusted file descriptors are rejected before any HTTP request", async t => {
  const fetch = mockFile(t);
  const { channel, transport } = setup(t);
  for (const patch of [
    { url: descriptor().url.replace("127.0.0.1", "evil.example") },
    { url: descriptor().url.replace(":32145", ":32146") },
    { url: descriptor().url.replace("http:", "https:") },
    { url: descriptor().url.replace("127.0.0.1", "user:pass@127.0.0.1") },
    { url: descriptor().url + "?redirect=1" }, { url: descriptor().url + "#fragment" },
    { url: `http://127.0.0.1:${port}/arbitrary` }, { url: "not a URL" },
    { byteLength: -1 }, { byteLength: 0.5 }, { byteLength: Number.MAX_SAFE_INTEGER + 1 },
    { fileName: "" }, { fileName: "bad\nname" }, { contentType: "text/plain\r\nheader" }, { extra: true },
  ]) { transport.response = descriptor(patch); await assert.rejects(channel.file(), IpcValidationError); }
  assert.equal(fetch.mock.callCount(), 0);
});

test("file readers reject length mismatches, expired files and transport failures", async t => {
  const { channel, transport } = setup(t);
  const cases = [
    ["abc", {}, 200, "invalid_file"],
    ["abc", { "X-Byte-Length": "4" }, 200, "invalid_file"],
    ["ab", { "X-Byte-Length": "3" }, 200, "invalid_file"],
    ["abcd", { "X-Byte-Length": "3" }, 200, "invalid_file"],
    ["missing", {}, 404, "file_expired"],
    ["error", {}, 500, "file_transfer_failed"],
  ];
  const fetch = mockFile(t);
  for (const [body, headers, status, code] of cases) {
    fetch.mock.mockImplementation(async () => new Response(body, { headers, status }));
    const file = await channel.file();
    await assert.rejects(file.text(), error => error.code === code);
  }
  fetch.mock.mockImplementation(async () => { throw new TypeError("network failed"); });
  await assert.rejects((await channel.file()).text(), error => error.code === "file_transfer_failed");
  transport.response = descriptor({ byteLength: 0 });
  fetch.mock.mockImplementation(async () => new Response(new Uint8Array(), { headers: { "X-Byte-Length": "0" } }));
  assert.equal((await (await channel.file()).arrayBuffer()).byteLength, 0);
});

test("read cancellation aborts HTTP; an already aborted read leaves the file unused", async t => {
  let request, canceled = false;
  t.mock.method(globalThis, "fetch", async (_url, options) => {
    request = options;
    return new Response(new ReadableStream({ cancel() { canceled = true; } }), { headers: { "X-Byte-Length": "3" } });
  });
  const { channel } = setup(t);
  const file = await channel.file();
  const early = new AbortController(); early.abort(new Error("early"));
  assert.throws(() => file.stream({ signal: early.signal }), /early/); assert.equal(file.used, false);
  const cancel = new AbortController();
  const pending = file.text({ signal: cancel.signal });
  const rejected = assert.rejects(pending, /stop/);
  await pause(); cancel.abort(new Error("stop")); await rejected;
  assert.equal(request.signal.aborted, true); assert.equal(canceled, true);
});

test("disconnect aborts active and unread streams and invalidates handles from the previous session", async t => {
  let request;
  const fetch = t.mock.method(globalThis, "fetch", async (_url, options) => {
    request = options;
    return new Response(new ReadableStream(), { headers: { "X-Byte-Length": "3" } });
  });
  const { channel, transport } = setup(t);
  const active = await channel.file(), unread = await channel.file(), unopened = await channel.file();
  const pending = active.text();
  const activeRejected = assert.rejects(pending, error => error.code === "unavailable");
  const idleReader = unread.stream().getReader();
  const idleRejected = assert.rejects(idleReader.closed, error => error.code === "unavailable");
  await pause(); transport.disconnect();
  await Promise.all([activeRejected, idleRejected]);
  assert.equal(request.signal.aborted, true);
  assert.throws(() => unopened.stream(), error => error.code === "unavailable");
  assert.equal(fetch.mock.callCount(), 1);
});

test("canceling a stream releases its HTTP body and session cleanup is safe afterwards", async t => {
  let canceled = false, request;
  mockFile(t).mock.mockImplementation(async (_url, options) => {
    request = options;
    return new Response(new ReadableStream({ pull(output) { output.enqueue(new Uint8Array([1])); }, cancel() { canceled = true; } }), { headers: { "X-Byte-Length": "3" } });
  });
  const { channel, app } = setup(t);
  const reader = (await channel.file()).stream().getReader();
  await reader.read(); await reader.cancel();
  assert.equal(canceled, true); assert.equal(request.signal.aborted, true);
  app.dispose();
});
