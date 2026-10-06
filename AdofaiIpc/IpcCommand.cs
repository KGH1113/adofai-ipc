using System;
using System.Threading;
using AdofaiIpc.Core;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc;

public sealed class IpcCommand
{
  private readonly IIpcPeer _peer;
  private int _rejected;
  public string Id { get; }
  public string Namespace { get; }
  public string Name { get; }
  public string PeerId => _peer.Info.PeerId;
  public string ConnectionId => _peer.Info.ConnectionId;
  public string Origin => _peer.Info.Origin;
  public JObject Payload { get; }
  public CancellationToken CancellationToken { get; }
  internal bool IsRejected => Volatile.Read(ref _rejected) != 0;

  internal IpcCommand(IIpcPeer peer, IpcPeerMessage message, CancellationToken cancellation)
  {
    _peer = peer; Id = message.Id; Namespace = message.Namespace; Name = message.Name;
    Payload = message.Payload as JObject ?? new JObject(); CancellationToken = cancellation;
  }

  public void Reply(string eventName, object payload)
  {
    if (IsRejected || CancellationToken.IsCancellationRequested) return;
    _peer.Send(IpcPeerMessage.Create("event", Namespace, eventName, payload, Id));
  }

  public void Reject(string code, string message)
  {
    if (Interlocked.Exchange(ref _rejected, 1) != 0) return;
    _peer.Send(IpcPeerMessage.Create("rejected", Namespace, "command.rejected", new { code, message }, Id));
  }

  internal void Fail(string code, string message)
  {
    if (IsRejected || CancellationToken.IsCancellationRequested) return;
    _peer.Send(IpcPeerMessage.Create("event", Namespace, "command.failed", new { code, message }, Id));
  }
}
