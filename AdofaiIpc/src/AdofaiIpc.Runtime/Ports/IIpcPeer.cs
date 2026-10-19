using System.Threading;

namespace AdofaiIpc.Core;

internal interface IIpcPeer
{
  IpcPeerInfo Info { get; }
  CancellationToken CancellationToken { get; }
  void Send(IRoutedMessage message);
  void Close();
}
