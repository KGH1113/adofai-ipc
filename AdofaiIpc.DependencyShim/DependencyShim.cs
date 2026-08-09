using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace AdofaiIpc.DependencyShim;

public static class DependencyShim
{
  private static readonly object Sync = new();
  private static readonly HashSet<string> LoadedOwners = new(StringComparer.Ordinal);

  public static bool Load(UnityModManager.ModEntry owner)
  {
    lock (Sync)
    {
      if (LoadedOwners.Contains(owner.Info.Id)) return true;
      BootstrapState state;
      try { state = BootstrapStateStore.Read(owner.Path); }
      catch (FileNotFoundException)
      {
        string seed = Path.Combine(Path.GetDirectoryName(typeof(DependencyShim).Assembly.Location),
          "AdofaiIpc.Bootstrap.dll");
        Seed(owner, seed);
        state = BootstrapStateStore.Read(owner.Path);
      }
      Exception trialError = null;
      if (!string.IsNullOrEmpty(state.Trial))
      {
        try
        {
          if (Invoke(owner, state.Trial))
          {
            state.Previous = state.Current;
            state.Current = state.Trial;
            state.Trial = null;
            BootstrapStateStore.Write(owner.Path, state);
            LoadedOwners.Add(owner.Info.Id);
            return true;
          }
        }
        catch (Exception exception) { trialError = Unwrap(exception); }

        state.Trial = null;
        BootstrapStateStore.Write(owner.Path, state);
      }

      foreach (string version in Candidates(state))
      {
        try
        {
          if (!Invoke(owner, version)) continue;
          if (version != state.Current)
          {
            state.Previous = state.Current;
            state.Current = version;
            BootstrapStateStore.Write(owner.Path, state);
          }
          LoadedOwners.Add(owner.Info.Id);
          if (trialError != null) owner.Logger.Error("Dependency bootstrap trial failed; using " + version + ": " + trialError);
          return true;
        }
        catch (Exception exception)
        {
          owner.Logger.Error("Dependency bootstrap " + version + " failed: " + Unwrap(exception));
        }
      }
      if (trialError != null) throw new InvalidOperationException("Dependency bootstrap trial and fallback failed.", trialError);
      return false;
    }
  }

  public static string Seed(UnityModManager.ModEntry owner, string sourceBootstrapPath)
  {
    if (owner == null) throw new ArgumentNullException(nameof(owner));
    string bootstrap = Path.GetFullPath(sourceBootstrapPath);
    AssemblyName identity = AssemblyName.GetAssemblyName(bootstrap);
    if (identity.Name != "AdofaiIpc.Bootstrap")
      throw new InvalidDataException("Seed is not an AdofaiIpc.Bootstrap assembly.");
    string version = FileVersionInfo.GetVersionInfo(bootstrap).ProductVersion;
    BootstrapStateStore.ValidateVersion(version);

    lock (Sync)
    {
      string rootShim = Path.Combine(owner.Path, "AdofaiIpc.DependencyShim.dll");
      CopyVerified(typeof(DependencyShim).Assembly.Location, rootShim, "AdofaiIpc.DependencyShim");
      string candidate = BootstrapStateStore.CandidatePath(owner.Path, version);
      CopyVerified(bootstrap, candidate, "AdofaiIpc.Bootstrap");
      BootstrapStateStore.Write(owner.Path, new BootstrapState { Current = version });
      WriteEntrypoint(owner.Path, rootShim);
      return version;
    }
  }

