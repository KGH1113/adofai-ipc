using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
