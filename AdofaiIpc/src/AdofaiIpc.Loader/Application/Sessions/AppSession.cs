using System;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
namespace AdofaiIpc;

internal sealed class AppSession<TApp> : IDisposable where TApp : IpcApp
{
  internal readonly string Id, DisplayName, SessionId = Guid.NewGuid().ToString("N");
  internal readonly IClientConnection Client;
  private readonly CancellationTokenSource _lifetime;
  private readonly object _observerGate = new object();
  private readonly object _lifetimeGate = new object();
  private CancellationTokenRegistration _disconnect;
  private bool _disposed;
  private readonly TaskCompletionSource<bool> _observersReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  private Task _observerTail;
  internal readonly CancellationToken Cancellation;
  internal TApp App;
  internal AppSession(string id, string displayName, IClientConnection client, CancellationToken modLifetime)
  {
    Id = id; DisplayName = displayName; Client = client; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(modLifetime);
    Cancellation = _lifetime.Token; _observerTail = _observersReady.Task;
  }
  internal void ReleaseObservers(bool acknowledged) => _observersReady.TrySetResult(acknowledged);
  internal void AttachDisconnect(CancellationToken cancellationToken, Action disconnected)
  {
    // Register may synchronously invoke the callback when the transport already closed.
    // Never hold the lifetime lock while disposing a registration that may be running.
    var registration = cancellationToken.Register(disconnected);
    bool dispose;
    lock (_lifetimeGate)
    {
      dispose = _disposed;
      if (!dispose) _disconnect = registration;
    }
    if (dispose) registration.Dispose();
  }
  internal void EnqueueObserver(Action callback)
  {
    lock (_observerGate)
      _observerTail = _observerTail.ContinueWith(_ => {
        if (_observersReady.Task.GetAwaiter().GetResult()) callback();
      }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
  }
  public void Dispose()
  {
    CancellationTokenRegistration disconnect;
    lock (_lifetimeGate)
    {
      if (_disposed) return;
      _disposed = true; disconnect = _disconnect;
    }
    disconnect.Dispose();
    try { _lifetime.Cancel(); } finally { _lifetime.Dispose(); }
  }
}
