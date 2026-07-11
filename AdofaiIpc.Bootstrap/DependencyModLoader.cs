using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyModLoader
{
  private static readonly object Sync = new();
  private static readonly HashSet<string> LoadedMods = new(StringComparer.Ordinal);

  public static void Load(UnityModManager.ModEntry modEntry, BootstrapManifest manifest)
  {
    lock (Sync)
    {
      if (LoadedMods.Contains(modEntry.Info.Id)) return;
    }

    string assemblyPath = Path.GetFullPath(Path.Combine(modEntry.Path, manifest.AssemblyName));
    string modRoot = EnsureTrailingSeparator(Path.GetFullPath(modEntry.Path));
    if (!assemblyPath.StartsWith(modRoot, StringComparison.Ordinal) || !File.Exists(assemblyPath))
      throw new FileNotFoundException("Dependent mod assembly was not found.", assemblyPath);

    int separator = manifest.EntryMethod.LastIndexOf('.');
    if (separator <= 0 || separator == manifest.EntryMethod.Length - 1)
      throw new InvalidDataException("EntryMethod must contain a type and method name.");

    string typeName = manifest.EntryMethod.Substring(0, separator);
    string methodName = manifest.EntryMethod.Substring(separator + 1);
    Assembly assembly = Assembly.LoadFrom(assemblyPath);
    Type type = assembly.GetType(typeName, true);
    MethodInfo method = type.GetMethod(
      methodName,
      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
      null,
      new[] { typeof(UnityModManager.ModEntry) },
      null);

    if (method == null)
      throw new MissingMethodException(typeName, methodName);

    object result = method.Invoke(null, new object[] { modEntry });
    if (method.ReturnType == typeof(bool) && result is bool loaded && !loaded)
      throw new InvalidOperationException($"{manifest.EntryMethod} returned false.");

    lock (Sync) LoadedMods.Add(modEntry.Info.Id);
  }

  private static string EnsureTrailingSeparator(string path)
  {
    return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;
  }
}
