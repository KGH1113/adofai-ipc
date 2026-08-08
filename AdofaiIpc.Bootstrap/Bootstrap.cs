using System.Threading;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

public static class Bootstrap
{
  public static bool Load(UnityModManager.ModEntry modEntry)
  {
    string displayName = modEntry.Info.DisplayName;
    SynchronizationContext mainThread = SynchronizationContext.Current;
    modEntry.Info.DisplayName = Status(modEntry, "Checking AdofaiIpc...");
    _ = DependencyCoordinator.RunAsync(modEntry, displayName, mainThread);
    return true;
  }

  internal static string Status(UnityModManager.ModEntry modEntry, string status) =>
    $"{modEntry.Info.Id} <color=grey>[{status}]</color>";
}
