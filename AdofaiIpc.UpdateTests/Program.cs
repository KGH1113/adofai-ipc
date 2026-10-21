using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using AdofaiIpc.Launcher;

internal static class Program
{
  private static int _passed;

  private static int Main()
  {
    try
    {
      Run("SemVer stable and prerelease ordering", TestSemVer);
      Run("stable update manifest validation", TestManifest);
      Run("atomic update state backup recovery", TestStateRecovery);
      Run("package size and SHA-256 validation", TestPackageChecksum);
      Run("ZIP traversal and duplicate path rejection", TestZipAttacks);
      Run("retirement preserves offline or unmigrated consumers", TestRetirementWait);
      Run("retirement immediately removes IPC and preserves consumer data", TestRetirementDelete);
      Run("retirement rejects wrong identity and linked directories", TestRetirementSafety);
      if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADOFAIIPC_LEGACY_LAUNCHER")))
      {
        Run("published 0.4.1 updater accepts and stages 0.4.2", TestPublishedUpdater);
        Run("loaded compatibility code remains executable after immediate removal", TestLoadedRemoval);
      }
      Console.WriteLine("Passed " + _passed + " updater tests.");
      return 0;
    }
    catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
  }

  private static void TestSemVer()
  {
    Assert(SemanticVersion.Parse("0.3.0").CompareTo(SemanticVersion.Parse("0.3.0-beta.9")) > 0,
      "Stable did not sort after prerelease.");
    Assert(SemanticVersion.Parse("0.3.1").CompareTo(SemanticVersion.Parse("0.3.0")) > 0,
      "Newer stable did not sort after installed stable.");
    Assert(SemanticVersion.Parse("0.4.0-beta.10").CompareTo(SemanticVersion.Parse("0.4.0-beta.2")) > 0,
      "Numeric prerelease identifiers were not compared numerically.");
    Assert(SemanticVersion.Parse("999999999999999999999.0.0").CompareTo(SemanticVersion.Parse("2.0.0")) > 0,
      "Large SemVer numeric identifiers were not supported.");
    Assert(!SemanticVersion.TryParse("v0.3.0", out _), "A v-prefixed product version was accepted.");
  }

  private static void TestManifest()
  {
    string valid = "{\"SchemaVersion\":1,\"Version\":\"0.3.1\",\"PackageUrl\":\"https://github.com/KGH1113/adofai-ipc/releases/download/v0.3.1/AdofaiIpc.zip\",\"PackageSize\":42,\"Sha256\":\"" + new string('a', 64) + "\"}";
    Assert(UpdateManifest.Parse(valid).Version == "0.3.1", "Valid manifest was rejected.");
    Throws(() => UpdateManifest.Parse(valid.Replace("0.3.1\",", "0.3.1-beta.1\",")), "Prerelease stable manifest was accepted.");
    Throws(() => UpdateManifest.Parse(valid.Replace("github.com/KGH1113", "example.com/KGH1113")), "Unofficial package URL was accepted.");
  }

  private static void TestStateRecovery()
  {
    using TemporaryDirectory directory = new();
    UpdateStateStore store = new(directory.Path);
    store.Save(new UpdateState { Current = "0.3.0" });
    store.Save(new UpdateState { Current = "0.3.1", Previous = "0.3.0" });
    File.WriteAllText(Path.Combine(directory.Path, "Update", "state.json"), "broken");
    Assert(store.Load().Current == "0.3.0", "Backup state was not recovered.");
  }

  private static void TestPackageChecksum()
  {
    using TemporaryDirectory directory = new();
    string package = Path.Combine(directory.Path, "package.bin");
    File.WriteAllBytes(package, new byte[] { 1, 2, 3, 4 });
    string sha = Hash(package);
    PackageInstaller.VerifyPackage(package, Manifest(4, sha));
    Throws(() => PackageInstaller.VerifyPackage(package, Manifest(3, sha)), "Incorrect package size was accepted.");
    Throws(() => PackageInstaller.VerifyPackage(package, Manifest(4, new string('0', 64))), "Incorrect SHA-256 was accepted.");
  }

  private static void TestZipAttacks()
  {
    using TemporaryDirectory directory = new();
    string traversal = Path.Combine(directory.Path, "traversal.zip");
    using (ZipArchive archive = ZipFile.Open(traversal, ZipArchiveMode.Create))
      archive.CreateEntry("../escape.txt");
    Throws(() => PackageInstaller.ExtractAndValidate(traversal, Path.Combine(directory.Path, "a"),
      Manifest(new FileInfo(traversal).Length, Hash(traversal))), "ZIP traversal was accepted.");

    string duplicate = Path.Combine(directory.Path, "duplicate.zip");
    using (ZipArchive archive = ZipFile.Open(duplicate, ZipArchiveMode.Create))
    { archive.CreateEntry("AdofaiIpc/file.txt"); archive.CreateEntry("AdofaiIpc/FILE.txt"); }
    Throws(() => PackageInstaller.ExtractAndValidate(duplicate, Path.Combine(directory.Path, "b"),
      Manifest(new FileInfo(duplicate).Length, Hash(duplicate))), "Case-insensitive duplicate path was accepted.");
  }

