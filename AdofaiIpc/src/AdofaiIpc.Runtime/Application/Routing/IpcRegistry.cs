using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Core;

public sealed partial class IpcRegistry : IDownloadTickets
{
  private readonly object _sync = new object();
  private readonly Dictionary<string, RoutingFeature> _namespaces = new Dictionary<string, RoutingFeature>();
  private readonly PeerConnections _connections;
  private readonly DownloadManager _downloads = new DownloadManager();
  private readonly CommandDispatcher _commands;
  private readonly IMessageProtocol _protocol;
  public string DownloadBaseUrl { get => _downloads.BaseUrl; set => _downloads.BaseUrl = value; }

  internal IpcRegistry(IGameThreadDispatcher dispatcher, IMessageProtocol protocol) {
    _commands = new CommandDispatcher(dispatcher);
    _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
    _connections = new PeerConnections(_sync, _protocol);
  }

  public AdofaiIpcNamespace RegisterNamespace(string name, IpcNamespaceInfo info)
  {
    if (!IpcNameValidator.IsValidNamespace(name)) throw new ArgumentException("Invalid namespace.", nameof(name));
    UnregisterNamespace(name);
    info ??= new IpcNamespaceInfo();
    if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = name;
    if (string.IsNullOrEmpty(info.Version)) info.Version = "0.0.0";
    var handle = new AdofaiIpcNamespace(this, name, info);
    ConnectedPeer[] peers;
    lock (_sync) { _namespaces.Add(name, new RoutingFeature(handle)); peers = SubscribersLocked(name); }
    NotifyState(handle, peers);
    return handle;
  }

  public bool UnregisterNamespace(string name)
  {
    RoutingFeature entry; ConnectedPeer[] peers;
    lock (_sync)
    {
      if (!_namespaces.TryGetValue(name, out entry)) return false;
      _namespaces.Remove(name); peers = SubscribersLocked(name);
    }
    entry.Handle.Lifetime.Cancel(); _downloads.RevokeNamespace(name);
    foreach (ConnectedPeer peer in peers)
    {
      peer.Transport.Send(_protocol.Create("namespace", name, "namespace.changed", new { status = "unavailable" }));
      Safe(() => entry.Handle.NotifyDisconnected(peer.Transport.Info));
    }
    return true;
  }

  public IpcNamespaceStatus GetNamespaceStatus(string name)
  { lock (_sync) return _namespaces.TryGetValue(name, out RoutingFeature e) ? e.Status : throw new InvalidOperationException("Namespace unavailable."); }

  internal void RegisterCommand(AdofaiIpcNamespace handle, string name, Func<IpcCommand, Task> action, bool mainThread)
  {
    if (!IpcNameValidator.IsValidMethod(name)) throw new ArgumentException("Invalid command.", nameof(name));
    if (action == null) throw new ArgumentNullException(nameof(action));
    lock (_sync) RequireCurrent(handle).Commands[name] = new CommandHandler(action, mainThread);
  }
  internal bool UnregisterCommand(AdofaiIpcNamespace handle, string name)
  { lock (_sync) return RequireCurrent(handle).Commands.Remove(name); }

  internal void SetStatus(AdofaiIpcNamespace handle, IpcNamespaceStatus status, string code, string message)
  {
    ConnectedPeer[] peers;
    lock (_sync) { RoutingFeature e = RequireCurrent(handle); e.Status = status; e.ErrorCode = code; e.ErrorMessage = message; peers = SubscribersLocked(handle.Name); }
    NotifyState(handle, peers);
    if (status == IpcNamespaceStatus.Ready)
      foreach (ConnectedPeer peer in peers) if (IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) Safe(() => handle.NotifySubscribed(peer.Transport.Info));
  }

