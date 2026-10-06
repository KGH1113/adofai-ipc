using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Core;

namespace AdofaiIpc;

/// <summary>In-process peer using the same namespace router as WebSocket connections.</summary>
public sealed class IpcLocalPeer : IDisposable, IIpcPeer
{
  private readonly IpcRegistry _registry;
  private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
  private readonly ConcurrentQueue<IpcPeerMessage> _messages = new ConcurrentQueue<IpcPeerMessage>();
  private int _draining, _queued;
  public IpcPeerInfo Info { get; }
  CancellationToken IIpcPeer.CancellationToken => _lifetime.Token;
  public event Action<IpcPeerMessage> Message;
  internal IpcLocalPeer(IpcRegistry registry, string name)
  {
    _registry = registry;
    Info = new IpcPeerInfo { PeerId = name + "-" + Guid.NewGuid().ToString("N"), ConnectionId = Guid.NewGuid().ToString("N") };
    if (!registry.Attach(this)) throw new InvalidOperationException("Too many IPC peers.");
  }
  public void Subscribe(string ns) => _registry.Receive(this, IpcPeerMessage.Create("subscribe", ns, null, null));
  public string SendCommand(string ns, string name, object payload)
  { var message = IpcPeerMessage.Create("command", ns, name, payload); _registry.Receive(this, message); return message.Id; }
  public void SendEvent(string ns, string name, object payload, string correlationId = null)
    => _registry.Receive(this, IpcPeerMessage.Create("event", ns, name, payload, correlationId));
  void IIpcPeer.Send(IpcPeerMessage message)
  {
    if (_lifetime.IsCancellationRequested) return;
    if (message.Kind == "ping") { _registry.Receive(this, IpcPeerMessage.Create("pong", null, null, null)); return; }
    if (Interlocked.Increment(ref _queued) > 256) { Interlocked.Decrement(ref _queued); Dispose(); return; }
    _messages.Enqueue(message); Drain();
  }
  private void Drain()
  {
    if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;
    _ = Task.Run(() => {
      try { while (!_lifetime.IsCancellationRequested && _messages.TryDequeue(out var message)) { Interlocked.Decrement(ref _queued); try { Message?.Invoke(message); } catch (Exception e) { System.Diagnostics.Trace.TraceError(e.ToString()); } } }
      finally { Interlocked.Exchange(ref _draining, 0); if (!_messages.IsEmpty && !_lifetime.IsCancellationRequested) Drain(); }
    });
  }
  void IIpcPeer.Close() => Dispose();
  public void Dispose() { if (_lifetime.IsCancellationRequested) return; _lifetime.Cancel(); _registry.Detach(this); while (_messages.TryDequeue(out _)) { } }
}
