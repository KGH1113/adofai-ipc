import test from "node:test";
import assert from "node:assert/strict";
import { IpcConnection } from "../../dist/transport.js";

function status(channel, major) {
  channel.receive({ kind: "namespace", namespace: channel.name, name: "namespace.state",
    payload: { status: "ready", ...(major === undefined ? {} : { featureProtocolMajor: major }) } });
}
test("a matching feature major becomes ready", async () => {
  const client = new IpcConnection();
  const channel = client.namespace("test", 10);
  status(channel, 10);
  await channel.whenReady();
  assert.equal(channel.status, "ready");
  client.close();
});
test("a wrong or missing feature major blocks commands and domain events", async () => {
  for (const major of [undefined, 9, 11]) {
    const client = new IpcConnection();
    const channel = client.namespace("test", 10);
    let events = 0;
    channel.on("snapshot", () => events++);
    status(channel, major);
    assert.equal(channel.error.code, "feature_protocol_mismatch");
    await assert.rejects(channel.whenReady(), error => error.code === "feature_protocol_mismatch");
    assert.throws(() => channel.send("write", {}), error => error.code === "feature_protocol_mismatch");
    channel.receive({ kind: "event", name: "snapshot", payload: {} });
    assert.equal(events, 0);
    channel.disconnected();
    status(channel, 10);
    await channel.whenReady();
    client.close();
  }
});
test("requirements are validated and consistent for repeated subscriptions", () => {
  const client = new IpcConnection();
  for (const major of [0, -1, 1.5, NaN]) assert.throws(() => client.namespace("test", major), TypeError);
  const channel = client.namespace("test", 1);
  assert.equal(client.namespace("test", 1), channel);
  assert.throws(() => client.namespace("test", 2), TypeError);
  client.close();
});
test("subscriptions without a feature requirement keep wire compatibility", async () => {
  const client = new IpcConnection();
  const channel = client.namespace("test");
  status(channel, undefined);
  await channel.whenReady();
  client.close();
});
