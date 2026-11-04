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

function mockDocument(t) {
  const previous = Object.getOwnPropertyDescriptor(globalThis, "document");
  const frames = [];
  const attached = new Set();
  class Frame extends EventTarget {
    attributes = {};
    setAttribute(name, value) { this.attributes[name] = value; }
    remove() { attached.delete(this); }
  }
  Object.defineProperty(globalThis, "document", { configurable: true, value: {
    createElement(tag) { assert.equal(tag, "iframe"); const frame = new Frame(); frames.push(frame); return frame; },
    body: { append(frame) { attached.add(frame); } },
  } });
  t.after(() => {
    if (previous) Object.defineProperty(globalThis, "document", previous);
    else delete globalThis.document;
  });
  return { frames, attached };
}

test("browser downloads hand off even very large files without reading or buffering content", async t => {
  const browser = mockDocument(t);
  const fetch = mockFile(t);
  const { channel, transport, app } = setup(t);
  transport.response = descriptor({ byteLength: 8 * 1024 ** 3 });
  const file = await channel.file();
  await file.download();
  assert.equal(file.used, true);
  assert.equal(fetch.mock.callCount(), 0);
  assert.equal(browser.frames.length, 1);
  const frame = browser.frames[0];
  assert.equal(frame.src, descriptor().url);
  assert.equal(frame.hidden, true);
  assert.equal(frame.tabIndex, -1);
  assert.equal(frame.referrerPolicy, "no-referrer");
  assert.equal(frame.attributes["aria-hidden"], "true");
  assert.equal(browser.attached.size, 1);
  await assert.rejects(file.download(), /already been read/);
  assert.throws(() => file.stream(), /already been read/);
  app.dispose();
  assert.equal(browser.attached.size, 0);
});

test("browser downloads reject stale sessions and cannot follow a previous content read", async t => {
  const browser = mockDocument(t);
  mockFile(t);
  const { channel, transport } = setup(t);
  const read = await channel.file();
  await read.text();
  await assert.rejects(read.download(), /already been read/);
  const stale = await channel.file();
  transport.disconnect();
  await assert.rejects(stale.download(), error => error.code === "unavailable");
  assert.equal(browser.frames.length, 0);
});

test("browser navigation nodes are released after a response page or session disconnect", async t => {
  const browser = mockDocument(t);
  const { channel, transport } = setup(t);
  await (await channel.file()).download();
  browser.frames[0].dispatchEvent(new Event("load"));
  assert.equal(browser.attached.size, 0);
  await (await channel.file()).download();
  assert.equal(browser.attached.size, 1);
  transport.disconnect();
  assert.equal(browser.attached.size, 0);
});

test("browser downloads report unsupported environments without fetching a file", async t => {
  const fetch = mockFile(t);
  const { channel } = setup(t);
  await assert.rejects((await channel.file()).download(), error => error.code === "file_download_unavailable");
  assert.equal(fetch.mock.callCount(), 0);
  const streamOnly = new ReceivedFile("empty", "text/plain", 0, () => new ReadableStream({ start(output) { output.close(); } }));
  await assert.rejects(streamOnly.download(), error => error.code === "file_download_unavailable");
  assert.equal(streamOnly.used, false);
  assert.equal(await streamOnly.text(), "");
});

test("mod-initiated callbacks may request a browser download without returning file bytes", async t => {
  const browser = mockDocument(t);
  const fetch = mockFile(t);
  const { channel, transport } = setup(t, async ({ file }) => { await file.download(); return file.size; });
  await channel.ready();
  transport.receive({ kind: "command", name: "rpc.app.receive", payload: { callId: "save", sessionId: channel.sessionId, args: { file: descriptor() } } });
  await pause();
  assert.equal(transport.sent.find(message => message.correlationId === "save").payload, 3);
  assert.equal(browser.frames.length, 1);
  assert.equal(fetch.mock.callCount(), 0);
});

const openedUrl = `http://127.0.0.1:${port}/ipc/files/${"b".repeat(43)}`;
function mockFileService(t, routes = {}) {
  const service = {
    open: () => Response.json({ url: openedUrl }),
    range: request => {
      const [, start, end] = /^bytes=(\d+)-(\d+)$/.exec(request.headers.get("Range"));
      return new Response("abc".slice(Number(start), Number(end) + 1), { status: 206, headers: { "Content-Range": `bytes ${start}-${end}/3` } });
    },
    close: () => new Response(null, { status: 204 }),
    download: () => new Response("abc", { headers: { "X-Byte-Length": "3" } }),
    ...routes,
  };
  return t.mock.method(globalThis, "fetch", async (url, options = {}) => {
    const request = new Request(url, options);
    if (url.includes("/ipc/files/open/")) return service.open(request);
    if (url.endsWith("/close")) return service.close(request);
    if (url.startsWith(openedUrl)) return service.range(request);
    return service.download(request);
  });
}

