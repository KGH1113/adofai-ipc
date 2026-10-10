using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Runtime;

internal sealed class RoutedRuntime : IIpcRuntime
{
  private readonly object _gate = new object();
  private readonly IpcRegistry _registry;
  private readonly Action<Action> _dispatch;
  private readonly Func<bool> _isGameThread;
  private readonly Dictionary<string, FeatureRegistration> _features = new Dictionary<string, FeatureRegistration>();
  private volatile bool _stopped;
  public RuntimeInfo Info { get; }
  public bool IsGameThread => _isGameThread();

  internal RoutedRuntime(IpcRegistry registry, RuntimeInfo info, Action<Action> dispatch, Func<bool> isGameThread)
  { _registry = registry; Info = info; _dispatch = dispatch; _isGameThread = isGameThread; }

  public void RunOnGameThread(Action action)
  {
    if (action == null) throw new ArgumentNullException(nameof(action));
    lock (_gate)
    {
      ThrowIfStopped();
      _dispatch(() => { if (!_stopped) action(); });
    }
  }

  public IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure)
  {
    if (description == null) throw new ArgumentNullException(nameof(description));
    if (configure == null) throw new ArgumentNullException(nameof(configure));
    FeatureRegistration registration;
    lock (_gate)
    {
      ThrowIfStopped();
      if (_features.ContainsKey(description.Name)) throw new InvalidOperationException("IPC feature is already registered: " + description.Name);
      var handle = _registry.RegisterNamespace(description.Name, new IpcNamespaceInfo {
        DisplayName = description.DisplayName, Version = description.Version,
        ProtocolMajor = description.ProtocolMajor, AllowedOrigins = description.AllowedOrigins });
      registration = new FeatureRegistration(this, handle, _dispatch);
      _features.Add(description.Name, registration);
    }
    var builder = new FeatureBuilder(registration);
    try { configure(builder); return registration; }
    catch { registration.Dispose(); throw; }
    finally { builder.Seal(); }
  }

  public ILocalPeer CreateLocalPeer(string name)
  {
    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A peer name is required.", nameof(name));
    lock (_gate) { ThrowIfStopped(); return new LocalPeerAdapter(new IpcLocalPeer(_registry, name)); }
  }

  internal void Release(FeatureRegistration registration)
  {
    lock (_gate)
    {
      if (_features.TryGetValue(registration.Name, out var current) && ReferenceEquals(current, registration))
      { _features.Remove(registration.Name); _registry.UnregisterNamespace(registration.Name); }
    }
  }

  internal void Shutdown()
  {
    FeatureRegistration[] registrations;
    lock (_gate)
    {
      if (_stopped) return;
      _stopped = true;
      registrations = new List<FeatureRegistration>(_features.Values).ToArray();
    }
    foreach (var registration in registrations)
    {
      try { registration.Dispose(); }
      catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
    }
  }
  private void ThrowIfStopped() { if (_stopped) throw new InvalidOperationException("The IPC process host has stopped."); }

  internal static JToken Parse(string json)
  {
    if (json == null) return new JObject();
    if (System.Text.Encoding.UTF8.GetByteCount(json) > IpcProtocol.MaxMessageBytes)
      throw new IpcMessageTooLargeException(System.Text.Encoding.UTF8.GetByteCount(json));
    using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
    JToken value = JToken.Load(reader);
    if (reader.Read()) throw new JsonReaderException("Only one JSON value is allowed.");
    return value;
  }
  internal static string Json(JToken token) => (token ?? new JObject()).ToString(Formatting.None);
}

internal sealed class FeatureBuilder : IFeatureBuilder
{
  private readonly FeatureRegistration _registration;
  private bool _sealed;
  internal FeatureBuilder(FeatureRegistration registration) { _registration = registration; }
  internal void Seal() => _sealed = true;
  private void Check() { if (_sealed) throw new InvalidOperationException("Feature configuration has finished."); _registration.Check(); }
  public void OnCommand(string name, IpcExecutionContext context, Func<IIncomingMessage, Task> handler)
  {
    Check();
    if (handler == null) throw new ArgumentNullException(nameof(handler));
    if (context != IpcExecutionContext.Background && context != IpcExecutionContext.GameThread) throw new ArgumentOutOfRangeException(nameof(context));
    Func<IpcCommand, Task> callback = command => handler(new IncomingMessage(_registration, command));
    if (context == IpcExecutionContext.GameThread)
      _registration.Handle.RegisterGameThreadAsyncCommand(name, callback);
    else _registration.Handle.RegisterAsyncCommand(name, callback);
  }
  public void OnClientAttached(Action<IClientConnection> handler) { Check(); _registration.AddAttached(handler); }
  public void OnClientDetached(Action<IClientConnection> handler) { Check(); _registration.AddDetached(handler); }
  public void OnEvent(Action<PeerEvent> handler) { Check(); _registration.AddEvent(handler); }
}

