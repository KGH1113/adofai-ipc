using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
namespace AdofaiIpc;

public sealed class ModRegistration<TApp> : IDisposable where TApp : IpcApp {
  private readonly ModRegistrationService<TApp> _service;
  internal ModRegistration(IIpcRuntime runtime, ModContract<TApp> contract, string displayName, string[] allowedOrigins)
    { _service = ModSdkComposition.Create(runtime, contract, displayName, allowedOrigins); }
  public IReadOnlyList<TApp> Apps => _service.Apps;
  public bool TryGetApp(string appId, out TApp app) => _service.TryGetApp(appId, out app);
  public event Action<TApp> AppConnected { add => _service.AppConnected += value; remove => _service.AppConnected -= value; }
  public event Action<TApp> AppDisconnected { add => _service.AppDisconnected += value; remove => _service.AppDisconnected -= value; }
  public event Action<Exception> AppEventFailed { add => _service.AppEventFailed += value; remove => _service.AppEventFailed -= value; }
  public void Handle<TRequest, TResult>(IpcMethod<TRequest, TResult> method, Func<TRequest, ModCallContext<TApp>, Task<TResult>> handler) => _service.Handle(method, handler);
  public void Handle<TRequest>(IpcMethod<TRequest, IpcUnit> method, Func<TRequest, ModCallContext<TApp>, Task> handler) => _service.Handle(method, handler);
  public void MarkReady() => _service.MarkReady();
  public void MarkError(string code, string message) => _service.MarkError(code, message);
  public void Dispose() => _service.Dispose();
}
