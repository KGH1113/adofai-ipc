using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public interface IFeatureBuilder
{
  // GameThread selects the invocation thread. Async continuations must dispatch explicitly.
  void OnCommand(string name, IpcExecutionContext context, Func<IIncomingMessage, Task> handler);
  void OnClientAttached(Action<IClientConnection> handler);
  void OnClientDetached(Action<IClientConnection> handler);
  void OnEvent(Action<PeerEvent> handler);
}
