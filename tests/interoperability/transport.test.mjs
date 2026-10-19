import assert from "node:assert/strict";
import { once } from "node:events";
import { spawn } from "node:child_process";
import test from "node:test";
import { connectIpc, IpcConnection } from "../../packages/adofai-ipc/dist/transport.js";
import { until } from "./support.mjs";

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

test("real gateway restarts and page reloads recover without recreating the browser tab", { skip: !process.env.ADOFAIIPC_PROTOCOL_FIXTURE || !globalThis.WebSocket }, async t => {
  const children = []; let connection;
  t.after(() => { connection?.close(); for (const child of children) if (child.exitCode === null && child.signalCode === null) child.kill("SIGTERM"); });
  const launch = async () => {
    const child = spawn(process.env.DOTNET_EXE ?? `${process.env.HOME}/.dotnet/dotnet`, [process.env.ADOFAIIPC_PROTOCOL_FIXTURE, "--serve"], { stdio: ["ignore", "pipe", "pipe"] });
    children.push(child); let output = ""; child.stdout.on("data", value => { output += value; });
    await until(() => /IPC_TEST_SERVER_PORT=([0-9]+)/.test(output));
    return { child, port: Number(output.match(/IPC_TEST_SERVER_PORT=([0-9]+)/)[1]) };
  };
  let server = await launch(); const port = server.port;
  const options = { startPort: port, endPort: port, connectTimeoutMs: 1000, heartbeatIntervalMs: 50, heartbeatTimeoutMs: 200 };
  connection = await connectIpc(options);
  const channel = connection.namespace("test-mod"); await channel.whenReady();
  for (let restart = 0; restart < 2; restart++) {
    const oldId = connection.connectionId;
    const exited = once(server.child, "exit"); server.child.kill("SIGTERM"); await exited;
    await until(() => connection.state === "reconnecting");
    server = await launch(); assert.equal(server.port, port);
    connection.checkConnection(); await channel.whenReady({ timeoutMs: 3000 });
    assert.equal(connection.state, "connected"); assert.notEqual(connection.connectionId, oldId);
  }
  for (let reload = 0; reload < 3; reload++) {
    const old = connection; const oldId = old.connectionId; old.close();
    connection = await connectIpc(options); await connection.namespace("test-mod").whenReady();
    assert.equal(old.state, "closed"); assert.notEqual(connection.connectionId, oldId);
  }
  connection.checkConnection(); await new Promise(resolve => setTimeout(resolve, 250));
  assert.equal(connection.state, "connected");
});