  internal bool Attach(IIpcPeer transport)
  {
    if (!_connections.Attach(transport)) return false;
    transport.Send(_protocol.Create("welcome", null, "connection.welcome", new {
      server = "AdofaiIpc", serverVersion = IpcProtocol.ProductVersion, protocolVersion = IpcProtocol.Version,
      peerId = transport.Info.PeerId, connectionId = transport.Info.ConnectionId }));
    return true;
  }

  internal void Detach(IIpcPeer transport)
  {
    AdofaiIpcNamespace[] handles; ConnectedPeer peer;
    lock (_sync)
    {
      peer = _connections.Detach(transport);
      if (peer == null) return;
      handles = peer.Subscriptions.Where(name => _namespaces.ContainsKey(name)).Select(name => _namespaces[name].Handle).ToArray();
    }
    peer.Lifetime.Cancel();
    foreach (var handle in handles) Safe(() => handle.NotifyDisconnected(transport.Info));
  }

  internal void Receive(IIpcPeer transport, IRoutedMessage message)
  {
    if (message == null) { transport.Close(); return; }
    ConnectedPeer peer;
    peer = _connections.Touch(transport);
    if (peer == null) return;
    if (message.Kind == "pong") return;
    if (message.Kind == "ping") { transport.Send(_protocol.Create("pong", null, "connection.pong", null, message.Id)); return; }
    if (message.Kind == "hello")
    { if (message.Payload?.Int32Property("protocolVersion") != IpcProtocol.Version) transport.Close(); return; }
    if (!IpcNameValidator.IsValidNamespace(message.Namespace)) { Reject(transport, message, "invalid_namespace", "A valid namespace is required."); return; }
    if (message.Kind == "subscribe") { Subscribe(peer, message.Namespace, message.Id); return; }
    if (message.Kind == "unsubscribe") { Unsubscribe(peer, message.Namespace); return; }
    RoutingFeature entry;
    CancellationToken subscriptionLifetime = default;
    lock (_sync)
    {
      _namespaces.TryGetValue(message.Namespace, out entry);
      if (!peer.Subscriptions.Contains(message.Namespace)) entry = null;
      else subscriptionLifetime = peer.SubscriptionLifetimes[message.Namespace].Token;
    }
    if (entry == null) { Reject(transport, message, "namespace_unavailable", "Subscribe to an available namespace first."); return; }
    if (!IsOriginAllowed(entry.Handle.Info, transport.Info.Origin)) { Reject(transport, message, "origin_not_allowed", "This application cannot access the namespace."); return; }
    if (!IpcNameValidator.IsValidMethod(message.Name) || string.IsNullOrEmpty(message.Id) || message.Id.Length > 128)
    { Reject(transport, message, "invalid_message", "A valid message name and id are required."); return; }
    if (message.Kind == "event")
    {
      Safe(() => entry.Handle.NotifyEvent(_protocol.Event(transport.Info, message)));
      return;
    }
    if (message.Kind != "command") { Reject(transport, message, "invalid_message", "Unsupported message kind."); return; }
    CommandHandler handler;
    lock (_sync)
    {
      if (entry.Status != IpcNamespaceStatus.Ready) handler = null;
      else entry.Commands.TryGetValue(message.Name, out handler);
    }
    if (entry.Status != IpcNamespaceStatus.Ready) { Reject(transport, message, "namespace_" + StatusName(entry.Status), entry.ErrorMessage ?? "The mod is not ready."); return; }
    if (handler == null) { Reject(transport, message, "command_not_found", "The mod does not support this command."); return; }
    if (message.Payload != null && !message.Payload.IsObject) { Reject(transport, message, "invalid_payload", "Command payload must be an object."); return; }
    lock (_sync)
    {
      // Duplicate command ids never repeat side effects on the same connection.
      if (!_commands.TryReserve()) { Reject(transport, message, "busy", "The game is handling too many commands. Try again."); return; }
      if (!peer.Remember(message.Id)) { _commands.Release(); Reject(transport, message, "duplicate_command", "This command was already accepted."); return; }
    }
    var linked = CancellationTokenSource.CreateLinkedTokenSource(transport.CancellationToken, peer.Lifetime.Token, subscriptionLifetime, entry.Handle.Lifetime.Token);
    var command = new IpcCommand(transport, message, linked.Token);
    _commands.Execute(command, handler.Action, handler.MainThread, linked,
      () => transport.Send(_protocol.Create("accepted", message.Namespace, "command.accepted", new { }, message.Id)));
  }

