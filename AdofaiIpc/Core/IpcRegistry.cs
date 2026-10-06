using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Core;

public sealed class IpcRegistry
{
  private readonly object _sync = new object();
  private bool _acceptingPeers = true;
  private readonly Dictionary<string, Entry> _namespaces = new Dictionary<string, Entry>();
  private readonly Dictionary<string, Peer> _peers = new Dictionary<string, Peer>();
  private readonly IpcDownloadTicketStore _tickets = new IpcDownloadTicketStore();
  private readonly Action<Action> _dispatchMainThread;
  private readonly SemaphoreSlim _commandSlots = new SemaphoreSlim(256, 256);
  public string DownloadBaseUrl { get; set; }

  public IpcRegistry(Action<Action> dispatchMainThread) { _dispatchMainThread = dispatchMainThread ?? throw new ArgumentNullException(nameof(dispatchMainThread)); }

  public AdofaiIpcNamespace RegisterNamespace(string name, IpcNamespaceInfo info)
  {
    if (!IpcNameValidator.IsValidNamespace(name)) throw new ArgumentException("Invalid namespace.", nameof(name));
    UnregisterNamespace(name);
    info ??= new IpcNamespaceInfo();
    if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = name;
    if (string.IsNullOrEmpty(info.Version)) info.Version = "0.0.0";
    var handle = new AdofaiIpcNamespace(this, name, info);
    Peer[] peers;
    lock (_sync) { _namespaces.Add(name, new Entry(handle)); peers = SubscribersLocked(name); }
    NotifyState(handle, peers);
    return handle;
  }

  public bool UnregisterNamespace(string name)
  {
    Entry entry; Peer[] peers;
    lock (_sync)
    {
      if (!_namespaces.TryGetValue(name, out entry)) return false;
      _namespaces.Remove(name); peers = SubscribersLocked(name);
    }
    entry.Handle.Lifetime.Cancel(); _tickets.RevokeNamespace(name);
    foreach (Peer peer in peers)
    {
      peer.Transport.Send(IpcPeerMessage.Create("namespace", name, "namespace.changed", new { status = "unavailable" }));
      Safe(() => entry.Handle.NotifyDisconnected(peer.Transport.Info));
    }
    return true;
  }

  public IpcNamespaceStatus GetNamespaceStatus(string name)
  { lock (_sync) return _namespaces.TryGetValue(name, out Entry e) ? e.Status : throw new InvalidOperationException("Namespace unavailable."); }

  internal void RegisterCommand(AdofaiIpcNamespace handle, string name, Func<IpcCommand, Task> action, bool mainThread)
  {
    if (!IpcNameValidator.IsValidMethod(name)) throw new ArgumentException("Invalid command.", nameof(name));
    if (action == null) throw new ArgumentNullException(nameof(action));
    lock (_sync) RequireCurrent(handle).Commands[name] = new Handler(action, mainThread);
  }
  internal bool UnregisterCommand(AdofaiIpcNamespace handle, string name)
  { lock (_sync) return RequireCurrent(handle).Commands.Remove(name); }

  internal void SetStatus(AdofaiIpcNamespace handle, IpcNamespaceStatus status, string code, string message)
  {
    Peer[] peers;
    lock (_sync) { Entry e = RequireCurrent(handle); e.Status = status; e.ErrorCode = code; e.ErrorMessage = message; peers = SubscribersLocked(handle.Name); }
    NotifyState(handle, peers);
    if (status == IpcNamespaceStatus.Ready)
      foreach (Peer peer in peers) if (IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) Safe(() => handle.NotifySubscribed(peer.Transport.Info));
  }

  internal bool Attach(IIpcPeer transport)
  {
    lock (_sync)
    {
      if (!_acceptingPeers || _peers.Count >= 64) return false;
      _peers.Add(transport.Info.PeerId, new Peer(transport));
    }
    transport.Send(IpcPeerMessage.Create("welcome", null, "connection.welcome", new {
      server = "AdofaiIpc", serverVersion = IpcProtocol.ProductVersion, protocolVersion = IpcProtocol.Version,
      peerId = transport.Info.PeerId, connectionId = transport.Info.ConnectionId }));
    return true;
  }

