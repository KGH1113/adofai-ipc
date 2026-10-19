using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Core;

internal sealed class ConnectedPeer
{
  internal readonly IIpcPeer Transport;
  internal readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
  internal readonly HashSet<string> Subscriptions = new HashSet<string>();
  internal readonly Dictionary<string, CancellationTokenSource> SubscriptionLifetimes = new Dictionary<string, CancellationTokenSource>();
  private readonly HashSet<string> _ids = new HashSet<string>();
  private readonly Queue<string> _order = new Queue<string>();
  internal DateTime LastSeen = DateTime.UtcNow;
  internal ConnectedPeer(IIpcPeer transport) { Transport = transport; }
  internal bool Remember(string id) { if (!_ids.Add(id)) return false; _order.Enqueue(id); if (_order.Count > 1024) _ids.Remove(_order.Dequeue()); return true; }
}
