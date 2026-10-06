using System.Threading;

namespace AdofaiIpc.Core;

internal interface IIpcPeer
{
  IpcPeerInfo Info { get; }
  CancellationToken CancellationToken { get; }
  void Send(IpcPeerMessage message);
  void Close();
}
