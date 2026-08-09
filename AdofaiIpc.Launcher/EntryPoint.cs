using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace AdofaiIpc.Launcher;

public static class EntryPoint
{
  private const string UnsafeFallbackKey = "AdofaiIpc.UnsafeFallback.v1";

  public static bool Load(UnityModManager.ModEntry owner)
  {
    string current = ResolveVersion();
    string candidate = UpdateCoordinator.TryStageLatest(owner, current);
    if (!string.IsNullOrEmpty(candidate) && candidate != current)
    {
      try
      {
        if (InvokeCandidate(owner, candidate))
        {
          try { UpdateCoordinator.Commit(owner, candidate); }
          catch (Exception exception)
          { owner.Logger.Warning("[AutoUpdate] The new version is active, but its state could not be saved: " + exception.Message); }
          owner.Info.Version = candidate;
          owner.Logger.Log("[AutoUpdate] Updated AdofaiIpc to " + candidate + ".");
          return true;
        }
      }
      catch (Exception exception)
      {
        owner.Logger.Warning("[AutoUpdate] AdofaiIpc " + candidate + " failed to initialize: " + Unwrap(exception));
      }

      if (UnsafeFallbackRequested()) return false;
      try { UpdateCoordinator.Discard(owner, candidate); }
      catch (Exception exception) { owner.Logger.Warning("[AutoUpdate] Could not discard the failed update: " + exception.Message); }
    }
    return LoadRuntime(owner, current);
  }

  public static bool LoadInstalled(UnityModManager.ModEntry owner) => LoadRuntime(owner, ResolveVersion());

  private static bool InvokeCandidate(UnityModManager.ModEntry owner, string version)
  {
    string path = Path.Combine(owner.Path, "Launcher", "versions", version, "AdofaiIpc.Launcher.dll");
    Assembly assembly = Assembly.LoadFrom(path);
    Type type = assembly.GetType("AdofaiIpc.Launcher.EntryPoint", true);
    MethodInfo method = type.GetMethod("LoadInstalled", BindingFlags.Public | BindingFlags.Static, null,
      new[] { typeof(UnityModManager.ModEntry) }, null) ?? throw new MissingMethodException(type.FullName, "LoadInstalled");
    object result = method.Invoke(null, new object[] { owner });
    return method.ReturnType != typeof(bool) || result is bool value && value;
  }

  private static bool LoadRuntime(UnityModManager.ModEntry owner, string version)
  {
    string path = Path.Combine(owner.Path, "Runtime", "versions", version, "AdofaiIpc.dll");
    if (!File.Exists(path)) throw new FileNotFoundException("AdofaiIpc runtime is missing.", path);
    owner.Info.Version = version;
    Assembly assembly = Assembly.LoadFrom(path);
    Type type = assembly.GetType("AdofaiIpc.Main", true);
    MethodInfo load = type.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null,
      new[] { typeof(UnityModManager.ModEntry) }, null) ?? throw new MissingMethodException(type.FullName, "Load");
    try
    {
      object result = load.Invoke(null, new object[] { owner });
      if (load.ReturnType != typeof(bool) || result is bool value && value) return true;
    }
    catch (Exception exception)
    {
      owner.Logger.Error("AdofaiIpc runtime initialization failed: " + Unwrap(exception));
    }

    MethodInfo rollback = type.GetMethod("Rollback", BindingFlags.Public | BindingFlags.Static, null,
      new[] { typeof(UnityModManager.ModEntry) }, null);
    bool cleaned = false;
    try { cleaned = rollback != null && rollback.Invoke(null, new object[] { owner }) is bool value && value; }
    catch (Exception exception) { owner.Logger.Error("AdofaiIpc cleanup failed: " + Unwrap(exception)); }
    if (!cleaned) AppDomain.CurrentDomain.SetData(UnsafeFallbackKey, true);
    return false;
  }

  private static string ResolveVersion()
  {
    DirectoryInfo directory = Directory.GetParent(typeof(EntryPoint).Assembly.Location);
    if (directory == null || !SemanticVersion.TryParse(directory.Name, out _))
      throw new InvalidDataException("AdofaiIpc launcher version directory is invalid.");
    return directory.Name;
  }

  private static bool UnsafeFallbackRequested() =>
    AppDomain.CurrentDomain.GetData(UnsafeFallbackKey) is bool value && value;

  private static Exception Unwrap(Exception exception) =>
    exception is TargetInvocationException invocation && invocation.InnerException != null
      ? invocation.InnerException
      : exception;
}
