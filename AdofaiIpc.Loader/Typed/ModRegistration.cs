using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc;

public sealed class ModRegistration<TApp> : IDisposable where TApp : IpcApp
{
  private readonly object _gate = new object();
  private readonly ModContract<TApp> _contract;
  private readonly IFeatureRegistration _registration;
  private readonly Dictionary<IpcMethod, Func<IIncomingMessage, Task>> _handlers = new Dictionary<IpcMethod, Func<IIncomingMessage, Task>>();
  private readonly Dictionary<string, AppIdentity> _apps = new Dictionary<string, AppIdentity>(StringComparer.Ordinal);
  private readonly Dictionary<string, AppIdentity> _appIds = new Dictionary<string, AppIdentity>(StringComparer.Ordinal);
  private readonly Dictionary<string, PendingCall> _pending = new Dictionary<string, PendingCall>(StringComparer.Ordinal);
  private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
  private readonly CancellationToken _lifetimeToken;
  private readonly string[] _origins;
  private bool _ready, _disposed;
  private const int MaxPending = 256;

  /// <summary>Raised asynchronously after a successful registration response. Observers run in session order.</summary>
  public event Action<TApp> AppConnected;
  public event Action<TApp> AppDisconnected;
  /// <summary>An observer failed; other observers still run and the registration remains valid.</summary>
  public event Action<Exception> AppEventFailed;

  /// <summary>A snapshot of registered apps. A snapshot proxy never targets a later connection.</summary>
  public IReadOnlyList<TApp> Apps
  {
    get
    {
      lock (_gate)
      {
        Check();
        var apps = new TApp[_apps.Count]; int index = 0;
        foreach (var identity in _apps.Values) apps[index++] = identity.App;
        return apps;
      }
    }
  }
  public bool TryGetApp(string appId, out TApp app)
  {
    lock (_gate)
    {
      Check();
      if (appId != null && _appIds.TryGetValue(appId, out var identity)) { app = identity.App; return true; }
      app = null; return false;
    }
  }

