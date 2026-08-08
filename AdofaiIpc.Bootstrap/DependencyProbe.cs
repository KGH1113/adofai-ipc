using System;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyProbe
{
  public static DependencyIssue Probe(UnityModManager.ModEntry owner, BootstrapManifest manifest)
  {
    UnityModManager.ModEntry dependency = ModActivator.Find();
    if (dependency == null) return null;
    if (!dependency.Enabled)
      return Issue(owner, manifest, DependencyIssueKind.Disabled, dependency.Info.Version,
        "AdofaiIpc is explicitly disabled in Unity Mod Manager.");
    if (!ModActivator.TrySatisfies(dependency.Info.Version, manifest.MinimumAdofaiIpcVersion,
          out string detail))
      return Issue(owner, manifest, DependencyIssueKind.Outdated, dependency.Info.Version, detail);
    return null;
  }

  public static DependencyIssue Issue(UnityModManager.ModEntry owner, BootstrapManifest manifest,
    DependencyIssueKind kind, string installedVersion, string detail) => new()
  {
    Kind = kind,
    ModId = owner.Info.Id,
    DisplayName = string.IsNullOrWhiteSpace(owner.Info.DisplayName) ? owner.Info.Id : owner.Info.DisplayName,
    MinimumVersion = manifest.MinimumAdofaiIpcVersion,
    InstalledVersion = installedVersion,
    Detail = detail
  };
}
