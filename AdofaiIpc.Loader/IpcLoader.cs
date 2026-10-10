using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AdofaiIpc.Contracts;

namespace AdofaiIpc.Loader;

/// <summary>Only selects local bundled DLLs. Never installs, downloads, or replaces loaded code.</summary>
public static class IpcLoader
{
  private const string RuntimeSlot = "AdofaiIpc.ProcessRuntime";
  private const string FailureSlot = "AdofaiIpc.ProcessRuntime.Failure";
  private const string GateName = "AdofaiIpc.ProcessRuntime.Gate";

  /// <summary>Call from a mod's game-thread entry point, before registering its feature.</summary>
  public static IIpcRuntime GetRuntime(string modsDirectory, RuntimeRequirements requirements = null)
  {
    if (string.IsNullOrWhiteSpace(modsDirectory)) throw new ArgumentException("The Mods directory is required.", nameof(modsDirectory));
    requirements ??= new RuntimeRequirements();
    SemanticVersion.Parse(requirements.MinimumRuntimeVersion);
    // Interned gate and AppDomain slots are shared even when loader copies are loaded separately.
    lock (string.Intern(GateName))
    {
      VerifyContractIdentity();
      object existing = AppDomain.CurrentDomain.GetData(RuntimeSlot);
      if (existing != null)
      {
        if (!(existing is IIpcRuntime runtime)) throw new InvalidOperationException("The process already selected an incompatible IPC contract.");
        RequireCompatible(runtime.Info, requirements);
        return runtime;
      }
      string failure = AppDomain.CurrentDomain.GetData(FailureSlot) as string;
      if (failure != null) throw new InvalidOperationException("IPC startup is locked for this process: " + failure + " Restart the game after correcting the bundled files.");

      RuntimeManifest selected = Discover(modsDirectory)
        .Where(candidate => !candidate.Version.IsPrerelease && candidate.ContractMajor == IpcContract.Major &&
          candidate.ContractAssemblyVersion == IpcContract.AssemblyVersion && candidate.WireMajor == IpcContract.WireMajor)
        .OrderByDescending(candidate => candidate.Version)
        .ThenBy(candidate => candidate.AssemblyPath, StringComparer.Ordinal).FirstOrDefault();
      if (selected == null) throw new InvalidOperationException("No compatible stable bundled IPC runtime was found under Mods/<mod>/ipc.");
      RequireCompatible(selected.ToInfo(), requirements);

      // Loading cannot be undone. A failed startup must never try another DLL in this process.
      AppDomain.CurrentDomain.SetData(FailureSlot, "startup is in progress");
      try
      {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "AdofaiIpc.Runtime"))
          throw new InvalidOperationException("A runtime DLL was loaded before selection. Consumer mods must reference only Contracts and Loader.");
        Assembly assembly = Assembly.LoadFrom(selected.AssemblyPath);
        var contract = assembly.GetReferencedAssemblies().SingleOrDefault(name => name.Name == "AdofaiIpc.Contracts");
        if (contract == null || contract.FullName != typeof(IIpcRuntime).Assembly.GetName().FullName)
          throw new InvalidDataException("The runtime references a different contract assembly identity.");
        Type entry = assembly.GetType("AdofaiIpc.Runtime.RuntimeEntry", true);
        MethodInfo describe = entry.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        MethodInfo start = entry.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (describe == null || start == null) throw new InvalidDataException("The runtime entry contract is missing.");
        selected.Verify(describe.Invoke(null, null) as RuntimeInfo);
        var runtime = start.Invoke(null, null) as IIpcRuntime ?? throw new InvalidDataException("The runtime did not return the shared contract.");
        selected.Verify(runtime.Info);
        AppDomain.CurrentDomain.SetData(RuntimeSlot, runtime);
        AppDomain.CurrentDomain.SetData(FailureSlot, null);
        return runtime;
      }
      catch (Exception error)
      {
        Exception cause = error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error;
        AppDomain.CurrentDomain.SetData(FailureSlot, cause.Message);
        throw new InvalidOperationException("IPC runtime startup failed; game restart is required.", cause);
      }
    }
  }

  private static IEnumerable<RuntimeManifest> Discover(string modsDirectory)
  {
    string root = Path.GetFullPath(modsDirectory);
    if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
    foreach (string mod in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.Ordinal))
    {
      string path = Path.Combine(mod, "ipc", "manifest.json");
      if (!File.Exists(path)) continue;
      RuntimeManifest candidate;
      try { candidate = RuntimeManifest.Read(path); }
      catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException ||
        error is Newtonsoft.Json.JsonException || error is BadImageFormatException || error is ArgumentException)
      { Trace.TraceWarning("Ignoring invalid IPC candidate " + path + ": " + error.Message); continue; }
      yield return candidate;
    }
  }

  private static void VerifyContractIdentity()
  {
    Assembly own = typeof(IIpcRuntime).Assembly;
    if (own.GetName().Version.ToString() != IpcContract.AssemblyVersion ||
      AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "AdofaiIpc.Contracts" && !ReferenceEquals(assembly, own)))
      throw new InvalidOperationException("IPC contract DLLs did not resolve to one shared assembly. Do not load contract copies with Assembly.Load(byte[]).");
  }

  private static void RequireCompatible(RuntimeInfo info, RuntimeRequirements requirement)
  {
    if (info.ContractMajor != requirement.ContractMajor || info.WireMajor != requirement.WireMajor)
      throw new InvalidOperationException("IPC contract/wire major mismatch. Selected " + info.ContractMajor + "/" + info.WireMajor +
        ", required " + requirement.ContractMajor + "/" + requirement.WireMajor + ".");
    if (SemanticVersion.Parse(info.Version).CompareTo(SemanticVersion.Parse(requirement.MinimumRuntimeVersion)) < 0)
      throw new InvalidOperationException("IPC runtime " + info.Version + " is selected; this mod requires " + requirement.MinimumRuntimeVersion + ". Update the bundled DLLs and restart the game.");
    foreach (string capability in requirement.RequiredCapabilities)
      if (!info.Supports(capability)) throw new InvalidOperationException("Selected IPC runtime lacks required capability: " + capability);
  }
}
