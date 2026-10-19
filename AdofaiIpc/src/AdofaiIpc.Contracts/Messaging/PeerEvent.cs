using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public sealed class PeerEvent
{
  public string PeerId { get; }
  public string ConnectionId { get; }
  public string Name { get; }
  public string Id { get; }
  public string CorrelationId { get; }
  public string Json { get; }
  public PeerEvent(string peerId, string connectionId, string name, string id, string correlationId, string json)
  { PeerId = peerId; ConnectionId = connectionId; Name = name; Id = id; CorrelationId = correlationId; Json = json; }
}