  private void Subscribe(ConnectedPeer peer, string name, string correlationId)
  {
    RoutingFeature entry;
    lock (_sync)
    {
      _namespaces.TryGetValue(name, out entry);
      if (entry != null && !IsOriginAllowed(entry.Handle.Info, peer.Transport.Info.Origin))
      { Reject(peer.Transport, _protocol.Create("subscribe", name, null, null, id: correlationId), "origin_not_allowed", "This application cannot access the namespace."); return; }
      if (!_connections.Subscribe(peer, name)) return;
    }
    peer.Transport.Send(_protocol.Create("subscribed", name, "namespace.subscribed", new { }, correlationId));
    peer.Transport.Send(StateMessage(name, entry));
    if (entry?.Status == IpcNamespaceStatus.Ready) Safe(() => entry.Handle.NotifySubscribed(peer.Transport.Info));
  }

  private void Unsubscribe(ConnectedPeer peer, string name)
  {
    RoutingFeature entry; CancellationTokenSource lifetime;
    lock (_sync)
    {
      lifetime = _connections.Unsubscribe(peer, name);
      if (lifetime == null) return;
      _namespaces.TryGetValue(name, out entry);
    }
    lifetime.Cancel();
    if (entry != null) Safe(() => entry.Handle.NotifyDisconnected(peer.Transport.Info));
  }

  internal void Publish(AdofaiIpcNamespace handle, string name, object payload)
  {
    ConnectedPeer[] peers; lock (_sync) { RequireCurrent(handle); peers = SubscribersLocked(handle.Name); }
    var message = _protocol.Create("event", handle.Name, name, payload);
    foreach (ConnectedPeer peer in peers) if (IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) peer.Transport.Send(message);
  }

  internal bool IsConnectionActive(AdofaiIpcNamespace handle, string peerId, string connectionId)
  {
    lock (_sync) { var peer = _connections.Find(peerId, connectionId);
      return _namespaces.TryGetValue(handle.Name, out var entry) && ReferenceEquals(entry.Handle, handle) && peer != null && peer.Subscriptions.Contains(handle.Name); }
  }

  internal CancellationToken ConnectionLifetime(string peerId, string connectionId)
  {
    var peer = _connections.Find(peerId, connectionId);
    return peer?.Lifetime.Token ?? new CancellationToken(true);
  }

  internal string SendToPeer(AdofaiIpcNamespace handle, string peerId, string kind, string name, object payload, string correlationId, string expectedConnectionId = null)
  {
    ConnectedPeer peer;
    lock (_sync)
    {
      RequireCurrent(handle);
      peer = _connections.Find(peerId, expectedConnectionId);
      if (peer == null || !peer.Subscriptions.Contains(handle.Name))
      {
        // Deferred hydration can outlive a normal disconnect or unsubscribe.
        if (kind == "event") return null;
        throw new InvalidOperationException("The peer is disconnected or not subscribed.");
      }
    }
    if (!IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) throw new InvalidOperationException("The peer cannot access this namespace.");
    var message = _protocol.Create(kind, handle.Name, name, payload, correlationId); peer.Transport.Send(message); return message.Id;
  }

