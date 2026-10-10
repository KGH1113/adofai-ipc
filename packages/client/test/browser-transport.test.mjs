import assert from "node:assert/strict";
import test from "node:test";
import { browserTransportFactory } from "../dist/index.js";

function browser(t) {
  const previous = Object.fromEntries(["WebSocket", "addEventListener", "removeEventListener"].map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
  const sockets = []; const page = new EventTarget();
  class Socket {
    static OPEN = 1;
    readyState = 0; bufferedAmount = 0; closes = 0;
    constructor() { sockets.push(this); }
    open() { this.readyState = 1; this.onopen?.(); }
    close() { this.closes++; this.readyState = 3; }
    send() {}
  }
  Object.defineProperty(globalThis, "WebSocket", { value: Socket, configurable: true });
  Object.defineProperty(globalThis, "addEventListener", { value: page.addEventListener.bind(page), configurable: true });
  Object.defineProperty(globalThis, "removeEventListener", { value: page.removeEventListener.bind(page), configurable: true });
  const transport = browserTransportFactory("ws://127.0.0.1:32145/ipc/ws", "adofai-ipc.v3");
  t.after(() => {
    transport.close();
    for (const [name, descriptor] of Object.entries(previous)) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor); else delete globalThis[name];
    }
  });
  return { sockets, page, transport };
}

test("an error after open invalidates the connection without waiting for a close event", async t => {
  const { sockets, transport } = browser(t);
  const reasons = []; transport.onClose(reason => reasons.push(reason));
  const connected = transport.connect(); sockets[0].open(); await connected;
  const error = sockets[0].onerror;
  error(new Event("error")); error(new Event("error"));
  assert.equal(reasons.length, 1); assert.equal(sockets[0].closes, 1);
  assert.equal(sockets[0].onmessage, null); assert.equal(sockets[0].onclose, null);
});

test("navigation closes the old page socket and cannot close a later session", async t => {
  const { sockets, page, transport } = browser(t);
  let ended = 0; transport.onClose(() => ended++);
  const connected = transport.connect(); sockets[0].open(); await connected;
  page.dispatchEvent(new Event("pagehide")); page.dispatchEvent(new Event("pagehide"));
  assert.equal(ended, 1); assert.equal(sockets[0].closes, 1);
  const next = browserTransportFactory("ws://127.0.0.1:32145/ipc/ws", "adofai-ipc.v3");
  const opened = next.connect(); sockets[1].open(); await opened;
  next.close(); page.dispatchEvent(new Event("pagehide"));
  assert.equal(sockets[1].closes, 1); assert.equal(sockets[0].closes, 1);
});

test("closing a connecting probe rejects its pending connection and detaches callbacks", async t => {
  const { sockets, transport } = browser(t);
  const connecting = transport.connect(); const rejected = assert.rejects(connecting);
  transport.close(); await rejected;
  assert.equal(sockets[0].closes, 1); assert.equal(sockets[0].onopen, null);
});
