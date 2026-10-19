using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Runtime;

internal sealed class FeatureBuilder : IFeatureBuilder
{
  private readonly FeatureRegistration _registration;
  private bool _sealed;
  internal FeatureBuilder(FeatureRegistration registration) { _registration = registration; }
  internal void Seal() => _sealed = true;
  private void Check() { if (_sealed) throw new InvalidOperationException("Feature configuration has finished."); _registration.Check(); }
  public void OnCommand(string name, IpcExecutionContext context, Func<IIncomingMessage, Task> handler)
  {
    Check();
    if (handler == null) throw new ArgumentNullException(nameof(handler));
    if (context != IpcExecutionContext.Background && context != IpcExecutionContext.GameThread) throw new ArgumentOutOfRangeException(nameof(context));
    Func<IpcCommand, Task> callback = command => handler(new IncomingMessage(_registration, command));
    if (context == IpcExecutionContext.GameThread)
      _registration.Handle.RegisterGameThreadAsyncCommand(name, callback);
    else _registration.Handle.RegisterAsyncCommand(name, callback);
  }
  public void OnClientAttached(Action<IClientConnection> handler) { Check(); _registration.AddAttached(handler); }
  public void OnClientDetached(Action<IClientConnection> handler) { Check(); _registration.AddDetached(handler); }
  public void OnEvent(Action<PeerEvent> handler) { Check(); _registration.AddEvent(handler); }
}
