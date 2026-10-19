using Newtonsoft.Json.Linq;
namespace AdofaiIpc;

public sealed class IpcPeerEvent
{
  public string PeerId { get; internal set; }
  public string ConnectionId { get; internal set; }
  public string Name { get; internal set; }
  public string Id { get; internal set; }
  public string CorrelationId { get; internal set; }
  public JToken Payload { get; internal set; }
}
