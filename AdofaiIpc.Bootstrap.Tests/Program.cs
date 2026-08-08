using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AdofaiIpc.Bootstrap;
using AdofaiIpc.DependencyShim;

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
    Assert(ModActivator.TrySatisfies("0.2.0", "0.2.0", out _), "Equal version was rejected.");
    Assert(ModActivator.TrySatisfies("0.3.0", "0.2.0", out _), "Newer version was rejected.");
    Assert(!ModActivator.TrySatisfies("0.1.0", "0.2.0", out _), "Outdated version was accepted.");
    Assert(!ModActivator.TrySatisfies("preview", "0.2.0", out _), "Invalid version was accepted.");
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
    BootstrapStateStore.Write(directory.Path, State("0.2.1"));
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
    Assert(BootstrapStateStore.Read(directory.Path).Trial == version, "Trial was not recorded.");
    DependencyShim.DiscardTrial(directory.Path, version);
    Assert(BootstrapStateStore.Read(directory.Path).Trial == null, "Trial was not discarded.");
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
