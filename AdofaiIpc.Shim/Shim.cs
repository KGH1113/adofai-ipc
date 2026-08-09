using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace AdofaiIpc.Shim;

public static class Shim
{
  internal const string UnsafeFallbackKey = "AdofaiIpc.UnsafeFallback.v1";

  public static bool Load(UnityModManager.ModEntry modEntry)
  {
    string displayName = modEntry.Info.DisplayName;
    try
    {
      AppDomain.CurrentDomain.SetData(UnsafeFallbackKey, false);
      StateStore store = new(modEntry.Path);
      RuntimeState state = store.Load();
      Exception trialError = null;

      if (!string.IsNullOrEmpty(state.Trial))
      {
        string trial = state.Trial;
        try
        {
          if (Invoke(store, trial, modEntry))
          {
            string active = LoadedVersion(store, trial, modEntry.Info.Version);
            Promote(store, store.Load(), active);
            Complete(modEntry, displayName, active);
            return true;
          }
        }
        catch (Exception exception) { trialError = Unwrap(exception); }

        if (UnsafeFallbackRequested())
          throw new InvalidOperationException("The trial runtime could not be cleaned up safely. Restart is required.", trialError);
        state.Trial = null;
        store.Save(state);
      }

      foreach (string version in Candidates(state))
      {
        try
        {
          if (!Invoke(store, version, modEntry))
          {
            if (UnsafeFallbackRequested()) break;
            continue;
          }
          string active = LoadedVersion(store, version, modEntry.Info.Version);
          RuntimeState latest = store.Load();
          if (!string.Equals(active, latest.Current, StringComparison.Ordinal) || latest.Trial != null)
            Promote(store, latest, active);
          Complete(modEntry, displayName, active);
          if (trialError != null)
            modEntry.Logger.Warning("[AutoUpdate] Update failed; using " + version + ": " + trialError.Message);
          return true;
        }
        catch (Exception exception)
        {
          modEntry.Logger.Error("[AutoUpdate] AdofaiIpc " + version + " failed: " + Unwrap(exception));
          if (UnsafeFallbackRequested()) break;
        }
      }

      throw new InvalidOperationException("No usable AdofaiIpc version was available.", trialError);
    }
    catch (Exception exception)
    {
      modEntry.Info.DisplayName = displayName + " <color=red>[Failed to load]</color>";
      modEntry.Logger.Error("[AutoUpdate] " + exception);
      return false;
    }
  }

  private static void Promote(StateStore store, RuntimeState state, string version)
  {
    if (!string.Equals(state.Current, version, StringComparison.Ordinal)) state.Previous = state.Current;
    state.Current = version;
    state.Trial = null;
    store.Save(state);
  }

  private static IEnumerable<string> Candidates(RuntimeState state)
  {
    if (!string.IsNullOrEmpty(state.Current)) yield return state.Current;
    if (!string.IsNullOrEmpty(state.Previous) && state.Previous != state.Current) yield return state.Previous;
  }

  private static bool Invoke(StateStore store, string version, UnityModManager.ModEntry owner)
  {
    string path = store.LauncherPath(version);
    if (!File.Exists(path)) throw new FileNotFoundException("AdofaiIpc launcher is missing.", path);
    Assembly assembly = Assembly.LoadFrom(path);
    Type type = assembly.GetType("AdofaiIpc.Launcher.EntryPoint", true);
    MethodInfo method = type.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null,
      new[] { typeof(UnityModManager.ModEntry) }, null) ??
      throw new MissingMethodException(type.FullName, "Load");
    object result = method.Invoke(null, new object[] { owner });
    return method.ReturnType != typeof(bool) || result is bool value && value;
  }

  private static void Complete(UnityModManager.ModEntry modEntry, string displayName, string version)
  {
    modEntry.Info.Version = version;
    modEntry.Info.DisplayName = displayName;
  }

  private static string LoadedVersion(StateStore store, string requested, string reported)
  {
    if (!string.IsNullOrWhiteSpace(reported))
    {
      try { if (File.Exists(store.LauncherPath(reported))) return reported; }
      catch { }
    }
    return requested;
  }

  private static bool UnsafeFallbackRequested() =>
    AppDomain.CurrentDomain.GetData(UnsafeFallbackKey) is bool value && value;

  private static Exception Unwrap(Exception exception) =>
    exception is TargetInvocationException invocation && invocation.InnerException != null
      ? invocation.InnerException
      : exception;
}
