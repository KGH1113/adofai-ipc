using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public abstract class IpcAppInvoker
{
  public abstract string AppId { get; }
  public abstract string DisplayName { get; }
  public abstract string SessionId { get; }
  public abstract CancellationToken CancellationToken { get; }
  public abstract Task<TResult> InvokeAsync<TRequest, TResult>(IpcMethod<TRequest, TResult> method, TRequest request, CancellationToken cancellationToken = default);
}