  internal void CreateDownload(AdofaiIpcNamespace handle, IpcCommand command, object result, string eventName = "download.ready", object metadata = null)
    => _downloads.Reply(handle, command, result, eventName, metadata);
  internal AdofaiIpc.Contracts.IpcFileTicket ExportFile(AdofaiIpcNamespace handle, IpcDownloadSource source,
    CancellationToken sessionCancellation, CancellationToken callCancellation)
    => _downloads.Export(handle, source, sessionCancellation, callCancellation);
  internal bool TryTakeDownloadTicket(string token, string origin, out IpcDownloadSource source, out string code, out string reason)
    => _downloads.TryTake(token, origin, out source, out code, out reason);
  internal bool TryTakeDownloadTicket(string token, string origin, out IpcDownloadSource source, out string approvedBrowserOrigin, out string code, out string reason)
    => _downloads.TryTake(token, origin, out source, out approvedBrowserOrigin, out code, out reason);
  bool IDownloadTickets.TryTake(string token, string origin, out IpcDownloadSource source, out string approvedBrowserOrigin, out string code, out string reason)
    => _downloads.TryTake(token, origin, out source, out approvedBrowserOrigin, out code, out reason);
  internal void ClearDownloadTickets() => _downloads.Clear();

  internal void Resume() => _connections.Resume();

  internal void Suspend()
  {
    var peers = _connections.Suspend();
    // Detach cancels routing lifetime even if a transport's asynchronous close
    // has not completed. Local peers receive unavailable before their queue ends.
    foreach (ConnectedPeer peer in peers) { Detach(peer.Transport); peer.Transport.Close(); }
  }

  internal void Heartbeat() => _connections.Heartbeat();
  private RoutingFeature RequireCurrent(AdofaiIpcNamespace handle)
  { if (!_namespaces.TryGetValue(handle.Name, out RoutingFeature e) || !ReferenceEquals(e.Handle, handle)) throw new InvalidOperationException("Namespace has been unloaded."); return e; }
  private ConnectedPeer[] SubscribersLocked(string name) => _connections.Subscribers(name);
  private void NotifyState(AdofaiIpcNamespace handle, ConnectedPeer[] peers)
  {
    RoutingFeature entry; lock (_sync) _namespaces.TryGetValue(handle.Name, out entry);
    var message = StateMessage(handle.Name, entry);
    foreach (var peer in peers) if (IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) peer.Transport.Send(message);
  }
  private IRoutedMessage StateMessage(string name, RoutingFeature e) => _protocol.Create("namespace", name, "namespace.changed", new {
    status = e == null ? "unavailable" : StatusName(e.Status), version = e?.Handle.Info.Version,
    featureProtocolMajor = e?.Handle.Info.ProtocolMajor,
    error = e?.ErrorCode == null ? null : new { code = e.ErrorCode, message = e.ErrorMessage } });
  private static string StatusName(IpcNamespaceStatus value) => value == IpcNamespaceStatus.Ready ? "ready" : value == IpcNamespaceStatus.Error ? "error" : "initializing";
  private void Reject(IIpcPeer peer, IRoutedMessage message, string code, string reason)
    => peer.Send(_protocol.Create("rejected", message.Namespace, "command.rejected", new { code, message = reason }, message.Id));
  private static void Safe(Action action) { try { action(); } catch (Exception e) { System.Diagnostics.Trace.TraceError(e.ToString()); } }
  private static bool IsOriginAllowed(IpcNamespaceInfo info, string origin)
  {
    if (string.IsNullOrEmpty(origin) || info.AllowedOrigins == null || info.AllowedOrigins.Length == 0) return true;
    foreach (string allowed in info.AllowedOrigins)
    {
      if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase)) return true;
      if (Uri.TryCreate(allowed, UriKind.Absolute, out Uri a) && Uri.TryCreate(origin, UriKind.Absolute, out Uri o)
        && a.Scheme == o.Scheme && a.Host.Equals(o.Host, StringComparison.OrdinalIgnoreCase)
        && (info.ExactOrigins ? a.Port == o.Port : a.IsDefaultPort)) return true;
    }
    return false;
  }
}
