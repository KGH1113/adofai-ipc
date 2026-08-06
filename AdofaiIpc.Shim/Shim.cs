using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace AdofaiIpc.Shim;

public static class Shim
{
  private const string LauncherType = "AdofaiIpc.Launcher.EntryPoint";
  private const string RuntimeType = "AdofaiIpc.Main";

  public static bool Load(UnityModManager.ModEntry modEntry)
  {
    StateStore store = new(modEntry.Path);
    string displayName = modEntry.Info.DisplayName;
    try
    {
      RuntimeState state = store.Load();
      if (!string.IsNullOrWhiteSpace(state.Trial))
      {
        string trial = state.Trial;
        modEntry.Info.DisplayName = Status(modEntry, "Trying " + trial + "...");
        if (TryLoad(store, trial, modEntry, out Exception trialError))
        {
          if (!VersionsEqual(state.Current, trial)) state.Previous = state.Current;
          state.Current = trial;
          state.Trial = null;
          TrySave(store, state, modEntry);
          modEntry.Info.Version = trial;
          modEntry.Info.DisplayName = displayName;
          return true;
        }

        modEntry.Logger.Warning("[AutoUpdate] Candidate " + trial + " failed: " + trialError);
      }

      if (TryLoad(store, state.Current, modEntry, out Exception currentError))
      {
        modEntry.Info.Version = state.Current;
        modEntry.Info.DisplayName = displayName;
        return true;
      }

      if (!string.IsNullOrWhiteSpace(state.Previous) &&
          !VersionsEqual(state.Current, state.Previous) &&
          TryLoad(store, state.Previous, modEntry, out Exception previousError))
      {
        string failed = state.Current;
        state.Current = state.Previous;
        state.Previous = null;
        TrySave(store, state, modEntry);
        modEntry.Info.Version = state.Current;
        modEntry.Info.DisplayName = displayName + " <color=yellow>[Rolled back]</color>";
        modEntry.Logger.Warning("[AutoUpdate] Rolled back from " + failed + " after: " + currentError);
        return true;
      }

      throw new InvalidOperationException("AdofaiIpc current runtime failed and no usable fallback was available.", currentError);
    }
    catch (Exception exception)
    {
      modEntry.Info.DisplayName = displayName + " <color=red>[Failed to load]</color>";
      modEntry.Logger.Error("[AutoUpdate] " + exception);
      return false;
    }
  }

  private static bool TryLoad(
    StateStore store,
    string version,
    UnityModManager.ModEntry modEntry,
    out Exception error)
  {
    try
    {
      string launcher = store.LauncherPath(version);
      if (File.Exists(launcher))
      {
        Invoke(launcher, LauncherType, modEntry);
      }
      else
      {
        string runtime = store.RuntimePath(version);
        if (!File.Exists(runtime)) throw new FileNotFoundException("AdofaiIpc runtime is missing.", runtime);
        Invoke(runtime, RuntimeType, modEntry);
      }
      error = null;
      return true;
    }
    catch (Exception exception)
    {
      error = exception is TargetInvocationException invocation && invocation.InnerException != null
        ? invocation.InnerException
        : exception;
      return false;
    }
  }

  private static void Invoke(string assemblyPath, string typeName, UnityModManager.ModEntry modEntry)
  {
    Assembly assembly = Assembly.LoadFrom(assemblyPath);
    Type type = assembly.GetType(typeName, true);
    MethodInfo method = type.GetMethod(
      "Load",
      BindingFlags.Public | BindingFlags.Static,
      null,
      new[] { typeof(UnityModManager.ModEntry) },
      null) ?? throw new MissingMethodException(typeName, "Load");
    object result = method.Invoke(null, new object[] { modEntry });
    if (result is bool loaded && !loaded) throw new InvalidOperationException(typeName + ".Load returned false.");
  }

  private static bool VersionsEqual(string left, string right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

  private static void TrySave(StateStore store, RuntimeState state, UnityModManager.ModEntry modEntry)
  {
    try { store.Save(state); }
    catch (Exception exception)
    {
      modEntry.Logger.Warning("[AutoUpdate] Runtime state could not be saved; the loaded runtime remains active: " + exception);
    }
  }

  private static string Status(UnityModManager.ModEntry modEntry, string status) =>
    modEntry.Info.Id + " <color=grey>[" + status + "]</color>";
}
