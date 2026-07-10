using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace AdofaiIpc.Unity;

internal sealed class MainThreadDispatcher : MonoBehaviour
{
  private const int MaxActionsPerFrame = 64;

  private static readonly ConcurrentQueue<Action> Pending = new();
  private static MainThreadDispatcher _instance;
  private static int _mainThreadId;

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

    Pending.Enqueue(action);
  }

  internal static void Shutdown()
  {
    while (Pending.TryDequeue(out _))
    {
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
      try
      {
        action();
      }
      catch (Exception e)
      {
        Main.Instance?.LogException(e);
      }
    }
  }
}