  internal void Detach(IIpcPeer transport)
  {
    AdofaiIpcNamespace[] handles; Peer peer;
    lock (_sync)
    {
      if (!_peers.TryGetValue(transport.Info.PeerId, out peer)) return;
      _peers.Remove(transport.Info.PeerId);
      handles = peer.Subscriptions.Where(name => _namespaces.ContainsKey(name)).Select(name => _namespaces[name].Handle).ToArray();
    }
    peer.Lifetime.Cancel();
    foreach (var handle in handles) Safe(() => handle.NotifyDisconnected(transport.Info));
  }

  internal void Receive(IIpcPeer transport, IpcPeerMessage message)
  {
    if (message == null) { transport.Close(); return; }
    Peer peer;
    lock (_sync) { if (!_peers.TryGetValue(transport.Info.PeerId, out peer)) return; peer.LastSeen = DateTime.UtcNow; }
    if (message.Kind == "pong") return;
    if (message.Kind == "ping") { transport.Send(IpcPeerMessage.Create("pong", null, "connection.pong", null, message.Id)); return; }
    if (message.Kind == "hello")
    { if ((int?)message.Payload?["protocolVersion"] != IpcProtocol.Version) transport.Close(); return; }
    if (!IpcNameValidator.IsValidNamespace(message.Namespace)) { Reject(transport, message, "invalid_namespace", "A valid namespace is required."); return; }
    if (message.Kind == "subscribe") { Subscribe(peer, message.Namespace, message.Id); return; }
    if (message.Kind == "unsubscribe") { Unsubscribe(peer, message.Namespace); return; }
    Entry entry;
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
      Safe(() => entry.Handle.NotifyEvent(new IpcPeerEvent { PeerId = transport.Info.PeerId, ConnectionId = transport.Info.ConnectionId,
        Name = message.Name, Id = message.Id, CorrelationId = message.CorrelationId, Payload = message.Payload ?? new JObject() }));
      return;
    }
    if (message.Kind != "command") { Reject(transport, message, "invalid_message", "Unsupported message kind."); return; }
    Handler handler;
    lock (_sync)
    {
      if (entry.Status != IpcNamespaceStatus.Ready) handler = null;
      else entry.Commands.TryGetValue(message.Name, out handler);
    }
    if (entry.Status != IpcNamespaceStatus.Ready) { Reject(transport, message, "namespace_" + StatusName(entry.Status), entry.ErrorMessage ?? "The mod is not ready."); return; }
    if (handler == null) { Reject(transport, message, "command_not_found", "The mod does not support this command."); return; }
    if (message.Payload != null && !(message.Payload is JObject)) { Reject(transport, message, "invalid_payload", "Command payload must be an object."); return; }
    lock (_sync)
    {
      // Duplicate command ids never repeat side effects on the same connection.
      if (!_commandSlots.Wait(0)) { Reject(transport, message, "busy", "The game is handling too many commands. Try again."); return; }
      if (!peer.Remember(message.Id)) { _commandSlots.Release(); Reject(transport, message, "duplicate_command", "This command was already accepted."); return; }
    }
    var linked = CancellationTokenSource.CreateLinkedTokenSource(transport.CancellationToken, peer.Lifetime.Token, subscriptionLifetime, entry.Handle.Lifetime.Token);
    var command = new IpcCommand(transport, message, linked.Token);
    // A dispatcher may discard queued actions on shutdown. Release their admission
    // slots at cancellation rather than depending on an action that never runs.
    // Running handlers retain their slot until they actually finish.
    int executionState = 0; // 0 queued, 1 running, 2 finished/cancelled
    Action cancelQueued = () => {
      if (Interlocked.CompareExchange(ref executionState, 2, 0) != 0) return;
      linked.Dispose(); _commandSlots.Release();
    };
    var cancellation = command.CancellationToken.Register(cancelQueued);
    if (Volatile.Read(ref executionState) == 2) cancellation.Dispose();
    transport.Send(IpcPeerMessage.Create("accepted", message.Namespace, "command.accepted", new { }, message.Id));
    Func<Task> execute = async () => {
      if (Interlocked.CompareExchange(ref executionState, 1, 0) != 0) return;
      try { if (!command.CancellationToken.IsCancellationRequested) await handler.Action(command).ConfigureAwait(false); }
      catch (OperationCanceledException) { }
      catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); command.Fail("handler_failed", "The mod could not complete the command."); }
      finally { Interlocked.Exchange(ref executionState, 2); cancellation.Dispose(); linked.Dispose(); _commandSlots.Release(); }
    };
    if (handler.MainThread)
    {
      try { _dispatchMainThread(() => { _ = execute(); }); }
      catch { cancelQueued(); cancellation.Dispose(); command.Fail("unavailable", "The game is shutting down."); }
    }
    else _ = Task.Run(execute);
  }

  private void Subscribe(Peer peer, string name, string correlationId)
  {
    Entry entry;
    lock (_sync)
    {
      _namespaces.TryGetValue(name, out entry);
      if (entry != null && !IsOriginAllowed(entry.Handle.Info, peer.Transport.Info.Origin))
      { Reject(peer.Transport, IpcPeerMessage.Create("subscribe", name, null, null, id: correlationId), "origin_not_allowed", "This application cannot access the namespace."); return; }
      if (peer.Subscriptions.Count >= 64 && !peer.Subscriptions.Contains(name)) return;
      if (peer.Subscriptions.Add(name)) peer.SubscriptionLifetimes.Add(name, new CancellationTokenSource());
    }
    peer.Transport.Send(IpcPeerMessage.Create("subscribed", name, "namespace.subscribed", new { }, correlationId));
    peer.Transport.Send(StateMessage(name, entry));
    if (entry?.Status == IpcNamespaceStatus.Ready) Safe(() => entry.Handle.NotifySubscribed(peer.Transport.Info));
  }

  private void Unsubscribe(Peer peer, string name)
  {
    Entry entry; CancellationTokenSource lifetime;
    lock (_sync)
    {
      if (!peer.Subscriptions.Remove(name)) return;
      lifetime = peer.SubscriptionLifetimes[name]; peer.SubscriptionLifetimes.Remove(name);
      _namespaces.TryGetValue(name, out entry);
    }
    lifetime.Cancel();
    if (entry != null) Safe(() => entry.Handle.NotifyDisconnected(peer.Transport.Info));
  }

  internal void Publish(AdofaiIpcNamespace handle, string name, object payload)
  {
    Peer[] peers; lock (_sync) { RequireCurrent(handle); peers = SubscribersLocked(handle.Name); }
    var message = IpcPeerMessage.Create("event", handle.Name, name, payload);
    foreach (Peer peer in peers) if (IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) peer.Transport.Send(message);
  }

  internal string SendToPeer(AdofaiIpcNamespace handle, string peerId, string kind, string name, object payload, string correlationId)
  {
    Peer peer;
    lock (_sync)
    {
      RequireCurrent(handle);
      if (!_peers.TryGetValue(peerId, out peer) || !peer.Subscriptions.Contains(handle.Name))
      {
        // Deferred hydration can outlive a normal disconnect or unsubscribe.
        if (kind == "event") return null;
        throw new InvalidOperationException("The peer is disconnected or not subscribed.");
      }
    }
    if (!IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) throw new InvalidOperationException("The peer cannot access this namespace.");
    var message = IpcPeerMessage.Create(kind, handle.Name, name, payload, correlationId); peer.Transport.Send(message); return message.Id;
  }

  internal void CreateDownload(AdofaiIpcNamespace handle, IpcCommand command, object result)
  {
    if (command.IsRejected || command.CancellationToken.IsCancellationRequested) { (result as IDisposable)?.Dispose(); return; }
    if (result is IpcDownloadError error) { command.Reject(error.Code, error.Message); return; }
    if (!(result is IpcDownloadSource source)) { command.Reject("download_invalid_result", "The mod could not prepare the file."); return; }
    if (string.IsNullOrEmpty(DownloadBaseUrl)) { source.Dispose(); command.Reject("unavailable", "The download server is unavailable."); return; }
    if (!_tickets.TryCreate(handle.Name, source, handle.Info.AllowedOrigins, out string token, out string code, out string reason))
    { source.Dispose(); command.Reject(code, reason); return; }
    command.Reply("download.ready", new { url = DownloadBaseUrl.TrimEnd('/') + "/ipc/download/" + token, byteLength = source.ByteLength });
  }
  internal bool TryTakeDownloadTicket(string token, string origin, out IpcDownloadSource source, out string code, out string reason)
    => _tickets.TryTake(token, origin, out source, out code, out reason);
  internal bool TryTakeDownloadTicket(string token, string origin, out IpcDownloadSource source, out string approvedBrowserOrigin, out string code, out string reason)
    => _tickets.TryTake(token, origin, out source, out approvedBrowserOrigin, out code, out reason);
  internal void ClearDownloadTickets() => _tickets.Clear();

  internal void Resume() { lock (_sync) _acceptingPeers = true; }

  internal void Suspend()
  {
    Peer[] peers;
    lock (_sync) { _acceptingPeers = false; peers = _peers.Values.ToArray(); }
    // Detach cancels routing lifetime even if a transport's asynchronous close
    // has not completed. Local peers receive unavailable before their queue ends.
    foreach (Peer peer in peers) { Detach(peer.Transport); peer.Transport.Close(); }
  }

  internal void Heartbeat()
  {
    Peer[] peers; lock (_sync) peers = _peers.Values.ToArray();
    foreach (Peer peer in peers)
    {
      if (DateTime.UtcNow - peer.LastSeen > TimeSpan.FromSeconds(45)) peer.Transport.Close();
      else peer.Transport.Send(IpcPeerMessage.Create("ping", null, "connection.ping", null));
    }
  }
  private Entry RequireCurrent(AdofaiIpcNamespace handle)
  { if (!_namespaces.TryGetValue(handle.Name, out Entry e) || !ReferenceEquals(e.Handle, handle)) throw new InvalidOperationException("Namespace has been unloaded."); return e; }
  private Peer[] SubscribersLocked(string name) => _peers.Values.Where(peer => peer.Subscriptions.Contains(name)).ToArray();
  private void NotifyState(AdofaiIpcNamespace handle, Peer[] peers)
  {
    Entry entry; lock (_sync) _namespaces.TryGetValue(handle.Name, out entry);
    var message = StateMessage(handle.Name, entry);
    foreach (var peer in peers) if (IsOriginAllowed(handle.Info, peer.Transport.Info.Origin)) peer.Transport.Send(message);
  }
  private static IpcPeerMessage StateMessage(string name, Entry e) => IpcPeerMessage.Create("namespace", name, "namespace.changed", new {
    status = e == null ? "unavailable" : StatusName(e.Status), version = e?.Handle.Info.Version,
    error = e?.ErrorCode == null ? null : new { code = e.ErrorCode, message = e.ErrorMessage } });
  private static string StatusName(IpcNamespaceStatus value) => value == IpcNamespaceStatus.Ready ? "ready" : value == IpcNamespaceStatus.Error ? "error" : "initializing";
  private static void Reject(IIpcPeer peer, IpcPeerMessage message, string code, string reason)
    => peer.Send(IpcPeerMessage.Create("rejected", message.Namespace, "command.rejected", new { code, message = reason }, message.Id));
  private static void Safe(Action action) { try { action(); } catch (Exception e) { System.Diagnostics.Trace.TraceError(e.ToString()); } }
  private static bool IsOriginAllowed(IpcNamespaceInfo info, string origin)
  {
    if (string.IsNullOrEmpty(origin) || info.AllowedOrigins == null || info.AllowedOrigins.Length == 0) return true;
    foreach (string allowed in info.AllowedOrigins)
    {
      if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase)) return true;
      if (Uri.TryCreate(allowed, UriKind.Absolute, out Uri a) && Uri.TryCreate(origin, UriKind.Absolute, out Uri o)
        && a.IsDefaultPort && a.Scheme == o.Scheme && a.Host == o.Host) return true;
    }
    return false;
  }
  private sealed class Entry
  {
    internal readonly AdofaiIpcNamespace Handle;
    internal readonly Dictionary<string, Handler> Commands = new Dictionary<string, Handler>();
    internal IpcNamespaceStatus Status = IpcNamespaceStatus.Initializing;
    internal string ErrorCode, ErrorMessage;
    internal Entry(AdofaiIpcNamespace handle) { Handle = handle; }
  }
  private sealed class Handler
  { internal readonly Func<IpcCommand, Task> Action; internal readonly bool MainThread; internal Handler(Func<IpcCommand, Task> action, bool mainThread) { Action = action; MainThread = mainThread; } }
  private sealed class Peer
  {
    internal readonly IIpcPeer Transport;
    internal readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
    internal readonly HashSet<string> Subscriptions = new HashSet<string>();
    internal readonly Dictionary<string, CancellationTokenSource> SubscriptionLifetimes = new Dictionary<string, CancellationTokenSource>();
    private readonly HashSet<string> _ids = new HashSet<string>();
    private readonly Queue<string> _order = new Queue<string>();
    internal DateTime LastSeen = DateTime.UtcNow;
    internal Peer(IIpcPeer transport) { Transport = transport; }
    internal bool Remember(string id) { if (!_ids.Add(id)) return false; _order.Enqueue(id); if (_order.Count > 1024) _ids.Remove(_order.Dequeue()); return true; }
  }
}
