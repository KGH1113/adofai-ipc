using System.Collections.Concurrent;
using AdofaiIpc;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using AdofaiIpc.Runtime;
using AdofaiIpc.Server;
using Newtonsoft.Json.Linq;
using TypedTest.Generated;

internal static partial class Program
{
  static ModRegistration<TypedTestApp> CreateTypedFeature(IIpcRuntime runtime, Action<CancellationToken> observeDelay = null)
  {
    int executions = 0;
    var mod = runtime.RegisterMod(TypedTestContract.Instance, "Typed test", new[] { "http://localhost:5173", "https://allowed.example" });
    mod.Handle(TypedTestMethods.Echo, (request, context) => Task.FromResult(request.Entry));
    mod.Handle(TypedTestMethods.EchoRich, (request, context) => Task.FromResult(request.Entry));
    mod.Handle(TypedTestMethods.NoOp, (request, context) => Task.CompletedTask);
    mod.Handle(TypedTestMethods.Add, (request, context) => Task.FromResult(checked(request.Left + request.Right)));
    mod.Handle(TypedTestMethods.AppId, (request, context) => Task.FromResult(context.App.AppId));
    mod.Handle(TypedTestMethods.Ask, (request, context) => context.App.ConfirmAsync(new ConfirmRequest { Message = request.Message }));
    mod.Handle(TypedTestMethods.AppRevision, (request, context) => context.App.RevisionAsync());
    mod.Handle(TypedTestMethods.BadResult, (request, context) => Task.FromResult<string>(null));
    mod.Handle(TypedTestMethods.Increment, (request, context) => Task.FromResult(Interlocked.Increment(ref executions)));
    mod.Handle(TypedTestMethods.Executions, (request, context) => Task.FromResult(Volatile.Read(ref executions)));
    mod.Handle(TypedTestMethods.Delay, async (request, context) => {
      observeDelay?.Invoke(context.CancellationToken);
      await Task.Delay(request.Milliseconds, context.CancellationToken);
      return true;
    });
    mod.MarkReady();
    return mod;
  }

