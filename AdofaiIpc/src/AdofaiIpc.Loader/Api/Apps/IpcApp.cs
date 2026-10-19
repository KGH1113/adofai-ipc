using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public abstract class IpcApp
{
  private readonly IpcAppInvoker _invoker;
  public string AppId => _invoker.AppId;
  public string DisplayName => _invoker.DisplayName;
  /// <summary>Opaque identity of this registered connection. It changes after reconnecting.</summary>
  public string SessionId => _invoker.SessionId;
  public CancellationToken CancellationToken => _invoker.CancellationToken;
  protected IpcApp(IpcAppInvoker invoker) { _invoker = invoker; }
  protected Task<TResult> InvokeAsync<TRequest, TResult>(IpcMethod<TRequest, TResult> method, TRequest request, CancellationToken cancellationToken = default)
    => _invoker.InvokeAsync(method, request, cancellationToken);
  protected async Task InvokeVoidAsync<TRequest>(IpcMethod<TRequest, IpcUnit> method, TRequest request, CancellationToken cancellationToken = default)
    => await _invoker.InvokeAsync(method, request, cancellationToken).ConfigureAwait(false);
}
