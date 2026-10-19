using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public abstract class IpcMethod
{
  public string Name { get; }
  public string WireName { get; }
  protected IpcMethod(string name, string wireName) { Name = name; WireName = wireName; }
}

public sealed class IpcMethod<TRequest, TResult> : IpcMethod
{
  public IpcCodec<TRequest> Input { get; }
  public IpcCodec<TResult> Output { get; }
  public IpcMethod(string name, string wireName, IpcCodec<TRequest> input, IpcCodec<TResult> output)
    : base(name, wireName) { Input = input; Output = output; }
}

public abstract class ModContract<TApp> where TApp : IpcApp
{
  public string Id { get; }
  public string Hash { get; }
  internal IpcMethod[] Methods { get; }
  internal Func<IpcAppInvoker, TApp> AppFactory { get; }
  protected ModContract(string id, string hash, IpcMethod[] methods, Func<IpcAppInvoker, TApp> appFactory)
  {
    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("A contract identity is required.");
    Id = id; Hash = hash; Methods = (IpcMethod[])methods.Clone(); AppFactory = appFactory;
  }
}

public sealed class ModCallContext<TApp> where TApp : IpcApp
{
  public TApp App { get; }
  public CancellationToken CancellationToken { get; }
  internal ModCallContext(TApp app, CancellationToken cancellation) { App = app; CancellationToken = cancellation; }
}

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

public abstract class IpcAppInvoker
{
  public abstract string AppId { get; }
  public abstract string DisplayName { get; }
  public abstract string SessionId { get; }
  public abstract CancellationToken CancellationToken { get; }
  public abstract Task<TResult> InvokeAsync<TRequest, TResult>(IpcMethod<TRequest, TResult> method, TRequest request, CancellationToken cancellationToken = default);
}

public sealed class IpcCallException : Exception
{
  public string Code { get; }
  public IpcCallException(string code, string message) : base(message) { Code = code; }
}

public static class IpcModExtensions
{
  public static ModRegistration<TApp> RegisterMod<TApp>(this IIpcRuntime ipc, ModContract<TApp> contract,
    string displayName, string[] allowedOrigins) where TApp : IpcApp
    => new ModRegistration<TApp>(ipc, contract, displayName, allowedOrigins);
}
