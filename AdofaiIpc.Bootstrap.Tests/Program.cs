using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;
using AdofaiIpc.Bootstrap;
using AdofaiIpc.DependencyShim;
using UnityModManagerNet;
using AdofaiIpc.Migration;

internal static class Program
{
  private static int _passed;

  private static int Main()
  {
    try
    {
      Run("three-field manifest and legacy URL compatibility", TestManifest);
      Run("version comparison", TestVersions);
      Run("shared issue aggregation and dedupe", TestRegistry);
      Run("cross-bootstrap BCL registry protocol", TestCrossBootstrapRegistry);
      Run("atomic bootstrap state and backup recovery", TestStateRecovery);
      Run("bootstrap trial staging and discard", TestTrial);
      Run("legacy package seed migration", TestSeedMigration);
      Run("legacy dependent mod transition", TestDependentTransition);
      Run("lockstep product version", TestLockstepVersion);
      Console.WriteLine($"Passed {_passed} bootstrap tests.");
      return 0;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine(exception);
      return 1;
    }
  }

  private static void TestManifest()
  {
    using TemporaryDirectory directory = new();
    File.WriteAllText(Path.Combine(directory.Path, "AdofaiIpcBootstrap.json"),
      "{\"MinimumAdofaiIpcVersion\":\"0.2.0\",\"AssemblyName\":\"Core.dll\",\"EntryMethod\":\"Core.Load\"}");
    BootstrapManifest manifest = BootstrapManifest.Load(directory.Path);
    Assert(manifest.MinimumAdofaiIpcVersion == "0.2.0", "Minimum version was not parsed.");
    File.WriteAllText(Path.Combine(directory.Path, "AdofaiIpcBootstrap.json"),
      "{\"MinimumAdofaiIpcVersion\":\"0.2.0\",\"AssemblyName\":\"Core.dll\",\"EntryMethod\":\"Core.Load\",\"DownloadUrl\":\"legacy\",\"ChecksumUrl\":\"legacy\"}");
    Assert(BootstrapManifest.Load(directory.Path).AssemblyName == "Core.dll", "Legacy URL fields were not ignored.");
  }

  private static void TestVersions()
  {
    Assert(ModActivator.TrySatisfies("0.3.0", "0.3.0", out _), "Equal version was rejected.");
    Assert(ModActivator.TrySatisfies("0.3.1", "0.3.0", out _), "Newer version was rejected.");
    Assert(!ModActivator.TrySatisfies("0.2.0", "0.3.0", out _), "Official v0.2.0 was not outdated.");
    Assert(!ModActivator.TrySatisfies("preview", "0.3.0", out _), "Invalid version was accepted.");
  }

  private static void TestRegistry()
  {
    string suffix = Guid.NewGuid().ToString("N");
    DependencyIssueRegistry.Report(Issue("a-" + suffix, "Alpha", "0.2.0"));
    DependencyIssueRegistry.Report(Issue("b-" + suffix, "Beta", "0.3.0"));
    DependencyIssueRegistry.Report(Issue("a-" + suffix, "Alpha changed", "0.4.0"));
    List<Hashtable> rows = DependencyIssueRegistry.Snapshot();
    Assert(rows.Count(row => (string)row["modId"] == "a-" + suffix) == 1, "Repeated mod was not deduplicated.");
    Assert(rows.Any(row => (string)row["modId"] == "a-" + suffix &&
                           (string)row["minimumVersion"] == "0.4.0"), "Repeated mod was not updated.");
    Assert(rows.Any(row => (string)row["modId"] == "b-" + suffix), "Second mod was not aggregated.");
  }

  private static void TestStateRecovery()
  {
    using TemporaryDirectory directory = new();
    BootstrapStateStore.Write(directory.Path, State("0.2.0"));
    BootstrapStateStore.Write(directory.Path, State("0.3.0"));
    File.WriteAllText(Path.Combine(directory.Path, "DependencyBootstrap", "state.json"), "broken");
    BootstrapState recovered = BootstrapStateStore.Read(directory.Path);
    Assert(recovered.Current == "0.2.0", "Backup state was not recovered.");
  }

