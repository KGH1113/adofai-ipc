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

internal sealed class FeatureRegistration : IFeatureRegistration
{
  private readonly Action<IFeatureRegistration> _release;
  private readonly Action<Action> _dispatch;
  private int _disposed;
  private Action<IClientConnection> _attached, _detached;
  private Action<PeerEvent> _event;
  internal AdofaiIpcNamespace Handle { get; }
  public string Name => Handle.Name;
  internal FeatureRegistration(Action<IFeatureRegistration> release, AdofaiIpcNamespace handle, Action<Action> dispatch)
  {
    _release = release; Handle = handle; _dispatch = dispatch;
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
      message.Name, message.Id, message.CorrelationId, RuntimeJson.Json(message.Payload))));
  }
  public void MarkInitializing() { Check(); Handle.MarkInitializing(); }
  public void MarkReady() { Check(); Handle.MarkReady(); }
  public void MarkError(string code, string message) { Check(); Handle.MarkError(code, message); }
  public void Publish(string name, string json) { Check(); Handle.Publish(name, RuntimeJson.Parse(json)); }
  public void SendToPeer(string peerId, string name, string json, string correlationId = null)
  { Check(); Handle.SendToPeer(peerId, name, RuntimeJson.Parse(json), correlationId); }
  public string SendCommandToPeer(string peerId, string name, string json)
  { Check(); return Handle.SendCommandToPeer(peerId, name, RuntimeJson.Parse(json)); }
  internal string SendToConnection(string peerId, string connectionId, string kind, string name, string json, string correlationId = null)
  { Check(); return Handle.SendToConnection(peerId, connectionId, kind, name, RuntimeJson.Parse(json), correlationId); }
  internal bool IsConnectionActive(string peerId, string connectionId) => Volatile.Read(ref _disposed) == 0 && Handle.IsConnectionActive(peerId, connectionId);
  internal CancellationToken ConnectionLifetime(string peerId, string connectionId) => Handle.ConnectionLifetime(peerId, connectionId);
  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    Handle.PeerSubscribed -= Attached; Handle.PeerDisconnected -= Detached; Handle.PeerEvent -= Event;
    _attached = null; _detached = null; _event = null;
    _release(this);
  }
}
