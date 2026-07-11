using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class ModActivator
{
  private const string ModId = "AdofaiIpc";

  public static UnityModManager.ModEntry Find()
  {
    return UnityModManager.modEntries.FirstOrDefault(entry => entry.Info.Id == ModId);
  }

  public static bool IsLoaded()
  {
    return AppDomain.CurrentDomain.GetAssemblies()
      .Any(assembly => assembly.GetName().Name == ModId);
  }

  public static void EnsureVersion(UnityModManager.ModEntry dependency, string minimumVersion)
  {
    if (dependency == null) return;
    if (!TryParseVersion(dependency.Info.Version, out Version installed) ||
        !TryParseVersion(minimumVersion, out Version required))
      throw new InvalidDataException("AdofaiIpc version metadata is invalid.");

    if (installed < required)
      throw new InvalidOperationException(
        $"AdofaiIpc {minimumVersion} or newer is required. Installed: {dependency.Info.Version}");
  }

  public static void EnsureActive(string minimumVersion)
  {
    UnityModManager.ModEntry dependency = Find();
    if (dependency != null)
    {
      EnsureVersion(dependency, minimumVersion);
      if (!dependency.Enabled)
        throw new InvalidOperationException("AdofaiIpc is installed but disabled.");

      if (!dependency.Active) dependency.Active = true;
      if (!dependency.Active || !IsLoaded())
        throw new InvalidOperationException("AdofaiIpc failed to load.");
      return;
    }

    string modPath = Path.Combine(UnityModManager.modsPath, ModId);
    string infoPath = Path.Combine(modPath, "Info.json");
    if (!File.Exists(infoPath))
      throw new FileNotFoundException("AdofaiIpc Info.json was not found.", infoPath);

    UnityModManager.ModInfo info = JsonConvert.DeserializeObject<UnityModManager.ModInfo>(
      File.ReadAllText(infoPath));
    if (info == null || info.Id != ModId)
      throw new InvalidDataException("AdofaiIpc Info.json is invalid.");

    dependency = new UnityModManager.ModEntry(info, EnsureTrailingSeparator(modPath));
    dependency.Enabled = true;
    UnityModManager.modEntries.Add(dependency);
    EnsureVersion(dependency, minimumVersion);
    dependency.Active = true;

    if (!dependency.Active || !IsLoaded())
      throw new InvalidOperationException("AdofaiIpc failed to load after installation.");
  }

  private static bool TryParseVersion(string value, out Version version)
  {
    version = null;
    if (string.IsNullOrWhiteSpace(value)) return false;
    string numeric = value.Split(new[] { '-', '+', ' ' }, 2)[0];
    return Version.TryParse(numeric, out version);
  }

  private static string EnsureTrailingSeparator(string path)
  {
    return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;
  }
}