  private static void TestCrossBootstrapRegistry()
  {
    string id = "foreign-" + Guid.NewGuid().ToString("N");
    Hashtable shared = (Hashtable)AppDomain.CurrentDomain.GetData(DependencyIssueRegistry.RegistryKey);
    Hashtable issues = (Hashtable)shared["issues"];
    issues[id] = new Hashtable(StringComparer.Ordinal)
    {
      ["kind"] = "Disabled",
      ["modId"] = id,
      ["displayName"] = "Foreign bootstrap",
      ["minimumVersion"] = "0.2.0",
      ["installedVersion"] = "0.2.0",
      ["detail"] = "disabled"
    };
    Assert(DependencyIssueRegistry.Snapshot().Any(row => (string)row["modId"] == id),
      "A BCL-only issue written by another bootstrap copy was not visible.");
  }

  private static void TestTrial()
  {
    using TemporaryDirectory directory = new();
    BootstrapStateStore.Write(directory.Path, State("0.2.0"));
    string candidate = typeof(Bootstrap).Assembly.Location;
    string version = DependencyShim.StageCandidate(directory.Path, candidate);
    Assert(version == "0.3.0", "Bootstrap ProductVersion was not preserved by the shim.");
    Assert(BootstrapStateStore.Read(directory.Path).Trial == version, "Trial was not recorded.");
    DependencyShim.DiscardTrial(directory.Path, version);
    Assert(BootstrapStateStore.Read(directory.Path).Trial == null, "Trial was not discarded.");
  }