test("open() serves a file by URL, streams any byte range again and again, and closes it", async t => {
  const fetch = mockFileService(t);
  const { channel } = setup(t);
  const file = await channel.file();
  const opened = await file.open();
  assert.equal(file.used, true);
  assert.equal(opened.url, openedUrl);
  assert.equal(opened.name, "hello.txt"); assert.equal(opened.type, "text/plain"); assert.equal(opened.size, 3);
  const [openUrl, openOptions] = fetch.mock.calls[0].arguments;
  assert.equal(openUrl, descriptor().url.replace("/ipc/download/", "/ipc/files/open/"));
  assert.equal(openOptions.method, "POST"); assert.equal(openOptions.redirect, "error"); assert.equal(openOptions.credentials, "omit");
  assert.equal(await new Response(opened.read({ start: 1, end: 3 })).text(), "bc");
  assert.equal(await new Response(opened.read({ start: 1, end: 3 })).text(), "bc");
  assert.equal(await new Response(opened.read()).text(), "abc");
  assert.equal(fetch.mock.calls[1].arguments[1].headers.Range, "bytes=1-2");
  const calls = fetch.mock.callCount();
  assert.equal(await new Response(opened.read({ start: 2, end: 2 })).text(), "");
  assert.equal(fetch.mock.callCount(), calls, "an empty range needs no request");
  for (const range of [{ start: -1 }, { end: 4 }, { start: 2, end: 1 }, { start: 0.5 }]) assert.throws(() => opened.read(range), RangeError);
  assert.throws(() => file.stream(), /already been read/);
  await opened.close(); await opened.close();
  assert.equal(opened.closed, true);
  const closes = fetch.mock.calls.filter(call => call.arguments[0] === `${openedUrl}/close`);
  assert.equal(closes.length, 1); assert.equal(closes[0].arguments[1].keepalive, true);
  assert.throws(() => opened.read(), error => error.code === "file_closed");
});

test("open() refusals leave the file unread for stream() or download()", async t => {
  const { channel, transport } = setup(t);
  const cases = [
    [() => Response.json({ code: "file_not_seekable", message: "x" }, { status: 409 }), "file_not_seekable"],
    [() => Response.json({ code: "file_open_limit", message: "x" }, { status: 429 }), "file_open_limit"],
    [() => { throw new TypeError("Failed to fetch"); }, "file_open_unavailable"],
    [() => new Response("Not Implemented", { status: 501 }), "file_open_unavailable"],
    [() => Response.json({ code: "surprise" }, { status: 500 }), "file_open_unavailable"],
  ];
  for (const [open, code] of cases) {
    const fetch = mockFileService(t, { open });
    const file = await channel.file();
    await assert.rejects(file.open(), error => error.code === code);
    assert.equal(file.used, false);
    assert.equal(await file.text(), "abc");
    fetch.mock.restore();
  }
  mockFileService(t, { open: () => Response.json({ url: "http://evil.example/ipc/files/" + "b".repeat(43) }) });
  await assert.rejects((await channel.file()).open(), error => error.code === "invalid_file");
  transport.disconnect();
});

test("range reads reject responses that are not the requested bytes", async t => {
  const { channel } = setup(t);
  const cases = [
    [() => new Response("abc"), "file_transfer_failed"],
    [() => new Response("gone", { status: 404 }), "file_expired"],
    [() => new Response("ab", { status: 206, headers: { "Content-Range": "bytes 0-1/3" } }), "file_transfer_failed"],
    [() => new Response("b", { status: 206, headers: { "Content-Range": "bytes 1-2/3" } }), "invalid_file"],
    [() => new Response("bcd", { status: 206, headers: { "Content-Range": "bytes 1-2/3" } }), "invalid_file"],
  ];
  for (const [range, code] of cases) {
    const fetch = mockFileService(t, { range });
    const opened = await (await channel.file()).open();
    await assert.rejects(new Response(opened.read({ start: 1, end: 3 })).text(), error => error.code === code);
    fetch.mock.restore();
  }
});

test("opened files stop reading when their app session ends", async t => {
  mockFileService(t);
  const { channel, transport } = setup(t);
  const opened = await (await channel.file()).open();
  const unopened = await channel.file();
  transport.disconnect();
  assert.throws(() => opened.read(), error => error.code === "unavailable");
  await assert.rejects(unopened.open(), error => error.code === "unavailable");
});
