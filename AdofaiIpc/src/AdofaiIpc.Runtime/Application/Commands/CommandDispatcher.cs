using System;
using System.Threading;
using System.Threading.Tasks;
namespace AdofaiIpc.Core;

internal sealed class CommandDispatcher {
  private readonly SemaphoreSlim _slots = new SemaphoreSlim(256, 256);
  private readonly IGameThreadDispatcher _dispatcher;
  internal CommandDispatcher(IGameThreadDispatcher dispatcher) { _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)); }
  internal bool TryReserve() => _slots.Wait(0);
  internal void Release() => _slots.Release();
  internal void Execute(IpcCommand command, Func<IpcCommand, Task> handler, bool mainThread,
    CancellationTokenSource linked, Action accepted) {
    // A dispatcher may discard queued actions on shutdown. Release their admission
    // slots at cancellation rather than depending on an action that never runs.
    // Running handlers retain their slot until they actually finish.
    int executionState = 0; // 0 queued, 1 running, 2 finished/cancelled
    Action cancelQueued = () => {
      if (Interlocked.CompareExchange(ref executionState, 2, 0) != 0) return;
      linked.Dispose(); Release();
    };
    var cancellation = command.CancellationToken.Register(cancelQueued);
    if (Volatile.Read(ref executionState) == 2) cancellation.Dispose();
    accepted();
    Func<Task> execute = async () => {
      if (Interlocked.CompareExchange(ref executionState, 1, 0) != 0) return;
      try { if (!command.CancellationToken.IsCancellationRequested) await handler(command).ConfigureAwait(false); }
      catch (OperationCanceledException) { }
      catch (IpcMessageTooLargeException) { command.Fail("message_too_large", "The mod response exceeds the IPC message limit. Use a download ticket for large data."); }
      catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); command.Fail("handler_failed", "The mod could not complete the command."); }
      finally { Interlocked.Exchange(ref executionState, 2); cancellation.Dispose(); linked.Dispose(); Release(); }
    };
    if (mainThread)
    {
      try { _dispatcher.Run(() => { _ = execute(); }); }
      catch { cancelQueued(); cancellation.Dispose(); command.Fail("unavailable", "The game is shutting down."); }
    }
    else _ = Task.Run(execute);
  }
}