  private static void TestSeedMigration()
  {
    using TemporaryDirectory directory = new();
    File.WriteAllText(Path.Combine(directory.Path, "Info.json"),
      "{\"Id\":\"Fixture\",\"DisplayName\":\"Fixture\",\"Version\":\"1.0.0\",\"AssemblyName\":\"Assets/AdofaiIpc/AdofaiIpc.DependencyShim.dll\",\"EntryMethod\":\"AdofaiIpc.DependencyShim.DependencyShim.Load\"}");
    UnityModManager.ModInfo info = JsonConvert.DeserializeObject<UnityModManager.ModInfo>(
      File.ReadAllText(Path.Combine(directory.Path, "Info.json")));
    UnityModManager.ModEntry owner = new(info, directory.Path + Path.DirectorySeparatorChar);
    string version = DependencyShim.Seed(owner, typeof(Bootstrap).Assembly.Location);
    Assert(version == "0.3.0", "Seed did not preserve the bootstrap product version.");
    Assert(File.Exists(Path.Combine(directory.Path, "AdofaiIpc.DependencyShim.dll")), "Seed did not install the fixed shim.");
    Assert(File.Exists(Path.Combine(directory.Path, "DependencyBootstrap", "versions", version,
      "AdofaiIpc.Bootstrap.dll")), "Seed did not install the bootstrap candidate.");
    dynamic migrated = JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(directory.Path, "Info.json")));
    Assert((string)migrated.AssemblyName == "AdofaiIpc.DependencyShim.dll", "Seed did not switch Info.json last.");
    migrated.AssemblyName = "Assets/AdofaiIpc/AdofaiIpc.DependencyShim.dll";
    File.WriteAllText(Path.Combine(directory.Path, "Info.json"), JsonConvert.SerializeObject(migrated));
    Assert(!TransitionMigration.Prepare(owner), "A complete fresh install was treated as a legacy transition.");
    dynamic canonical = JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(directory.Path, "Info.json")));
    Assert((string)canonical.AssemblyName == "AdofaiIpc.DependencyShim.dll",
      "Fresh-install seed entrypoint was not normalized.");
  }

  private static void TestDependentTransition()
  {
    using TemporaryDirectory directory = new();
    string payloadManifest = Path.Combine(Path.GetDirectoryName(typeof(TransitionMigration).Assembly.Location),
      "AdofaiIpcBootstrap.json");
    File.WriteAllText(payloadManifest,
      "{\"MinimumAdofaiIpcVersion\":\"0.3.0\",\"AssemblyName\":\"Core.dll\",\"EntryMethod\":\"Core.Load\"}");
    File.WriteAllText(Path.Combine(directory.Path, "Info.json"),
      "{\"Id\":\"LegacyMod\",\"DisplayName\":\"Legacy Mod\",\"Version\":\"1.0.0\",\"AssemblyName\":\"AdofaiIpc.Bootstrap.dll\",\"EntryMethod\":\"AdofaiIpc.Bootstrap.Bootstrap.Load\"}");
    UnityModManager.ModInfo info = JsonConvert.DeserializeObject<UnityModManager.ModInfo>(
      File.ReadAllText(Path.Combine(directory.Path, "Info.json")));
    UnityModManager.ModEntry owner = new(info, directory.Path + Path.DirectorySeparatorChar);
    Assert(TransitionMigration.Prepare(owner), "Legacy dependent mod was not migrated.");
    Assert(!TransitionMigration.Prepare(owner), "Completed migration was not idempotent.");
    dynamic migrated = JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(directory.Path, "Info.json")));
    Assert((string)migrated.EntryMethod == "AdofaiIpc.DependencyShim.DependencyShim.Load",
      "Dependent mod entrypoint was not switched.");
    Assert(File.Exists(Path.Combine(directory.Path, "AdofaiIpcBootstrap.json.pending")),
      "The replacement dependency manifest was not staged.");
    typeof(DependencyShim).GetMethod("PromotePendingManifest", BindingFlags.NonPublic | BindingFlags.Static)
      ?.Invoke(null, new object[] { directory.Path });
    dynamic manifest = JsonConvert.DeserializeObject(
      File.ReadAllText(Path.Combine(directory.Path, "AdofaiIpcBootstrap.json")));
    Assert((string)manifest.MinimumAdofaiIpcVersion == "0.3.0",
      "The replacement dependency manifest was not activated by the shim.");
    File.Delete(payloadManifest);
  }

  private static void TestLockstepVersion()
  {
    string root = AppContext.BaseDirectory;
    string version = ReadJsonVersion(Path.Combine(root, "Current", "Info.json"), "Version");
    Assert(version == "0.3.0", "Unexpected canonical product version.");
    Assert(ReadJsonVersion(Path.Combine(root, "Current", "client-package.json"), "version") == version,
      "npm package is not lockstep with Info.json.");
    Assert(FileVersionInfo.GetVersionInfo(typeof(Bootstrap).Assembly.Location).ProductVersion == version,
      "Bootstrap ProductVersion is not lockstep with Info.json.");
    Assert(FileVersionInfo.GetVersionInfo(Path.Combine(root, "Current", "AdofaiIpc.dll")).ProductVersion == version,
      "Runtime ProductVersion is not lockstep with Info.json.");
    Assert(FileVersionInfo.GetVersionInfo(Path.Combine(root, "Current", "AdofaiIpc.DependencyShim.dll")).ProductVersion == "1.0.0",
      "Fixed dependency shim ABI version changed.");
  }

  private static string ReadJsonVersion(string path, string property)
  {
    dynamic value = JsonConvert.DeserializeObject(File.ReadAllText(path));
    return (string)value[property];
  }


  private static DependencyIssue Issue(string id, string name, string minimum) => new()
  {
    Kind = DependencyIssueKind.Outdated,
    ModId = id,
    DisplayName = name,
    MinimumVersion = minimum,
    InstalledVersion = "0.1.0",
    Detail = "outdated"
  };

  private static BootstrapState State(string current) => new() { Current = current };

  private static void Run(string name, Action test)
  {
    test();
    _passed++;
    Console.WriteLine("PASS " + name);
  }

  private static void Assert(bool condition, string message)
  {
    if (!condition) throw new InvalidOperationException(message);
  }

  private sealed class TemporaryDirectory : IDisposable
  {
    public TemporaryDirectory()
    {
      Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "adofai-ipc-bootstrap-test-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
  }
}
