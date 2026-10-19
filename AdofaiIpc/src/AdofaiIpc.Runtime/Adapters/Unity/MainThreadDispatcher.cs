using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace AdofaiIpc.Unity;

internal sealed class MainThreadDispatcher : MonoBehaviour
{
  private const int MaxActionsPerFrame = 64;
  private const int MaxPendingActions = 1024;

  private static readonly ConcurrentQueue<Action> Pending = new();
  private static MainThreadDispatcher _instance;
  private static int _mainThreadId;
  private static int _pendingCount;

  internal static bool IsMainThread =>
    Thread.CurrentThread.ManagedThreadId == _mainThreadId;

  internal static void Initialize()
  {
    if (_instance != null) return;

    _mainThreadId = Thread.CurrentThread.ManagedThreadId;
    GameObject gameObject = new("AdofaiIpc Main Thread Dispatcher");
    DontDestroyOnLoad(gameObject);
    _instance = gameObject.AddComponent<MainThreadDispatcher>();
  }

  internal static void Run(Action action)
  {
    if (action == null) throw new ArgumentNullException(nameof(action));
    if (_instance == null) throw new InvalidOperationException("AdofaiIpc is not initialized.");

    if (IsMainThread)
    {
      action();
      return;
    }

    if (Interlocked.Increment(ref _pendingCount) > MaxPendingActions)
    {
      Interlocked.Decrement(ref _pendingCount);
      throw new InvalidOperationException("IPC game-thread queue is full.");
    }
    Pending.Enqueue(action);
  }

  internal static void Shutdown()
  {
    while (Pending.TryDequeue(out _))
    {
      Interlocked.Decrement(ref _pendingCount);
    }

    if (_instance != null)
    {
      Destroy(_instance.gameObject);
      _instance = null;
    }

    _mainThreadId = 0;
  }

  private void Update()
  {
    for (int i = 0; i < MaxActionsPerFrame && Pending.TryDequeue(out Action action); i++)
    {
      Interlocked.Decrement(ref _pendingCount);
      try
      {
        action();
      }
      catch (Exception e)
      {
        Debug.LogException(e);
      }
    }
  }
}