internal sealed class FeatureRegistration : IFeatureRegistration
{
  private readonly RoutedRuntime _owner;
  private readonly Action<Action> _dispatch;
  private int _disposed;
  private Action<IClientConnection> _attached, _detached;
  private Action<PeerEvent> _event;
  internal AdofaiIpcNamespace Handle { get; }
  public string Name => Handle.Name;
  internal FeatureRegistration(RoutedRuntime owner, AdofaiIpcNamespace handle, Action<Action> dispatch)
  {
    _owner = owner; Handle = handle; _dispatch = dispatch;
    handle.PeerSubscribed += Attached;
    handle.PeerDisconnected += Detached;
    handle.PeerEvent += Event;
  }
  internal void Check() { if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(Name); }
  internal void AddAttached(Action<IClientConnection> handler) => _attached += handler ?? throw new ArgumentNullException(nameof(handler));
  internal void AddDetached(Action<IClientConnection> handler) => _detached += handler ?? throw new ArgumentNullException(nameof(handler));
  internal void AddEvent(Action<PeerEvent> handler) => _event += handler ?? throw new ArgumentNullException(nameof(handler));
  private void Dispatch(Action action) => _dispatch(() => { if (Volatile.Read(ref _disposed) == 0) action(); });
  private void Attached(IpcPeerInfo peer) { if (_attached != null) Dispatch(() => _attached?.Invoke(new ClientConnection(this, peer))); }
  private void Detached(IpcPeerInfo peer) { if (_detached != null) Dispatch(() => _detached?.Invoke(new ClientConnection(this, peer))); }
  private void Event(IpcPeerEvent message)
  {
    if (_event != null) Dispatch(() => _event?.Invoke(new PeerEvent(message.PeerId, message.ConnectionId,
      message.Name, message.Id, message.CorrelationId, RoutedRuntime.Json(message.Payload))));
  }
  public void MarkInitializing() { Check(); Handle.MarkInitializing(); }
  public void MarkReady() { Check(); Handle.MarkReady(); }
  public void MarkError(string code, string message) { Check(); Handle.MarkError(code, message); }
  public void Publish(string name, string json) { Check(); Handle.Publish(name, RoutedRuntime.Parse(json)); }
  public void SendToPeer(string peerId, string name, string json, string correlationId = null)
  { Check(); Handle.SendToPeer(peerId, name, RoutedRuntime.Parse(json), correlationId); }
  public string SendCommandToPeer(string peerId, string name, string json)
  { Check(); return Handle.SendCommandToPeer(peerId, name, RoutedRuntime.Parse(json)); }
  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    Handle.PeerSubscribed -= Attached; Handle.PeerDisconnected -= Detached; Handle.PeerEvent -= Event;
    _attached = null; _detached = null; _event = null;
    _owner.Release(this);
  }
}

internal sealed class ClientConnection : IClientConnection
{
  private readonly FeatureRegistration _registration;
  private readonly IpcPeerInfo _info;
  internal ClientConnection(FeatureRegistration registration, IpcPeerInfo info) { _registration = registration; _info = info; }
  public string PeerId => _info.PeerId;
  public string ConnectionId => _info.ConnectionId;
  public string Origin => _info.Origin;
  public void Send(string name, string json, string correlationId = null) => _registration.SendToPeer(PeerId, name, json, correlationId);
  public string SendCommand(string name, string json) => _registration.SendCommandToPeer(PeerId, name, json);
}

internal sealed class IncomingMessage : IIncomingMessage
{
  private readonly FeatureRegistration _registration;
  private readonly IpcCommand _command;
  internal IncomingMessage(FeatureRegistration registration, IpcCommand command)
  {
    _registration = registration; _command = command;
    Client = new ClientConnection(registration, new IpcPeerInfo { PeerId = command.PeerId, ConnectionId = command.ConnectionId, Origin = command.Origin });
  }
  public string Id => _command.Id;
  public string Name => _command.Name;
  public string Json => RoutedRuntime.Json(_command.Payload);
  public IClientConnection Client { get; }
  public CancellationToken CancellationToken => _command.CancellationToken;
  public void Reply(string name, string json) => _command.Reply(name, RoutedRuntime.Parse(json));
  public void Reject(string code, string message) => _command.Reject(code, message);
  public void Reject(string code, string message, string detailsJson) => _command.Reject(code, message, RoutedRuntime.Parse(detailsJson));
  public void ReplyDownload(string name, Stream source, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null)
  {
    IpcDownloadSource download;
    try { download = new IpcDownloadSource(source, byteLength, fileName, contentType); }
    catch { source?.Dispose(); throw; }
    SendDownload(name, download, metadataJson);
  }
  public void ReplyDownload(string name, Action<Stream> writer, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null)
    => SendDownload(name, new IpcDownloadSource(writer, byteLength, fileName, contentType), metadataJson);
  private void SendDownload(string name, IpcDownloadSource source, string metadataJson)
  {
    try { _registration.Check(); _registration.Handle.ReplyDownload(_command, name, source, metadataJson == null ? null : RoutedRuntime.Parse(metadataJson)); }
    catch { source.Dispose(); throw; }
  }
}

internal sealed class LocalPeerAdapter : ILocalPeer
{
  private readonly IpcLocalPeer _peer;
  public bool IsClosed => _peer.IsClosed;
  public event Action<LocalMessage> Message;
  internal LocalPeerAdapter(IpcLocalPeer peer) { _peer = peer; peer.Message += Receive; }
  private void Receive(IpcPeerMessage message) => Message?.Invoke(new LocalMessage(message.Kind, message.Namespace,
    message.Name, message.Id, message.CorrelationId, RoutedRuntime.Json(message.Payload)));
  public void Subscribe(string feature) => _peer.Subscribe(feature);
  public void Unsubscribe(string feature) => _peer.Unsubscribe(feature);
  public string SendCommand(string feature, string name, string json) => _peer.SendCommand(feature, name, RoutedRuntime.Parse(json));
  public void SendEvent(string feature, string name, string json, string correlationId = null) => _peer.SendEvent(feature, name, RoutedRuntime.Parse(json), correlationId);
  public void Dispose() { _peer.Dispose(); _peer.Message -= Receive; Message = null; }
}