  static async Task ServeTypedExample(bool includeTest)
  {
    var registry = new IpcRegistry(action => action());
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), action => action(), () => false);
    using var typed = CreateTypedFeature(runtime);
    typed.AppConnected += app => { _ = SendTypedSnapshot(app); };
    var server = new IpcServer(registry);
    server.Start();
    Console.WriteLine("IPC_TYPED_SERVER_PORT=" + server.Port);
    Console.Out.Flush();
    try { await Task.Delay(Timeout.Infinite); }
    finally { runtime.Shutdown(); server.Stop(); }
  }

  static async Task SendTypedSnapshot(TypedTestApp app)
  {
    try { await app.UpdatedAsync(new UpdatedRequest { Entry = TypedTestMethods.EchoRich.Output.Read(RichJson) }, app.CancellationToken); }
    catch (OperationCanceledException) { }
    catch (IpcCallException error) when (error.Code == "unavailable") { }
    catch (Exception error) { Console.Error.WriteLine("Typed snapshot failed: " + error); }
  }

  static async Task TestTypedSdk()
  {
    TestTypedRichSchemas();
    await TestTypedDeferredDetach();
    await TestTypedObserverOrder();
    var registry = new IpcRegistry(action => action());
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), action => action(), () => false);
    using (var incomplete = runtime.RegisterMod(TypedTestContract.Instance, "Missing handler", new[] { "http://localhost:5173" }))
    {
      CheckInvalidOperation(incomplete.MarkReady, "MarkReady accepted missing typed handlers");
      incomplete.Handle(TypedTestMethods.Echo, (request, context) => Task.FromResult(request.Entry));
      CheckInvalidOperation(() => incomplete.Handle(TypedTestMethods.Echo, (request, context) => Task.FromResult(request.Entry)), "duplicate typed handler was accepted");
    }
    var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var mod = CreateTypedFeature(runtime, token => entered.TrySetResult(token));
    CheckInvalidOperation(() => runtime.RegisterMod(TypedTestContract.Instance, "Duplicate", new[] { "http://localhost:5173" }), "typed registration stole an existing mod");
    CheckInvalidOperation(() => mod.Handle(TypedTestMethods.Echo, (request, context) => Task.FromResult(request.Entry)), "typed handlers changed after MarkReady");
    using var peer = new TypedPeer(registry, "one", "http://localhost:5173");
    peer.Subscribe();
    int connectedEvents = 0, observerFailures = 0;
    var connectedObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    mod.AppEventFailed += error => Interlocked.Increment(ref observerFailures);
    mod.AppConnected += app => { if (app.AppId == peer.AppId) throw new InvalidOperationException("Broken app observer"); };
    mod.AppConnected += app => {
      if (app.AppId == peer.AppId) Check(peer.RegistrationAcknowledged, "AppConnected ran before its registration acknowledgement");
      Interlocked.Increment(ref connectedEvents);
      if (app.AppId == peer.AppId) connectedObserved.TrySetResult(true);
    };
    var beforeRegister = await peer.Call("rpc.mod.appid", new { });
    Check((string)beforeRegister.Payload["code"] == "app_not_registered", "unregistered typed call executed");
    var mismatch = await peer.Register(new string('0', 64));
    Check((string)mismatch.Payload["code"] == "contract_mismatch", "contract mismatch was accepted");
    var registered = await peer.Register();
    await connectedObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check((string)registered.Payload["appId"] == peer.AppId, "app UUID did not survive registration");
    Check(mod.Apps.Count == 1, "registered app snapshot lost an app");
    Check(mod.TryGetApp(peer.AppId, out var selected) && selected.SessionId == (string)registered.Payload["sessionId"], "registered app selection lost session identity");
    peer.OnCommand = message => {
      Check((string)message.Payload["sessionId"] == selected.SessionId, "callback envelope lost its registered session");
      peer.Reply(message, 17);
    };
    Check(await selected.RevisionAsync() == 17, "mod could not initiate a call before receiving an app method call");
    Check(connectedEvents == 1 && observerFailures == 1, "broken observer disrupted another app registration observer");
    var repeatedRegistration = await peer.Register();
    Check((string)repeatedRegistration.Payload["sessionId"] == selected.SessionId && connectedEvents == 1, "repeated registration created a duplicate app session");
    Check((string)(await peer.Call("rpc.mod.appid", new { })).Payload == peer.AppId, "context.App identified a different app");
    var entry = new { text = "한글 😀", enabled = true, revision = 12 };
    var echoed = await peer.Call("rpc.mod.echo", new { entry });
    Check(echoed.Name == "rpc.result" && JToken.DeepEquals(echoed.Payload, JToken.FromObject(entry)), "typed DTO casing or UTF-8 conversion changed values");
    Check((int)(await peer.Call("rpc.mod.add", new { left = int.MaxValue, right = 0 })).Payload == int.MaxValue, "int32 upper bound changed");
    foreach (var bad in new object[] {
      new { left = "1", right = 2 }, new { left = 1.5, right = 2 }, new { left = (long)int.MaxValue + 1, right = 0 },
      new { left = 1 }, new { left = 1, right = 2, extra = true }, new { Left = 1, right = 2 }
    })
      Check((string)(await peer.Call("rpc.mod.add", bad)).Payload["code"] == "invalid_request", "malformed typed input reached its handler");
    Check((string)(await peer.Call("rpc.mod.badresult", new { })).Payload["code"] == "invalid_result", "invalid typed output was returned as success");
    Check((await peer.Call("rpc.mod.noop", new { })).Payload.Type == JTokenType.Null, "void result did not use null on the wire");
    var codec = new IpcCodec<Entry>(IpcSchema.Object(IpcSchema.Field("text", IpcSchema.String), IpcSchema.Field("enabled", IpcSchema.Boolean), IpcSchema.Field("revision", IpcSchema.Int32)));
    foreach (var json in new[] { "{\"text\":\"a\",\"text\":\"b\",\"enabled\":true,\"revision\":1}", "{\"text\":null,\"enabled\":true,\"revision\":1}", "{\"text\":\"a\",\"enabled\":true,\"revision\":1} {}" })
    {
      bool invalid = false;
      try { codec.Read(json); } catch (IpcValidationException) { invalid = true; }
      Check(invalid, "strict codec accepted duplicate, null or trailing JSON");
    }
    // A local app can reply synchronously inside SendCommand. The pending call must already exist.
    peer.OnCommand = message => peer.Reply(message, true);
    Check((bool)(await peer.Call("rpc.mod.ask", new { message = "Continue?" })).Payload, "synchronous app reply was lost");
    peer.OnCommand = message => peer.Reply(message, "not an int");
    Check((string)(await peer.Call("rpc.mod.apprevision", new { })).Payload["code"] == "invalid_result", "reverse output skipped validation");
    using var other = new TypedPeer(registry, "two", "http://localhost:5173");
    other.Subscribe(); await other.Register();
    peer.OnCommand = message => {
      other.Reply(message, false);
      peer.Reply(message, true);
    };
    Check((bool)(await peer.Call("rpc.mod.ask", new { message = "Only this app" })).Payload, "another connection spoofed an app result");
    Check(mod.TryGetApp(other.AppId, out var otherApp), "second registered app was not selectable");
    int firstCalls = 0, otherCalls = 0;
    peer.OnCommand = message => { Interlocked.Increment(ref firstCalls); peer.Reply(message, true); };
    other.OnCommand = message => { Interlocked.Increment(ref otherCalls); other.Reply(message, true); };
    Check(await otherApp.ConfirmAsync(new ConfirmRequest { Message = "Target one app" }) && otherCalls == 1 && firstCalls == 0, "selected app call was broadcast or misrouted");
    var rich = TypedTestMethods.EchoRich.Output.Read(RichJson);
    other.OnCommand = message => other.Reply(message, null);
    await otherApp.UpdatedAsync(new UpdatedRequest { Entry = rich });
    other.OnCommand = null;
    using (var cancel = new CancellationTokenSource())
    {
      var pending = otherApp.ConfirmAsync(new ConfirmRequest { Message = "Cancel this call" }, cancel.Token);
      cancel.Cancel();
      bool canceled = false;
      try { await pending; } catch (OperationCanceledException) { canceled = true; }
      Check(canceled, "targeted callback ignored its cancellation token");
    }
    using (var duplicate = new TypedPeer(registry, "duplicate-app", "http://localhost:5173", other.AppId))
    {
      duplicate.Subscribe();
      Check((string)(await duplicate.Register()).Payload["code"] == "app_id_in_use", "two active connections claimed the same app UUID");
    }
    var disconnected = otherApp.ConfirmAsync(new ConfirmRequest { Message = "Disconnect this app" });
    other.Dispose();
    Check(otherApp.CancellationToken.IsCancellationRequested && !mod.TryGetApp(other.AppId, out _), "disconnected app remained selectable or uncanceled");
    try { await disconnected; Check(false, "disconnected callback succeeded"); }
    catch (IpcCallException error) { Check(error.Code == "unavailable", "disconnect callback returned an unrelated error"); }
    using (var reconnected = new TypedPeer(registry, "reconnected-app", "http://localhost:5173", other.AppId))
    {
      reconnected.Subscribe(); await reconnected.Register();
      Check(mod.TryGetApp(other.AppId, out var replacementApp) && replacementApp.SessionId != otherApp.SessionId, "reconnection reused an old opaque session");
      reconnected.OnCommand = message => reconnected.Reply(message, false);
      try { await otherApp.ConfirmAsync(new ConfirmRequest { Message = "Stale proxy" }); Check(false, "stale proxy reached a replacement app"); }
      catch (IpcCallException error) { Check(error.Code == "unavailable", "stale proxy did not fail as unavailable"); }
      Check(!await replacementApp.ConfirmAsync(new ConfirmRequest { Message = "Replacement proxy" }), "replacement app proxy did not address the new session");
    }
    using (var wrongPort = new TypedPeer(registry, "wrong-port", "http://localhost:5174"))
    {
      var response = wrongPort.Subscribe();
      Check(response.Name == "command.rejected" && (string)response.Payload["code"] == "origin_not_allowed", "exact origin policy accepted a different port");
    }
    using (var defaultPort = new TypedPeer(registry, "default-port", "http://localhost"))
      Check((string)defaultPort.Subscribe().Payload["code"] == "origin_not_allowed", "explicit port policy accepted the default port");
    using (var native = new TypedPeer(registry, "native", null))
    {
      native.Subscribe(); Check((await native.Register()).Name == "rpc.result", "origin policy incorrectly authenticated native peers");
    }
    peer.SendCommand("rpc.mod.delay", new { milliseconds = 30000 });
    var cancellation = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    mod.Dispose();
    Check(cancellation.IsCancellationRequested, "mod disposal did not cancel its running typed handler");
    using var replacement = CreateTypedFeature(runtime);
    mod.Dispose();
    Check(registry.GetNamespaceStatus("typed-test") == IpcNamespaceStatus.Ready, "stale typed disposal removed a replacement");
    runtime.Shutdown();
    Console.WriteLine("PASS typed SDK: rich schemas, omitted/null fields, enum/void conversion, selected app calls, session isolation, reconnect, origin ports and cancellation.");
  }

  const string RichJson = "{\"count\":9007199254740991,\"labels\":[\"a\",\"한글\"],\"literal\":\"fixed\",\"maybeLabel\":null,\"phase\":\"pending\",\"ratio\":1.25}";
  static void TestTypedRichSchemas()
  {
    var codec = TypedTestMethods.EchoRich.Output;
    var rich = codec.Read(RichJson);
    Check(!rich.Omitted.IsSpecified && !rich.OptionalNullable.IsSpecified && rich.MaybeLabel == null && rich.Phase == Phase.Pending && rich.Count == 9007199254740991L, "rich DTO mapping lost optional/null/enum/safe integer values");
    Check(JToken.DeepEquals(JToken.Parse(codec.Write(rich)), JToken.Parse(RichJson)), "omitted optional fields were serialized as null");
    bool valueRejected = false;
    try { _ = rich.Omitted.Value; } catch (InvalidOperationException) { valueRejected = true; }
    Check(valueRejected, "omitted optional value looked specified");
    var nullable = JObject.Parse(RichJson); nullable["optionalNullable"] = JValue.CreateNull();
    rich = codec.Read(nullable.ToString());
    Check(rich.OptionalNullable.IsSpecified && rich.OptionalNullable.Value == null && JObject.Parse(codec.Write(rich)).ContainsKey("optionalNullable"), "explicit null was collapsed into omission");
    rich.Omitted = "present"; rich.OptionalNullable = "also present";
    Check(codec.Read(codec.Write(rich)).Omitted.Value == "present", "optional string construction did not roundtrip");
    foreach (var bad in new[] {
      RichJson.Replace("9007199254740991", "9007199254740992"), RichJson.Replace("9007199254740991", "1.5"),
      RichJson.Replace("9007199254740991", "1e-300"),
      RichJson.Replace("[\"a\",\"한글\"]", "[1]"), RichJson.Replace("\"pending\"", "\"PENDING\""),
      RichJson.Replace("\"pending\"", "1"), RichJson.Replace("\"fixed\"", "\"other\""),
      RichJson.Replace("\"maybeLabel\":null,", ""), RichJson.Replace("\"ratio\":1.25", "\"ratio\":\"1.25\""),
      RichJson.Replace("\"ratio\":1.25", "\"ratio\":1e400"), RichJson.Replace("\"ratio\":1.25", "\"ratio\":1.25,\"omitted\":null")
    })
    {
      bool invalid = false;
      try { codec.Read(bad); } catch (IpcValidationException) { invalid = true; }
      Check(invalid, "rich schema accepted invalid input: " + bad);
    }
    foreach (Action<RichEntry> mutate in new Action<RichEntry>[] {
      value => value.Ratio = double.NaN, value => value.Ratio = double.PositiveInfinity,
      value => value.Count = 9007199254740992L, value => value.Phase = (Phase)123,
      value => value.Omitted = new IpcOptional<string>(null), value => value.Labels = new string[] { null }
    })
    {
      bool invalid = false; var value = codec.Read(RichJson); mutate(value);
      try { codec.Write(value); } catch (IpcValidationException) { invalid = true; }
      Check(invalid, "rich codec serialized an invalid C# value");
    }
    var voidCodec = new IpcCodec<IpcUnit>(IpcSchema.Null);
    Check(voidCodec.Write(IpcUnit.Value) == "null" && voidCodec.Read("null") == IpcUnit.Value, "void codec did not roundtrip its unit value");
    Check(TypedTestMethods.NoOp.Input.Write(IpcUnit.Value) == "{}", "empty request was serialized as void");
    var optionalCodec = new IpcCodec<OptionalNumbers>(IpcSchema.Object(IpcSchema.Field("number", IpcSchema.Optional(IpcSchema.Nullable(IpcSchema.Int32)))));
    Check(!optionalCodec.Read("{}").Number.IsSpecified && optionalCodec.Write(new OptionalNumbers()) == "{}", "optional nullable value type could not be omitted");
    var explicitNull = optionalCodec.Read("{\"number\":null}");
    Check(explicitNull.Number.IsSpecified && explicitNull.Number.Value == null && optionalCodec.Write(explicitNull) == "{\"number\":null}", "nullable value type lost explicit null");
    var zero = optionalCodec.Read("{\"number\":0}");
    Check(zero.Number.IsSpecified && zero.Number.Value == 0, "nullable value type lost zero");
  }

  sealed class OptionalNumbers { public IpcOptional<int?> Number { get; set; } }

  static async Task TestTypedDeferredDetach()
  {
    var notifications = new ConcurrentQueue<Action>();
    var registry = new IpcRegistry(action => action());
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), notifications.Enqueue, () => false);
    using var mod = CreateTypedFeature(runtime);
    using var original = new TypedPeer(registry, "same-peer", "http://localhost:5173", connectionId: "original-connection");
    original.Subscribe(); await original.Register();
    Check(mod.TryGetApp(original.AppId, out var stale), "original session was not registered");
    original.Dispose(); // The SDK detach notification intentionally remains queued on the game dispatcher.
    Check(stale.CancellationToken.IsCancellationRequested && !mod.TryGetApp(original.AppId, out _), "transport cancellation waited for the game dispatcher");
    using var replacement = new TypedPeer(registry, "same-peer", "http://localhost:5173", original.AppId, connectionId: "replacement-connection");
    replacement.Subscribe(); await replacement.Register();
    Check(mod.TryGetApp(original.AppId, out var current) && current.SessionId != stale.SessionId, "immediate same-app reconnect was rejected before game dispatch drained");
    int commands = 0;
    replacement.OnCommand = _ => Interlocked.Increment(ref commands);
    try { await stale.ConfirmAsync(new ConfirmRequest { Message = "Must never reach replacement" }); Check(false, "stale session call succeeded during a deferred detach"); }
    catch (IpcCallException error) { Check(error.Code == "unavailable", "deferred detach did not report unavailable"); }
    Check(commands == 0, "same PeerId replacement received a stale connection's callback");
    while (notifications.TryDequeue(out var notification)) notification();
    Check(mod.TryGetApp(original.AppId, out var afterDispatch) && afterDispatch.SessionId == current.SessionId, "deferred old disconnect retired the replacement session");
    runtime.Shutdown();
  }

  static async Task TestTypedObserverOrder()
  {
    var registry = new IpcRegistry(action => action());
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), action => action(), () => false);
    using var mod = CreateTypedFeature(runtime);
    using var peer = new TypedPeer(registry, "observer-order", "http://localhost:5173");
    using var release = new ManualResetEventSlim();
    var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    int order = 0;
    mod.AppConnected += app => {
      Check(peer.RegistrationAcknowledged && Interlocked.Increment(ref order) == 1, "connected observer ran before acknowledgement or after disconnection");
      entered.TrySetResult(true);
      Check(release.Wait(TimeSpan.FromSeconds(5)), "observer order test was not released");
    };
    mod.AppDisconnected += app => {
      Check(app.CancellationToken.IsCancellationRequested && Interlocked.Increment(ref order) == 2, "disconnected observer ran before connected observer finished");
      disconnected.TrySetResult(true);
    };
    peer.Subscribe(); await peer.Register();
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await Task.Run(peer.Dispose).WaitAsync(TimeSpan.FromSeconds(3));
    release.Set();
    await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    runtime.Shutdown();
  }

  sealed class TypedPeer : IIpcPeer, IDisposable
  {
    private readonly IpcRegistry _registry;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcPeerMessage>> _calls = new();
    private readonly ConcurrentQueue<IpcPeerMessage> _sent = new();
    public IpcPeerInfo Info { get; }
    public CancellationToken CancellationToken => CancellationToken.None;
    public string AppId { get; }
    public bool RegistrationAcknowledged { get; private set; }
    public Action<IpcPeerMessage> OnCommand;
    public TypedPeer(IpcRegistry registry, string id, string origin, string appId = null, string connectionId = null)
    {
      _registry = registry;
      AppId = appId ?? Guid.NewGuid().ToString();
      Info = new IpcPeerInfo { PeerId = id, ConnectionId = connectionId ?? "connection-" + id, Origin = origin };
      Check(registry.Attach(this), "typed peer attach failed");
    }
    public void Send(IRoutedMessage routed) { Send(JsonMessageProtocol.ToWire(routed)); }
  public void Send(IpcPeerMessage message)
    {
      _sent.Enqueue(message);
      if (message.Name == "rpc.result" && message.Payload is JObject registered && registered.ContainsKey("sessionId")) RegistrationAcknowledged = true;
      if (message.Kind == "command") OnCommand?.Invoke(message);
      if (message.CorrelationId != null && message.Name is "rpc.result" or "command.rejected" or "command.failed")
        if (_calls.TryRemove(message.CorrelationId, out var pending)) pending.TrySetResult(message);
    }
    public IpcPeerMessage Subscribe()
    {
      _registry.Receive(this, IpcPeerMessage.Create("subscribe", "typed-test", "namespace.subscribe", null));
      return _sent.Last();
    }
    public Task<IpcPeerMessage> Register(string hash = null) => Call("rpc.register", new { appId = AppId, displayName = "Test app", contractHash = hash ?? TypedTestContract.Instance.Hash });
    public void SendCommand(string name, object payload) =>
      _registry.Receive(this, IpcPeerMessage.Create("command", "typed-test", name, payload));
    public async Task<IpcPeerMessage> Call(string name, object payload)
    {
      string id = Guid.NewGuid().ToString("N");
      var completion = new TaskCompletionSource<IpcPeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
      _calls[id] = completion;
      _registry.Receive(this, IpcPeerMessage.Create("command", "typed-test", name, payload, id: id));
      return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public void Reply(IpcPeerMessage command, object value) =>
      _registry.Receive(this, IpcPeerMessage.Create("event", "typed-test", "rpc.result", value ?? JValue.CreateNull(), (string)command.Payload["callId"]));
    public void Close() { }
    public void Dispose() { _registry.Detach(this); }
  }
}
