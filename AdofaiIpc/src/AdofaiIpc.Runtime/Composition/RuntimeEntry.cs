using System;
using System.Threading;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using AdofaiIpc.Server;
using AdofaiIpc.Unity;
using UnityEngine;

namespace AdofaiIpc.Runtime;

/// <summary>Invoked by the local loader after version selection. Not a UMM mod entry point.</summary>
public static class RuntimeEntry
{
  private static RoutedRuntime _runtime;
  private static IpcServer _server;
  private static bool _stopped;

  public static RuntimeInfo Describe() => new RuntimeInfo(IpcProtocol.ProductVersion, IpcContract.Major, IpcProtocol.Version,
    "commands", "game-thread", "snapshots", "local-peer", "downloads", "typed-files");

  public static IIpcRuntime Start()
  {
    if (_stopped) throw new InvalidOperationException("The process host has stopped. Restart the game.");
    if (_runtime != null) return _runtime;
    if (SynchronizationContext.Current?.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
      throw new InvalidOperationException("Start the IPC runtime from the Unity game thread.");
    try
    {
      MainThreadDispatcher.Initialize();
      var registry = new IpcRegistry(MainThreadDispatcher.Run);
      var runtime = new RoutedRuntime(registry, Describe(), MainThreadDispatcher.Run, () => MainThreadDispatcher.IsMainThread);
      _server = new IpcServer(registry);
      _server.Start();
      _runtime = runtime;
      Application.quitting += Shutdown;
      Application.unloading += Shutdown;
      return runtime;
    }
    catch
    {
      _stopped = true;
      try { _server?.Stop(); }
      finally { _server = null; MainThreadDispatcher.Shutdown(); }
      throw;
    }
  }

  private static void Shutdown()
  {
    if (_stopped) return;
    _stopped = true;
    Application.quitting -= Shutdown;
    Application.unloading -= Shutdown;
    try { _runtime?.Shutdown(); }
    catch (Exception error) { Debug.LogException(error); }
    try { _server?.Stop(); }
    catch (Exception error) { Debug.LogException(error); }
    finally { _server = null; MainThreadDispatcher.Shutdown(); }
  }
}
