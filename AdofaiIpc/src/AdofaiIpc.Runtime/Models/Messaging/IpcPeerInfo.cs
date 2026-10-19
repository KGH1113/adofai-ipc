namespace AdofaiIpc;

public sealed class IpcPeerInfo
{
  public string PeerId { get; internal set; }
  public string ConnectionId { get; internal set; }
  public string Origin { get; internal set; }
}