  private static UpdateManifest Manifest(long size, string sha) => new()
  { SchemaVersion = 1, Version = "0.3.1", PackageUrl = "https://github.com/KGH1113/adofai-ipc/releases/download/v0.3.1/AdofaiIpc.zip", PackageSize = size, Sha256 = sha };

  private static string CreateRetirementFixture(string parent)
  {
    string root = Path.Combine(parent, "Mods", "AdofaiIpc");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "Info.json"), "{\"Id\":\"AdofaiIpc\",\"Version\":\"0.4.2\"}");
    return root;
  }

  private static void TestRetirementWait()
  {
    using TemporaryDirectory temporary = new();
    string root = CreateRetirementFixture(temporary.Path);
    // UnityModManager identifies mods by Id, even when the folder was renamed.
    string mod = Path.Combine(temporary.Path, "Mods", "renamed-replay");
    Directory.CreateDirectory(mod);
    File.WriteAllText(Path.Combine(mod, "Info.json"), "{\"Id\":\"TUFReplay\",\"EntryMethod\":\"AdofaiIpc.DependencyShim.DependencyShim.Load\"}");
    Assert(!AdofaiIpc.Retirement.LegacyRetirement.Ready(root, out _), "An old consumer was accepted for retirement.");
    Throws(() => AdofaiIpc.Retirement.LegacyRetirement.Delete(root), "A still-needed IPC installation was removed.");
    Assert(Directory.Exists(root), "Failed migration did not preserve IPC.");
    File.WriteAllText(Path.Combine(mod, "Info.json"), "{\"Id\":\"TUFReplay\",\"EntryMethod\":\"TUFReplay.Bootstrap.Bootstrap.Load\"}");
    Assert(!AdofaiIpc.Retirement.LegacyRetirement.Ready(root, out _), "An incomplete SDK installation was accepted.");
  }

  private static void TestRetirementDelete()
  {
    using TemporaryDirectory temporary = new();
    string root = CreateRetirementFixture(temporary.Path);
    string mod = Path.Combine(temporary.Path, "Mods", "TUFHelperLite");
    Directory.CreateDirectory(Path.Combine(mod, "ipc"));
    File.WriteAllText(Path.Combine(mod, "Info.json"), "{\"Id\":\"TUFHelperLite\",\"AssemblyName\":\"TUFHelperLite.Launcher.dll\",\"EntryMethod\":\"TUFHelperLite.Launcher.EntryPoint.Load\"}");
    foreach (string file in new[] { "TUFHelperLite.Launcher.dll", "AdofaiIpc.Contracts.dll", "AdofaiIpc.Loader.dll", "ipc/manifest.json", "ipc/AdofaiIpc.Runtime.dll", "Settings.json", "Data.sqlite" })
      File.WriteAllText(Path.Combine(mod, file), "preserve");
    AdofaiIpc.Retirement.LegacyRetirement.Delete(root);
    Assert(!Directory.Exists(root), "IPC was not removed immediately.");
    Assert(File.ReadAllText(Path.Combine(mod, "Settings.json")) == "preserve", "Consumer settings were changed.");
    Assert(File.ReadAllText(Path.Combine(mod, "Data.sqlite")) == "preserve", "Consumer data was changed.");
    Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(temporary.Path, ".adofai-ipc-retired")).Any(), "Unlocked files remained after removal.");
  }

  private static void TestRetirementSafety()
  {
    using TemporaryDirectory temporary = new();
    string root = CreateRetirementFixture(temporary.Path);
    File.WriteAllText(Path.Combine(root, "Info.json"), "{\"Id\":\"OtherMod\",\"Version\":\"0.4.2\"}");
    Throws(() => AdofaiIpc.Retirement.LegacyRetirement.Delete(root), "Another mod identity was removed.");
    Assert(Directory.Exists(root), "Wrong-identity check changed the directory.");
    File.WriteAllText(Path.Combine(root, "Info.json"), "{\"Id\":\"AdofaiIpc\",\"Version\":\"0.4.2\"}");
    string outside = Path.Combine(temporary.Path, "outside");
    Directory.CreateDirectory(outside);
    File.WriteAllText(Path.Combine(outside, "keep"), "keep");
    Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
    Throws(() => AdofaiIpc.Retirement.LegacyRetirement.Delete(root), "An installation with a link was removed.");
    Assert(File.Exists(Path.Combine(outside, "keep")), "A linked target was changed.");
    Directory.Delete(Path.Combine(root, "linked"));
    Directory.CreateSymbolicLink(Path.Combine(temporary.Path, ".adofai-ipc-retired"), outside);
    Throws(() => AdofaiIpc.Retirement.LegacyRetirement.Delete(root), "A linked retirement destination was accepted.");
    Assert(Directory.Exists(root), "Linked destination failure detached the live installation.");
  }

  private static void TestPublishedUpdater()
  {
    string oldLauncher = Environment.GetEnvironmentVariable("ADOFAIIPC_LEGACY_LAUNCHER");
    string zip = Environment.GetEnvironmentVariable("ADOFAIIPC_RETIREMENT_PACKAGE");
    Assert(File.Exists(oldLauncher) && File.Exists(zip), "Published old launcher or new package is missing.");
    AssemblyLoadContext context = new("published-ipc-updater", isCollectible: true);
    context.Resolving += (_, name) => name.Name == "Newtonsoft.Json" ? typeof(Newtonsoft.Json.Linq.JObject).Assembly :
      name.Name == "UnityModManager" ? typeof(UnityModManagerNet.UnityModManager).Assembly : null;
    try
    {
      Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(oldLauncher));
      Assert(System.Diagnostics.FileVersionInfo.GetVersionInfo(oldLauncher).ProductVersion == "0.4.1", "The fixture is not deployed IPC 0.4.1.");
      Type manifestType = assembly.GetType("AdofaiIpc.Launcher.UpdateManifest", true);
      object manifest = Activator.CreateInstance(manifestType, nonPublic: true);
      foreach (var property in new (string Name, object Value)[] { ("SchemaVersion", 1), ("Version", "0.4.2"), ("PackageSize", new FileInfo(zip).Length), ("Sha256", Hash(zip)), ("PackageUrl", "https://github.com/KGH1113/adofai-ipc/releases/download/v0.4.2/AdofaiIpc.zip") })
        manifestType.GetProperty(property.Name).SetValue(manifest, property.Value);
      Type installer = assembly.GetType("AdofaiIpc.Launcher.PackageInstaller", true);
      installer.GetMethod("VerifyPackage").Invoke(null, new[] { zip, manifest });
      using TemporaryDirectory temporary = new();
      string root = (string)installer.GetMethod("ExtractAndValidate").Invoke(null, new[] { zip, temporary.Path, manifest });
      Assert(File.Exists(Path.Combine(root, "Runtime", "versions", "0.4.2", "AdofaiIpc.dll")), "The old validator did not accept the new runtime.");
      string installed = Path.Combine(temporary.Path, "Mods", "AdofaiIpc");
      new UpdateStateStore(installed).Save(new UpdateState { Current = "0.4.1" });
      File.WriteAllText(Path.Combine(installed, "Info.json"), "{\"Id\":\"AdofaiIpc\",\"Version\":\"0.4.1\"}");
      installer.GetMethod("InstallCandidate").Invoke(null, new[] { installed, root, manifest });
      Assert(new UpdateStateStore(installed).Load().Trial == "0.4.2", "The old updater did not stage 0.4.2.");
      Assert(AdofaiIpc.Retirement.LegacyRetirement.Ready(installed, out _), "The old root Info version incorrectly prevented retirement after auto-update.");
      AdofaiIpc.Retirement.LegacyRetirement.Delete(installed);
      Assert(!Directory.Exists(installed), "The actual staged upgrade could not retire its old root installation.");
    }
    finally { context.Unload(); }
  }

  private static void TestLoadedRemoval()
  {
    using TemporaryDirectory temporary = new();
    string root = CreateRetirementFixture(temporary.Path);
    string path = Path.Combine(root, "AdofaiIpc.Launcher.dll");
    File.Copy(Environment.GetEnvironmentVariable("ADOFAIIPC_LEGACY_LAUNCHER"), path);
    AssemblyLoadContext context = new("loaded-removal", isCollectible: true);
    try
    {
      Assembly assembly = context.LoadFromAssemblyPath(path);
      MethodInfo parse = assembly.GetType("AdofaiIpc.Launcher.SemanticVersion", true).GetMethod("Parse");
      Assert(parse.Invoke(null, new object[] { "0.4.2" }) != null, "The deployed code did not initialize.");
      AdofaiIpc.Retirement.LegacyRetirement.Delete(root);
      Assert(!Directory.Exists(root), "The active mod folder survived removal with an assembly loaded.");
      Assert(parse.Invoke(null, new object[] { "1.0.0" }) != null, "Loaded code stopped executing after its directory was removed.");
    }
    finally { context.Unload(); }
  }

  private static string Hash(string path)
  { using SHA256 sha = SHA256.Create(); using FileStream stream = File.OpenRead(path); return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2"))); }
  private static void Run(string name, Action test) { test(); _passed++; Console.WriteLine("PASS " + name); }
  private static void Throws(Action action, string message) { try { action(); } catch { return; } throw new InvalidOperationException(message); }
  private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

  private sealed class TemporaryDirectory : IDisposable
  {
    public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "adofai-ipc-update-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
    public string Path { get; }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
  }
}
