using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using AdofaiIpc;
using AdofaiIpc.Core;
using AdofaiIpc.Server;
using AdofaiIpc.Contracts;
using AdofaiIpc.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
  static void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); }
  static async Task Main(string[] args)
  {
    if (!args.Contains("--serve"))
    {
      TestDiscardedDispatcherQueue();
      TestDisconnectedEventDelivery();
      await TestSharedRuntime();
    }
    var queue = new ConcurrentQueue<Action>();
    var registry = new IpcRegistry(action => queue.Enqueue(action));
    var ns = registry.RegisterNamespace("test-mod", new IpcNamespaceInfo { Version = "1.0.0", AllowedOrigins = new[] { "https://allowed.example" } });
    int executions = 0;
    ns.RegisterCommand("level.open", cmd => { Interlocked.Increment(ref executions); cmd.Reply("level.opened", new { levelId = (string)cmd.Payload["levelId"] }); });
    ns.RegisterMainThreadCommand("queued.work", cmd => { Interlocked.Increment(ref executions); cmd.Reply("queued.done", new { }); });
    ns.RegisterCommand("denied.work", cmd => cmd.Reject("bad_level", "Pick another level."));
    ns.RegisterCommand("denied.details", cmd => cmd.Reject("invalid_field", "Choose a valid output.", new { field = "outputDirectory", row = 2 }));
    ns.RegisterDownloadCommand("level.download", cmd => new IpcDownloadSource(new MemoryStream(Encoding.UTF8.GetBytes("a level file")), 12, "test.adofai"));
    string largeChart = "{\"comment\":\"" + new string('한', IpcProtocol.MaxMessageBytes) + "\"}";
    ns.RegisterCommand("chart.inline", cmd => cmd.Reply("chart.snapshot", new { levelText = largeChart }));
    ns.RegisterCommand("chart.stream", cmd => ns.ReplyDownload(cmd, "chart.snapshot",
      new IpcDownloadSource(destination => {
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        writer.Write(largeChart);
      }, Encoding.UTF8.GetByteCount(largeChart), "test.adofai", "application/json; charset=utf-8"),
      new { LevelSessionId = "level-1", FloorCount = 4896 }));
    var failedTicketSources = new ConcurrentQueue<TrackedDownloadStream>();
    ns.RegisterCommand("chart.bad-metadata", cmd => {
      var source = new TrackedDownloadStream(); failedTicketSources.Enqueue(source);
      ns.ReplyDownload(cmd, "chart.snapshot", new IpcDownloadSource(source, 0, "test.adofai"), new { inline = largeChart });
    });
    ns.RegisterCommand("connection.check", cmd => cmd.Reply("connection.checked", new { }));
    ns.RegisterCommand("peer.challenge", cmd => ns.SendCommandToPeer(cmd.PeerId, "peer.answer", new { input = 42 }));
    ns.PeerEvent += evt => { if (evt.Name == "peer.answered") ns.SendToPeer(evt.PeerId, "peer.verified", evt.Payload, evt.CorrelationId); };
    ns.PeerSubscribed += peer => ns.SendToPeer(peer.PeerId, "state.snapshot", new { revision = 1 });
    var server = new IpcServer(registry); server.Start();
    try
    {
      if (args.Contains("--serve"))
      {
        ns.MarkReady(); Console.WriteLine("IPC_TEST_SERVER_PORT=" + server.Port); Console.Out.Flush();
        await Task.Delay(Timeout.Infinite); return;
      }
      using var client = await Socket.Connect(server.Port, "https://allowed.example");
      var welcome = await client.Next("connection.welcome");
      Check((int)welcome.Payload["protocolVersion"] == 3, "wire protocol");
      await client.Send("subscribe", "test-mod", "namespace.subscribe", "subscribe1");
      await client.Next("namespace.subscribed");
      Check((string)(await client.Next("namespace.changed")).Payload["status"] == "initializing", "initializing snapshot");
      ns.MarkReady();
      Check((string)(await client.Next("namespace.changed")).Payload["status"] == "ready", "ready push");
      await client.Next("state.snapshot");
      await client.Send("command", "test-mod", "level.open", "open1", new { levelId = "abc" });
      Check((await client.Next("command.accepted")).CorrelationId == "open1", "accepted correlation");
      var opened = await client.Next("level.opened");
      Check(opened.CorrelationId == "open1" && (string)opened.Payload["levelId"] == "abc", "named result correlation");
      await client.Send("command", "test-mod", "level.open", "open1");
      Check((string)(await client.Next("command.rejected")).Payload["code"] == "duplicate_command", "deduplication");
      Check(executions == 1, "duplicate repeated side effect");
      await client.Send("command", "test-mod", "denied.work", "deny1");
      await client.Next("command.accepted");
      Check((string)(await client.Next("command.rejected")).Payload["code"] == "bad_level", "domain rejection");
      await client.Send("command", "test-mod", "denied.details", "deny-details1");
      await client.Next("command.accepted");
      var detailedRejection = await client.Next("command.rejected");
      Check(detailedRejection.CorrelationId == "deny-details1" && (string)detailedRejection.Payload["details"]["field"] == "outputDirectory" && (int)detailedRejection.Payload["details"]["row"] == 2, "domain validation details and correlation");
      ns.Publish("progress.changed", new { revision = 2 });
      Check((int)(await client.Next("progress.changed")).Payload["revision"] == 2, "unsolicited push");
      await client.Send("command", "test-mod", "peer.challenge", "challenge1");
      await client.Next("command.accepted"); var challenge = await client.Next("peer.answer");
      await client.Send("event", "test-mod", "peer.answered", "answer1", new { output = 84 }, challenge.Id);
      Check((await client.Next("peer.verified")).CorrelationId == challenge.Id, "reverse peer command correlation");
      await client.Send("command", "test-mod", "level.download", "download1");
      await client.Next("command.accepted"); var ticket = await client.Next("download.ready");
      using var http = new HttpClient();
      var request = new HttpRequestMessage(HttpMethod.Get, (string)ticket.Payload["url"]); request.Headers.Add("Origin", "https://allowed.example");
      using var response = await http.SendAsync(request);
      Check(response.IsSuccessStatusCode && await response.Content.ReadAsStringAsync() == "a level file", "stream download bytes");
      Check(response.Headers.GetValues("Access-Control-Allow-Origin").Single() == "https://allowed.example", "download CORS");
      Check((await http.GetAsync((string)ticket.Payload["url"])).StatusCode == HttpStatusCode.NotFound, "single-use download");
      // Oversized output fails its command without destroying the connection.
      for (int i = 0; i < 3; i++)
      {
        string id = "large-inline-" + i;
        await client.Send("command", "test-mod", "chart.inline", id);
        await client.Next("command.accepted");
        var failed = await client.Next("command.failed");
        Check(failed.CorrelationId == id && (string)failed.Payload["code"] == "message_too_large", "oversized response failure correlation");
      }
      try { ns.Publish("chart.changed", new { levelText = largeChart }); throw new Exception("Oversized publication was accepted."); }
      catch (IpcMessageTooLargeException error) { Check(error.ByteLength > error.MaxByteLength, "publication size diagnostics"); }
      await client.Send("command", "test-mod", "chart.bad-metadata", "bad-ticket");
      await client.Next("command.accepted");
      var failedTicket = await client.Next("command.failed");
      Check(failedTicket.CorrelationId == "bad-ticket" && (string)failedTicket.Payload["code"] == "message_too_large", "oversized download metadata failure");
      Check(failedTicketSources.Single().Disposed, "unsent ticket retained its download source");
      await client.Send("command", "test-mod", "chart.stream", "large-stream");
      await client.Next("command.accepted");
      var chartTicket = await client.Next("chart.snapshot");
      Check(chartTicket.CorrelationId == "large-stream" && (int)chartTicket.Payload["metadata"]["FloorCount"] == 4896, "named download metadata and correlation");
      Check(Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(chartTicket)) < 1024, "chart bytes leaked into control message");
      var chartRequest = new HttpRequestMessage(HttpMethod.Get, (string)chartTicket.Payload["url"]); chartRequest.Headers.Add("Origin", "https://allowed.example");
      using var chartResponse = await http.SendAsync(chartRequest);
      byte[] chartBytes = await chartResponse.Content.ReadAsByteArrayAsync();
      Check(chartResponse.IsSuccessStatusCode && chartBytes.LongLength == (long)chartTicket.Payload["byteLength"] && Encoding.UTF8.GetString(chartBytes) == largeChart, "large UTF-8 chart download");
      Check(chartResponse.Headers.GetValues("Access-Control-Allow-Origin").Single() == "https://allowed.example", "named download CORS");
      Check((await http.GetAsync((string)chartTicket.Payload["url"])).StatusCode == HttpStatusCode.NotFound, "named download single-use ticket");
      await client.Send("command", "test-mod", "connection.check", "still-connected");
      await client.Next("command.accepted");
      Check((await client.Next("connection.checked")).CorrelationId == "still-connected", "large transfers disconnected other commands");
      Console.WriteLine("PASS: oversized results preserve connections; named tickets stream >6MiB UTF-8 charts with metadata and CORS.");
      using var forbidden = await Socket.Connect(server.Port, "https://forbidden.example");
      await forbidden.Next("connection.welcome"); await forbidden.Send("subscribe", "test-mod", "namespace.subscribe", "forbidden1");
      Check((string)(await forbidden.Next("command.rejected")).Payload["code"] == "origin_not_allowed", "origin authorization");
      await client.Send("subscribe", "late-mod", "namespace.subscribe", "late1"); await client.Next("namespace.subscribed");
      Check((string)(await client.Next("namespace.changed")).Payload["status"] == "unavailable", "missing namespace snapshot");
      var late = registry.RegisterNamespace("late-mod", new IpcNamespaceInfo());
      await client.Next("namespace.changed"); late.PeerSubscribed += peer => late.SendToPeer(peer.PeerId, "late.snapshot", new { }); late.MarkReady();
      await client.Next("namespace.changed"); await client.Next("late.snapshot");
      await client.Send("command", "test-mod", "queued.work", "queued1"); await client.Next("command.accepted");
      registry.UnregisterNamespace("test-mod"); await client.Next("namespace.changed");
      while (queue.TryDequeue(out var action)) action(); await Task.Delay(30);
      Check(executions == 1, "unload did not cancel queued game-thread command");
      var localNs = registry.RegisterNamespace("local-mod", new IpcNamespaceInfo());
      localNs.RegisterCommand("echo", cmd => cmd.Reply("echoed", cmd.Payload)); localNs.MarkReady();
      using var local = new IpcLocalPeer(registry, "renderer");
      var localResult = new TaskCompletionSource<IpcPeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
      local.Message += msg => { if (msg.Name == "echoed") localResult.TrySetResult(msg); };
      local.Subscribe("local-mod"); var localId = local.SendCommand("local-mod", "echo", new { value = 7 });
      Check((await localResult.Task.WaitAsync(TimeSpan.FromSeconds(3))).CorrelationId == localId, "same-process router");
      Console.WriteLine("PASS: real WebSocket lifecycle, commands/events, dedup, origins, streamed tickets, unload cancellation, local peer.");
    }
    finally { server.Stop(); }
  }

  private sealed class TrackedDownloadStream : MemoryStream
  {
    public bool Disposed;
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
  }

  static void CheckInvalidOperation(Action action, string detail)
  {
    bool thrown = false;
    try { action(); } catch (InvalidOperationException) { thrown = true; }
    Check(thrown, detail);
  }

  static void TestDisconnectedEventDelivery()
  {
    var registry = new IpcRegistry(action => action());
    var peer = new FakePeer(); peer.Info.Origin = "https://allowed.example";
    registry.Attach(peer);
    var ns = registry.RegisterNamespace("snapshot-mod", new IpcNamespaceInfo { AllowedOrigins = new[] { "https://allowed.example" } });
    ns.MarkReady();
    registry.Receive(peer, IpcPeerMessage.Create("subscribe", "snapshot-mod", "namespace.subscribe", null));
    int before = peer.Sent;
    ns.SendToPeer(peer.Info.PeerId, "state.snapshot", new { value = 1 });
    Check(peer.Sent == before + 1, "authorized event delivery");
    registry.Receive(peer, IpcPeerMessage.Create("unsubscribe", "snapshot-mod", "namespace.unsubscribe", null));
    before = peer.Sent;
    ns.SendToPeer(peer.Info.PeerId, "state.snapshot", new { value = 2 });
    Check(peer.Sent == before, "deferred snapshot after unsubscribe was delivered");
    CheckInvalidOperation(() => ns.SendCommandToPeer(peer.Info.PeerId, "work.begin", new { }), "unsubscribed reverse command silently accepted");
    registry.Receive(peer, IpcPeerMessage.Create("subscribe", "snapshot-mod", "namespace.subscribe", null));
    peer.Info.Origin = "https://forbidden.example";
    CheckInvalidOperation(() => ns.SendToPeer(peer.Info.PeerId, "state.snapshot", new { }), "unauthorized event silently accepted");
    peer.Info.Origin = "https://allowed.example";
    registry.Detach(peer);
    before = peer.Sent;
    ns.SendToPeer(peer.Info.PeerId, "state.snapshot", new { value = 3 });
    Check(peer.Sent == before, "deferred snapshot after detach was delivered");
    CheckInvalidOperation(() => ns.SendCommandToPeer(peer.Info.PeerId, "work.begin", new { }), "disconnected reverse command silently accepted");
    registry.UnregisterNamespace("snapshot-mod");
    CheckInvalidOperation(() => ns.SendToPeer(peer.Info.PeerId, "state.snapshot", new { }), "invalid namespace handle silently accepted");
    Console.WriteLine("PASS disconnected events are dropped; reverse commands, origin and stale handles remain strict");
  }

  static void TestDiscardedDispatcherQueue()
  {
    var queued = new ConcurrentQueue<Action>();
    var registry = new IpcRegistry(queued.Enqueue);
    var peer = new FakePeer(); registry.Attach(peer);
    var ns = registry.RegisterNamespace("queue-mod", new IpcNamespaceInfo());
    ns.RegisterMainThreadCommand("work", _ => throw new Exception("Cancelled queued work executed")); ns.MarkReady();
    registry.Receive(peer, IpcPeerMessage.Create("subscribe", "queue-mod", "namespace.subscribe", null));
    for (int i = 0; i < 256; i++) registry.Receive(peer, IpcPeerMessage.Create("command", "queue-mod", "work", new { }, id: "before-" + i));
    Check(peer.Accepted == 256, "initial command capacity");
    registry.UnregisterNamespace("queue-mod"); queued.Clear();
    ns = registry.RegisterNamespace("queue-mod", new IpcNamespaceInfo()); ns.RegisterMainThreadCommand("work", _ => { }); ns.MarkReady();
    for (int i = 0; i < 256; i++) registry.Receive(peer, IpcPeerMessage.Create("command", "queue-mod", "work", new { }, id: "after-" + i));
    Check(peer.Accepted == 512 && peer.Busy == 0, "discarded dispatcher actions leaked command admission slots");
    registry.Suspend(); queued.Clear(); registry.Resume();
    registry.Attach(peer);
    registry.Receive(peer, IpcPeerMessage.Create("subscribe", "queue-mod", "namespace.subscribe", null));
    for (int i = 0; i < 256; i++) registry.Receive(peer, IpcPeerMessage.Create("command", "queue-mod", "work", new { }, id: "after-stop-" + i));
    Check(peer.Accepted == 768 && peer.Busy == 0, "router stop must cancel discarded work without unregistering namespaces");
    registry.Receive(peer, IpcPeerMessage.Create("unsubscribe", "queue-mod", "namespace.unsubscribe", null)); queued.Clear();
    registry.Receive(peer, IpcPeerMessage.Create("subscribe", "queue-mod", "namespace.subscribe", null));
    for (int i = 0; i < 256; i++) registry.Receive(peer, IpcPeerMessage.Create("command", "queue-mod", "work", new { }, id: "after-unsubscribe-" + i));
    Check(peer.Accepted == 1024 && peer.Busy == 0, "unsubscribe did not cancel queued work or restore command capacity");
    registry.Suspend(); queued.Clear(); registry.Resume();
    using var local = new IpcLocalPeer(registry, "stop-test"); local.Subscribe("queue-mod");
    bool unavailable = false; local.Message += msg => { if (msg.Name == "namespace.changed" && (string)msg.Payload["status"] == "unavailable") unavailable = true; };
    registry.Suspend();
    Check(local.IsClosed && unavailable, "router stop must close and invalidate local peers");
    bool rejected = false; try { local.SendCommand("queue-mod", "work", new { }); } catch (ObjectDisposedException) { rejected = true; }
    Check(rejected, "closed local peer silently accepted a command");
    registry.UnregisterNamespace("queue-mod");
    Console.WriteLine("PASS cancelled/discarded game-thread queue releases all admission slots");
  }
  sealed class FakePeer : IIpcPeer
  {
    public IpcPeerInfo Info { get; } = new() { PeerId = "test-peer", ConnectionId = "test-connection" };
    public CancellationToken CancellationToken => CancellationToken.None;
    public int Accepted, Busy, Sent;
    public void Send(IpcPeerMessage message) { Sent++; if (message.Kind == "accepted") Accepted++; if ((string)message.Payload?["code"] == "busy") Busy++; }
    public void Close() { }
  }

  sealed class Socket : IDisposable
  {
    readonly ClientWebSocket socket = new();
    readonly ConcurrentQueue<IpcPeerMessage> messages = new();
    readonly SemaphoreSlim received = new(0);
    readonly CancellationTokenSource lifetime = new();
    public static async Task<Socket> Connect(int port, string origin)
    {
      var value = new Socket(); value.socket.Options.AddSubProtocol(IpcProtocol.Subprotocol);
      value.socket.Options.SetRequestHeader("Origin", origin);
      await value.socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port + IpcProtocol.WebSocketPath), CancellationToken.None);
      _ = value.Read(); return value;
    }
    async Task Read()
    {
      try
      {
        byte[] buffer = new byte[8192];
        while (!lifetime.IsCancellationRequested)
        {
          using var body = new MemoryStream(); WebSocketReceiveResult result;
          do { result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token); if (result.MessageType == WebSocketMessageType.Close) return; body.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
          var msg = JsonConvert.DeserializeObject<IpcPeerMessage>(Encoding.UTF8.GetString(body.ToArray()));
          if (msg.Kind == "ping") { await Send("pong", null, "connection.pong", Guid.NewGuid().ToString("N")); continue; }
          messages.Enqueue(msg); received.Release();
        }
      }
      catch (OperationCanceledException) { }
      catch (WebSocketException) { }
    }
    public async Task Send(string kind, string ns, string name, string id, object payload = null, string correlationId = null)
    {
      var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(IpcPeerMessage.Create(kind, ns, name, payload, correlationId, id)));
      await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    public async Task<IpcPeerMessage> Next(string name)
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      while (true) { await received.WaitAsync(timeout.Token); if (messages.TryDequeue(out var msg) && msg.Name == name) return msg; }
    }
    public void Dispose() { try { socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None).GetAwaiter().GetResult(); } catch { } lifetime.Cancel(); socket.Dispose(); }
  }
}
