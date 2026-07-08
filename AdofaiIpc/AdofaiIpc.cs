using AdofaiIpc.Core;

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
}
