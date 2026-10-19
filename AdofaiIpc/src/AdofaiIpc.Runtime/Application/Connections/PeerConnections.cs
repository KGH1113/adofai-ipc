using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace AdofaiIpc.Core;

internal sealed class PeerConnections {
  private readonly object _gate;
  private readonly IMessageProtocol _protocol;
  private readonly Dictionary<string, ConnectedPeer> _peers = new Dictionary<string, ConnectedPeer>();
  private bool _accepting = true;
  internal PeerConnections(object gate, IMessageProtocol protocol) { _gate = gate; _protocol = protocol; }
  internal bool Attach(IIpcPeer transport) {
    lock (_gate) {
      if (!_accepting || _peers.Count >= 64) return false;
      _peers.Add(transport.Info.PeerId, new ConnectedPeer(transport)); return true;
    }
  }
  internal ConnectedPeer Find(string peerId, string connectionId = null) {
    lock (_gate) return _peers.TryGetValue(peerId, out var peer) &&
      (connectionId == null || peer.Transport.Info.ConnectionId == connectionId) ? peer : null;
  }
  internal ConnectedPeer Touch(IIpcPeer transport) {
    lock (_gate) {
      var peer = Find(transport.Info.PeerId);
      if (peer == null || !ReferenceEquals(peer.Transport, transport)) return null;
      peer.LastSeen = DateTime.UtcNow; return peer;
    }
  }
  internal ConnectedPeer Detach(IIpcPeer transport) {
    lock (_gate) {
      var peer = Find(transport.Info.PeerId);
      if (peer == null || !ReferenceEquals(peer.Transport, transport)) return null;
      _peers.Remove(transport.Info.PeerId); return peer;
    }
  }
  internal bool Subscribe(ConnectedPeer peer, string name) {
    lock (_gate) {
      if (peer.Subscriptions.Count >= 64 && !peer.Subscriptions.Contains(name)) return false;
      if (peer.Subscriptions.Add(name)) peer.SubscriptionLifetimes.Add(name, new CancellationTokenSource());
      return true;
    }
  }
  internal CancellationTokenSource Unsubscribe(ConnectedPeer peer, string name) {
    lock (_gate) {
      if (!peer.Subscriptions.Remove(name)) return null;
      var lifetime = peer.SubscriptionLifetimes[name]; peer.SubscriptionLifetimes.Remove(name); return lifetime;
    }
  }
  internal ConnectedPeer[] Subscribers(string name) {
    lock (_gate) return _peers.Values.Where(peer => peer.Subscriptions.Contains(name)).ToArray();
  }
  internal void Resume() { lock (_gate) _accepting = true; }
  internal ConnectedPeer[] Suspend() { lock (_gate) { _accepting = false; return _peers.Values.ToArray(); } }
  internal void Heartbeat() {
    ConnectedPeer[] peers; lock (_gate) peers = _peers.Values.ToArray();
    foreach (var peer in peers) {
      if (DateTime.UtcNow - peer.LastSeen > TimeSpan.FromSeconds(45)) peer.Transport.Close();
      else peer.Transport.Send(_protocol.Create("ping", null, "connection.ping", null));
    }
  }
}
