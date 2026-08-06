using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace AdofaiIpc.Launcher;

public static class EntryPoint
{
  public static bool Load(UnityModManager.ModEntry modEntry)
  {
    string version = ResolveVersion();
    string runtimePath = Path.Combine(modEntry.Path, "Runtime", "versions", version, "AdofaiIpc.dll");
    if (!File.Exists(runtimePath)) throw new FileNotFoundException("AdofaiIpc payload is missing.", runtimePath);

    Assembly assembly = Assembly.LoadFrom(runtimePath);
    Type type = assembly.GetType("AdofaiIpc.Main", true);
    MethodInfo method = type.GetMethod(
      "Load",
      BindingFlags.Public | BindingFlags.Static,
      null,
      new[] { typeof(UnityModManager.ModEntry) },
      null) ?? throw new MissingMethodException("AdofaiIpc.Main", "Load");
    object result = method.Invoke(null, new object[] { modEntry });
    if (result is bool loaded && !loaded)
    {
      MethodInfo rollback = type.GetMethod(
        "Rollback",
        BindingFlags.Public | BindingFlags.Static,
        null,
        new[] { typeof(UnityModManager.ModEntry) },
        null);
      rollback?.Invoke(null, new object[] { modEntry });
      return false;
    }

    UpdateCoordinator.Schedule(modEntry, version);
    return true;
  }

  private static string ResolveVersion()
  {
    string location = typeof(EntryPoint).Assembly.Location;
    DirectoryInfo directory = Directory.GetParent(location);
    if (directory == null || string.IsNullOrWhiteSpace(directory.Name))
      throw new InvalidDataException("AdofaiIpc launcher version directory is invalid.");
    return directory.Name;
  }
}
