using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public sealed class ModCallContext<TApp> where TApp : IpcApp
{
  public TApp App { get; }
  public CancellationToken CancellationToken { get; }
  internal ModCallContext(TApp app, CancellationToken cancellation) { App = app; CancellationToken = cancellation; }
}
