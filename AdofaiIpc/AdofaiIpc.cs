using System;
using AdofaiIpc.Core;
using AdofaiIpc.Unity;

namespace AdofaiIpc;

public static class AdofaiIpc
{
  internal static IpcRegistry Registry { get; } = new IpcRegistry();

  public static AdofaiIpcNamespace RegisterNamespace(string name, IpcNamespaceInfo info = null)
  {
    return Registry.RegisterNamespace(name, info);
  }

  public static bool UnregisterNamespace(string name)
  {
    return Registry.UnregisterNamespace(name);
  }

  public static bool IsMainThread => MainThreadDispatcher.IsMainThread;

  public static void RunOnMainThread(Action action)
  {
    MainThreadDispatcher.Run(action);
  }
}
