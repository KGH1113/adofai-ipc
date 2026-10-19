using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
namespace AdofaiIpc.Runtime;
internal sealed class LocalPeerFactory : ILocalPeerFactory {
  private readonly IpcRegistry _registry;
  internal LocalPeerFactory(IpcRegistry registry) { _registry = registry; }
  public ILocalPeer Create(string name) => new LocalPeerAdapter(new IpcLocalPeer(_registry, name));
}