  internal ModRegistration(IIpcRuntime runtime, ModContract<TApp> contract, string displayName, string[] allowedOrigins)
  {
    if (runtime == null) throw new ArgumentNullException(nameof(runtime));
    _contract = contract ?? throw new ArgumentNullException(nameof(contract));
    _lifetimeToken = _lifetime.Token;
    if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200) throw new ArgumentException("A display name of 1–200 characters is required.", nameof(displayName));
    if (allowedOrigins == null || allowedOrigins.Length == 0) throw new ArgumentException("Specify the allowed browser origins explicitly.", nameof(allowedOrigins));
    _origins = (string[])allowedOrigins.Clone();
    foreach (var origin in _origins)
      if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") ||
        uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        throw new ArgumentException("Use HTTP(S) origins without paths, credentials, queries or fragments.", nameof(allowedOrigins));
    _registration = runtime.RegisterFeature(new FeatureDescription(contract.Id, "2.0.0", displayName: displayName, allowedOrigins: _origins) { ExactOrigins = true }, builder => {
      builder.OnCommand("rpc.register", IpcExecutionContext.Background, RegisterApp);
      foreach (var method in contract.Methods)
      {
        var definition = method;
        builder.OnCommand(definition.WireName, IpcExecutionContext.Background, message => Dispatch(definition, message));
      }
      builder.OnEvent(Receive);
      builder.OnClientDetached(client => Detach(client.ConnectionId));
    });
  }

  public void Handle<TRequest, TResult>(IpcMethod<TRequest, TResult> method,
    Func<TRequest, ModCallContext<TApp>, Task<TResult>> handler)
  {
    if (handler == null) throw new ArgumentNullException(nameof(handler));
    lock (_gate)
    {
      Check();
      if (_ready) throw new InvalidOperationException("Install handlers before MarkReady().");
      if (Array.IndexOf(_contract.Methods, method) < 0) throw new ArgumentException("This method belongs to a different contract.", nameof(method));
      if (_handlers.ContainsKey(method)) throw new InvalidOperationException("A handler is already registered for " + method.Name);
      _handlers.Add(method, async message => {
        TRequest request;
        try { request = method.Input.Read(message.Json); }
        catch (IpcValidationException error) { message.Reject("invalid_request", error.Message); return; }
        AppIdentity identity;
        lock (_gate) { if (!_apps.TryGetValue(message.Client.ConnectionId, out identity)) { message.Reject("app_not_registered", "Register the app before calling methods."); return; } }
        TResult result;
        try { result = await handler(request, new ModCallContext<TApp>(identity.App, message.CancellationToken)).ConfigureAwait(false); }
        catch (IpcCallException error) { message.Reject(error.Code, error.Message); return; }
        catch (IpcValidationException error) { message.Reject("invalid_result", error.Message); return; }
        message.CancellationToken.ThrowIfCancellationRequested();
        string json;
        try { json = method.Output.Write(result); }
        catch (IpcValidationException error) { message.Reject("invalid_result", error.Message); return; }
        message.Reply("rpc.result", json);
      });
    }
  }

  public void Handle<TRequest>(IpcMethod<TRequest, IpcUnit> method, Func<TRequest, ModCallContext<TApp>, Task> handler)
  {
    if (handler == null) throw new ArgumentNullException(nameof(handler));
    Handle<TRequest, IpcUnit>(method, async (request, context) => { await handler(request, context).ConfigureAwait(false); return IpcUnit.Value; });
  }

  public void MarkReady()
  {
    lock (_gate)
    {
      Check();
      foreach (var method in _contract.Methods)
        if (!_handlers.ContainsKey(method)) throw new InvalidOperationException("Missing mod handler: " + method.Name);
      _ready = true; _registration.MarkReady();
    }
  }

  public void MarkError(string code, string message)
  {
    lock (_gate) { Check(); _registration.MarkError(code, message); }
  }

  private Task Dispatch(IpcMethod method, IIncomingMessage message)
  {
    Func<IIncomingMessage, Task> handler;
    lock (_gate) { Check(); handler = _handlers[method]; }
    if (!OriginAllowed(message.Client.Origin)) { message.Reject("origin_not_allowed", "The app origin is not allowed."); return Task.CompletedTask; }
    return handler(message);
  }

  private Task RegisterApp(IIncomingMessage message)
  {
    AppIdentity connected = null;
    AppIdentity stale = null;
    bool acknowledged = false;
    try
    {
      if (!OriginAllowed(message.Client.Origin)) throw new IpcCallException("origin_not_allowed", "The app origin is not allowed.");
      var schema = IpcSchema.Object(IpcSchema.Field("appId", IpcSchema.String), IpcSchema.Field("displayName", IpcSchema.String), IpcSchema.Field("contractHash", IpcSchema.String));
      var value = IpcJson.Parse(message.Json); schema.Validate(value);
      string id = (string)value["appId"], name = (string)value["displayName"];
      if (!Guid.TryParse(id, out _) || string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new IpcValidationException("An app UUID and a display name of 1–200 characters are required.");
      if ((string)value["contractHash"] != _contract.Hash) throw new IpcCallException("contract_mismatch", "The mod and app were generated from different IPC contracts.");
      AppIdentity identity;
      lock (_gate)
      {
        Check();
        message.CancellationToken.ThrowIfCancellationRequested();
        if (_apps.TryGetValue(message.Client.ConnectionId, out identity))
        {
          if (identity.Id != id || identity.DisplayName != name) throw new IpcCallException("already_registered", "This connection already registered a different app identity.");
        }
        else
        {
          if (_appIds.TryGetValue(id, out var duplicate))
          {
            if (!(duplicate.Client is IClientConnectionState previous) || previous.IsConnected)
              throw new IpcCallException("app_id_in_use", "This app UUID is already registered by another connection.");
            stale = RemoveIdentity(duplicate.Client.ConnectionId);
          }
          identity = new AppIdentity(id, name, message.Client, _lifetimeToken);
          try { identity.App = _contract.AppFactory(new AppInvoker(this, identity)); }
          catch { identity.Dispose(); throw; }
          identity.EnqueueObserver(() => Notify(AppConnected, identity.App));
          _apps.Add(message.Client.ConnectionId, identity); _appIds.Add(id, identity); connected = identity;
        }
      }
      if (connected != null && message.Client is IClientConnectionState state)
        connected.AttachDisconnect(state.CancellationToken, () => Detach(message.Client.ConnectionId, connected));
      message.Reply("rpc.result", new JObject { ["appId"] = id, ["sessionId"] = identity.SessionId }.ToString(Formatting.None));
      acknowledged = true;
    }
    catch (IpcCallException error) { message.Reject(error.Code, error.Message); }
    catch (Exception error) when (error is JsonException || error is IpcValidationException) { message.Reject("invalid_request", error.Message); }
    finally { if (stale != null) FinishDetach(stale); connected?.ReleaseObservers(acknowledged); }
    return Task.CompletedTask;
  }

  private bool OriginAllowed(string origin)
  {
    // Origin is a browser policy, not authentication for native peers.
    if (string.IsNullOrEmpty(origin)) return true;
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var source)) return false;
    foreach (var allowed in _origins)
    {
      var target = new Uri(allowed);
      if (source.Scheme == target.Scheme && source.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) && source.Port == target.Port) return true;
    }
    return false;
  }

  private void Receive(PeerEvent message)
  {
    if (message.CorrelationId == null || (message.Name != "rpc.result" && message.Name != "rpc.error")) return;
    PendingCall pending;
    lock (_gate)
    {
      if (!_pending.TryGetValue(message.CorrelationId, out pending) || pending.ConnectionId != message.ConnectionId) return;
    }
    if (message.Name == "rpc.result") pending.Completion.TrySetResult(message.Json);
    else
    {
      try
      {
        var error = IpcJson.Parse(message.Json);
        pending.Completion.TrySetException(new IpcCallException((string)error["code"] ?? "handler_failed", (string)error["message"] ?? "App method failed."));
      }
      catch (Exception error) { pending.Completion.TrySetException(new IpcCallException("invalid_response", error.Message)); }
    }
  }

  private async Task<TResult> InvokeApp<TRequest, TResult>(AppIdentity identity, IpcMethod<TRequest, TResult> method, TRequest request, CancellationToken cancellationToken)
  {
    string args = method.Input.Write(request), id = Guid.NewGuid().ToString("N");
    var pending = new PendingCall(identity.Client.ConnectionId);
    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(identity.Cancellation, cancellationToken);
    lifetime.CancelAfter(TimeSpan.FromSeconds(30));
    using var cancellation = lifetime.Token.Register(() => pending.Completion.TrySetCanceled());
    lock (_gate)
    {
      Check();
      if (!_apps.TryGetValue(identity.Client.ConnectionId, out var active) || !ReferenceEquals(active, identity))
        throw new IpcCallException("unavailable", "The app session disconnected.");
      if (_pending.Count >= MaxPending) throw new IpcCallException("busy", "Too many pending app calls.");
      _pending.Add(id, pending);
    }
    try
    {
      lifetime.Token.ThrowIfCancellationRequested();
      try { identity.Client.SendCommand(method.WireName, new JObject { ["callId"] = id, ["sessionId"] = identity.SessionId, ["args"] = IpcJson.Parse(args) }.ToString(Formatting.None)); }
      catch (InvalidOperationException) { throw new IpcCallException("unavailable", "The app session disconnected."); }
      return method.Output.Read(await pending.Completion.Task.ConfigureAwait(false));
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !identity.Cancellation.IsCancellationRequested)
    { throw new IpcCallException("timeout", "The app call timed out. Its side effects may already have occurred."); }
    finally { lock (_gate) _pending.Remove(id); }
  }

  // Caller holds _gate. Cancellation and observers run only after releasing it.
  private AppIdentity RemoveIdentity(string connectionId, AppIdentity expected = null)
  {
    if (!_apps.TryGetValue(connectionId, out var identity) || (expected != null && !ReferenceEquals(identity, expected))) return null;
    _apps.Remove(connectionId); _appIds.Remove(identity.Id);
    foreach (var pending in _pending.Values)
      if (pending.ConnectionId == connectionId) pending.Completion.TrySetException(new IpcCallException("unavailable", "The app disconnected."));
    return identity;
  }
  private void Detach(string connectionId, AppIdentity expected = null)
  {
    AppIdentity identity;
    lock (_gate) identity = RemoveIdentity(connectionId, expected);
    if (identity != null) FinishDetach(identity);
  }
  private void FinishDetach(AppIdentity identity)
  {
    try { identity.Dispose(); }
    catch (Exception error) { ReportEventFailure(error); }
    finally { identity.EnqueueObserver(() => Notify(AppDisconnected, identity.App)); }
  }

  private void Notify(Action<TApp> observers, TApp app)
  {
    if (observers == null) return;
    foreach (Action<TApp> observer in observers.GetInvocationList())
      try { observer(app); }
      catch (Exception error) { ReportEventFailure(error); }
  }
  private void ReportEventFailure(Exception error)
  {
    var failures = AppEventFailed;
    if (failures == null) Trace.TraceError("IPC app observer failed: {0}", error);
    else foreach (Action<Exception> failure in failures.GetInvocationList())
      try { failure(error); } catch (Exception reportingError) { Trace.TraceError("IPC error observer failed: {0}", reportingError); }
  }

  private void Check() { if (_disposed) throw new ObjectDisposedException(_contract.Id); }
  public void Dispose()
  {
    AppIdentity[] identities;
    lock (_gate)
    {
      if (_disposed) return;
      _disposed = true;
      identities = new AppIdentity[_apps.Count]; _apps.Values.CopyTo(identities, 0);
      foreach (var pending in _pending.Values) pending.Completion.TrySetCanceled();
      _pending.Clear(); _apps.Clear(); _appIds.Clear();
    }
    foreach (var identity in identities) FinishDetach(identity);
    _lifetime.Cancel(); _registration.Dispose(); _lifetime.Dispose();
  }

  private sealed class AppIdentity : IDisposable
  {
    internal readonly string Id, DisplayName, SessionId = Guid.NewGuid().ToString("N");
    internal readonly IClientConnection Client;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _observerGate = new object();
    private readonly object _lifetimeGate = new object();
    private CancellationTokenRegistration _disconnect;
    private bool _disposed;
    private readonly TaskCompletionSource<bool> _observersReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _observerTail;
    internal readonly CancellationToken Cancellation;
    internal TApp App;
    internal AppIdentity(string id, string displayName, IClientConnection client, CancellationToken modLifetime)
    {
      Id = id; DisplayName = displayName; Client = client; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(modLifetime);
      Cancellation = _lifetime.Token; _observerTail = _observersReady.Task;
    }
    internal void ReleaseObservers(bool acknowledged) => _observersReady.TrySetResult(acknowledged);
    internal void AttachDisconnect(CancellationToken cancellationToken, Action disconnected)
    {
      // Register may synchronously invoke the callback when the transport already closed.
      // Never hold the lifetime lock while disposing a registration that may be running.
      var registration = cancellationToken.Register(disconnected);
      bool dispose;
      lock (_lifetimeGate)
      {
        dispose = _disposed;
        if (!dispose) _disconnect = registration;
      }
      if (dispose) registration.Dispose();
    }
    internal void EnqueueObserver(Action callback)
    {
      lock (_observerGate)
        _observerTail = _observerTail.ContinueWith(_ => {
          if (_observersReady.Task.GetAwaiter().GetResult()) callback();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
    public void Dispose()
    {
      CancellationTokenRegistration disconnect;
      lock (_lifetimeGate)
      {
        if (_disposed) return;
        _disposed = true; disconnect = _disconnect;
      }
      disconnect.Dispose();
      try { _lifetime.Cancel(); } finally { _lifetime.Dispose(); }
    }
  }
  private sealed class PendingCall
  {
    internal readonly string ConnectionId;
    internal readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal PendingCall(string connectionId) { ConnectionId = connectionId; }
  }
  private sealed class AppInvoker : IpcAppInvoker
  {
    private readonly ModRegistration<TApp> _owner;
    private readonly AppIdentity _identity;
    internal AppInvoker(ModRegistration<TApp> owner, AppIdentity identity) { _owner = owner; _identity = identity; }
    public override string AppId => _identity.Id;
    public override string DisplayName => _identity.DisplayName;
    public override string SessionId => _identity.SessionId;
    public override CancellationToken CancellationToken => _identity.Cancellation;
    public override Task<TResult> InvokeAsync<TRequest, TResult>(IpcMethod<TRequest, TResult> method, TRequest request, CancellationToken cancellationToken = default)
      => _owner.InvokeApp(_identity, method, request, cancellationToken);
  }
}