  public static string StageCandidate(string modRoot, string sourceAssemblyPath)
  {
    string source = Path.GetFullPath(sourceAssemblyPath);
    AssemblyName identity = AssemblyName.GetAssemblyName(source);
    if (identity.Name != "AdofaiIpc.Bootstrap" || identity.Version == null)
      throw new InvalidDataException("Candidate is not an AdofaiIpc.Bootstrap assembly.");
    string version = FileVersionInfo.GetVersionInfo(source).ProductVersion;
    BootstrapStateStore.ValidateVersion(version);
    lock (Sync)
    {
      BootstrapState state = BootstrapStateStore.Read(modRoot);
      if (state.Current == version) return version;
      string destination = BootstrapStateStore.CandidatePath(modRoot, version);
      Directory.CreateDirectory(Path.GetDirectoryName(destination));
      string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
      File.Copy(source, temporary, false);
      AssemblyName copied = AssemblyName.GetAssemblyName(temporary);
      string copiedVersion = FileVersionInfo.GetVersionInfo(temporary).ProductVersion;
      if (copied.Name != identity.Name || copied.Version != identity.Version || copiedVersion != version)
        throw new InvalidDataException("Copied dependency bootstrap candidate failed verification.");
      if (File.Exists(destination))
      {
        AssemblyName existing = AssemblyName.GetAssemblyName(destination);
        string existingVersion = FileVersionInfo.GetVersionInfo(destination).ProductVersion;
        File.Delete(temporary);
        if (existing.Name != identity.Name || existing.Version != identity.Version || existingVersion != version)
          throw new InvalidDataException("Existing dependency bootstrap candidate failed verification.");
      }
      else File.Move(temporary, destination);
      state.Trial = version;
      BootstrapStateStore.Write(modRoot, state);
      return version;
    }
  }

  public static void DiscardTrial(string modRoot, string version)
  {
    lock (Sync)
    {
      BootstrapState state = BootstrapStateStore.Read(modRoot);
      if (state.Trial != version) return;
      state.Trial = null;
      BootstrapStateStore.Write(modRoot, state);
      if (state.Current == version || state.Previous == version) return;
      string path = Path.GetDirectoryName(BootstrapStateStore.CandidatePath(modRoot, version));
      if (Directory.Exists(path)) Directory.Delete(path, true);
    }
  }

  private static IEnumerable<string> Candidates(BootstrapState state)
  {
    if (!string.IsNullOrEmpty(state.Current)) yield return state.Current;
    if (!string.IsNullOrEmpty(state.Previous) && state.Previous != state.Current) yield return state.Previous;
  }

  private static bool Invoke(UnityModManager.ModEntry owner, string version)
  {
    string path = BootstrapStateStore.CandidatePath(owner.Path, version);
    if (!File.Exists(path)) throw new FileNotFoundException("Dependency bootstrap candidate is missing.", path);
    Assembly assembly = Assembly.LoadFrom(path);
    Type type = assembly.GetType("AdofaiIpc.Bootstrap.Bootstrap", true);
    MethodInfo method = type.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null,
      new[] { typeof(UnityModManager.ModEntry) }, null);
    if (method == null) throw new MissingMethodException(type.FullName, "Load");
    object result = method.Invoke(null, new object[] { owner });
    return method.ReturnType != typeof(bool) || result is bool value && value;
  }

  private static Exception Unwrap(Exception exception) =>
    exception is TargetInvocationException invocation && invocation.InnerException != null
      ? invocation.InnerException
      : exception;

  private static void CopyVerified(string source, string destination, string expectedName)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(destination));
    if (File.Exists(destination))
    {
      if (AssemblyName.GetAssemblyName(destination).Name != expectedName)
        throw new InvalidDataException("Existing dependency file has an unexpected identity: " + destination);
      return;
    }
    string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
    File.Copy(source, temporary, false);
    if (AssemblyName.GetAssemblyName(temporary).Name != expectedName)
      throw new InvalidDataException("Copied dependency file has an unexpected identity.");
    File.Move(temporary, destination);
  }

  private static void WriteEntrypoint(string modRoot, string rootShim)
  {
    string infoPath = Path.Combine(modRoot, "Info.json");
    JObject info = JObject.Parse(File.ReadAllText(infoPath));
    info["AssemblyName"] = Path.GetFileName(rootShim);
    info["EntryMethod"] = "AdofaiIpc.DependencyShim.DependencyShim.Load";
    string temporary = infoPath + ".tmp-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(temporary, info.ToString());
    if (File.Exists(infoPath)) File.Replace(temporary, infoPath, infoPath + ".bak", true);
    else File.Move(temporary, infoPath);
  }
}
