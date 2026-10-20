using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Ports;
namespace AdofaiIpc;

internal sealed class AppCalls<TApp> : IDisposable where TApp : IpcApp {
  private readonly object _gate;
  private readonly IAppProtocol _protocol;
  private readonly Action _check;
  private readonly Func<AppSession<TApp>, bool> _active;
  private readonly Func<CancellationToken, CancellationToken, FileTransferScope> _files;
  private readonly Dictionary<string, PendingCall> _pending = new Dictionary<string, PendingCall>(StringComparer.Ordinal);
  private const int MaxPending = 256;
  internal AppCalls(object gate, IAppProtocol protocol, Action check, Func<AppSession<TApp>, bool> active,
    Func<CancellationToken, CancellationToken, FileTransferScope> files) {
    _gate = gate; _protocol = protocol; _check = check; _active = active; _files = files;
  }
  internal void Receive(PeerEvent message)
  {
    if (message.CorrelationId == null || (message.Name != "rpc.result" && message.Name != "rpc.error")) return;
    PendingCall pending;
    lock (_gate)
    {
      if (!_pending.TryGetValue(message.CorrelationId, out pending) || pending.ConnectionId != message.ConnectionId) return;
    }
    if (message.Name == "rpc.result") pending.Completion.TrySetResult(message.Json);
    else
    {
      try
      {
        pending.Completion.TrySetException(_protocol.ReadError(message.Json));
      }
      catch (Exception error) { pending.Completion.TrySetException(new IpcCallException("invalid_response", error.Message)); }
    }
  }

  internal async Task<TResult> InvokeApp<TRequest, TResult>(AppSession<TApp> identity, IpcMethod<TRequest, TResult> method, TRequest request, CancellationToken cancellationToken)
  {
    using var files = _files(identity.Cancellation, cancellationToken);
    string args = method.Input.Write(request, files), id = Guid.NewGuid().ToString("N");
    var pending = new PendingCall(identity.Client.ConnectionId);
    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(identity.Cancellation, cancellationToken);
    lifetime.CancelAfter(TimeSpan.FromSeconds(30));
    using var cancellation = lifetime.Token.Register(() => pending.Completion.TrySetCanceled());
    lock (_gate)
    {
      _check();
      if (!_active(identity))
        throw new IpcCallException("unavailable", "The app session disconnected.");
      if (_pending.Count >= MaxPending) throw new IpcCallException("busy", "Too many pending app calls.");
      _pending.Add(id, pending);
    }
    try
    {
      lifetime.Token.ThrowIfCancellationRequested();
      try { identity.Client.SendCommand(method.WireName, _protocol.WriteAppCall(id, identity.SessionId, args)); }
      catch (InvalidOperationException) { throw new IpcCallException("unavailable", "The app session disconnected."); }
      var result = method.Output.Read(await pending.Completion.Task.ConfigureAwait(false));
      files.Commit(); return result;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !identity.Cancellation.IsCancellationRequested)
    { throw new IpcCallException("timeout", "The app call timed out. Its side effects may already have occurred."); }
    finally { lock (_gate) _pending.Remove(id); }
  }

  internal void Disconnect(string connectionId) {
    lock (_gate) foreach (var pending in _pending.Values)
      if (pending.ConnectionId == connectionId) pending.Completion.TrySetException(new IpcCallException("unavailable", "The app disconnected."));
  }
  public void Dispose() {
    lock (_gate) { foreach (var pending in _pending.Values) pending.Completion.TrySetCanceled(); _pending.Clear(); }
  }
  private sealed class PendingCall
  {
    internal readonly string ConnectionId;
    internal readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal PendingCall(string connectionId) { ConnectionId = connectionId; }
  }
}
