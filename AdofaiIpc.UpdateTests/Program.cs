using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AdofaiIpc.Launcher;

internal static class Program
{
  private static int _passed;

  private static void Main()
  {
    Run("semantic version ordering", TestSemanticVersions);
    Run("stable manifest validation", TestManifest);
    Run("atomic state and backup recovery", TestStateStore);
    Run("package validation and candidate staging", TestPackage);
    Run("unsafe archive rejection", TestUnsafeArchive);
    Console.WriteLine("Passed " + _passed + " AdofaiIpc update tests.");
  }

  private static void TestSemanticVersions()
  {
    Assert(SemanticVersion.Parse("0.3.0").CompareTo(SemanticVersion.Parse("0.3.0-beta.2")) > 0,
      "Stable must sort after prerelease.");
    Assert(SemanticVersion.Parse("0.4.0").CompareTo(SemanticVersion.Parse("0.3.9")) > 0,
      "Newer stable version was not selected.");
    Assert(SemanticVersion.Parse("0.3.0+build.1").CompareTo(SemanticVersion.Parse("0.3.0")) == 0,
      "Build metadata changed precedence.");
  }

  private static void TestManifest()
  {
    UpdateManifest valid = Manifest("0.3.0", 10, new string('a', 64));
    Assert(valid.Version == "0.3.0", "Manifest version was not parsed.");
    AssertThrows<InvalidDataException>(() => Manifest("0.3.0-beta.1", 10, new string('a', 64)));
    AssertThrows<InvalidDataException>(() => UpdateManifest.Parse(
      Json("0.3.0", "https://example.com/AdofaiIpc.zip", 10, new string('a', 64))));
  }

  private static void TestStateStore()
  {
    using TemporaryDirectory temporary = new();
    string update = Path.Combine(temporary.Path, "Update");
    Directory.CreateDirectory(update);
    File.WriteAllText(Path.Combine(update, "state.json"),
      "{\"SchemaVersion\":1,\"Current\":\"0.2.0\",\"Previous\":null,\"Trial\":null}");
    UpdateStateStore store = new(temporary.Path);
    UpdateState state = store.Load();
    state.Trial = "0.3.0";
    store.Save(state);
    Assert(store.Load().Trial == "0.3.0", "Trial state was not saved.");
    Assert(File.Exists(Path.Combine(update, "state.json.bak")), "Atomic state backup was not retained.");
  }

  private static void TestPackage()
  {
    using TemporaryDirectory temporary = new();
    string package = Path.Combine(temporary.Path, "package.zip");
    CreatePackage(package, "0.3.0", unsafeEntry: false);
    UpdateManifest manifest = Manifest("0.3.0", new FileInfo(package).Length, Sha256(package));
    PackageInstaller.VerifyPackage(package, manifest);
    string install = Path.Combine(temporary.Path, "installed");
    Directory.CreateDirectory(Path.Combine(install, "Update"));
    File.WriteAllText(Path.Combine(install, "Update", "state.json"),
      "{\"SchemaVersion\":1,\"Current\":\"0.2.0\",\"Previous\":null,\"Trial\":null}");
    string staging = Path.Combine(temporary.Path, "staging");
    string root = PackageInstaller.ExtractAndValidate(package, staging, manifest);
    PackageInstaller.InstallCandidate(install, root, manifest);
    Assert(File.Exists(Path.Combine(install, "Runtime", "versions", "0.3.0", "AdofaiIpc.dll")),
      "Runtime candidate was not installed.");
    Assert(new UpdateStateStore(install).Load().Trial == "0.3.0", "Candidate was not marked as trial.");
  }

  private static void TestUnsafeArchive()
  {
    using TemporaryDirectory temporary = new();
    string package = Path.Combine(temporary.Path, "unsafe.zip");
    CreatePackage(package, "0.3.0", unsafeEntry: true);
    UpdateManifest manifest = Manifest("0.3.0", new FileInfo(package).Length, Sha256(package));
    AssertThrows<InvalidDataException>(() =>
      PackageInstaller.ExtractAndValidate(package, Path.Combine(temporary.Path, "staging"), manifest));
    Assert(!File.Exists(Path.Combine(temporary.Path, "escape.txt")), "Unsafe ZIP entry escaped staging.");
  }

  private static void CreatePackage(string path, string version, bool unsafeEntry)
  {
    using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
    Add(archive, "AdofaiIpc/Info.json", "{\"Id\":\"AdofaiIpc\",\"Version\":\"" + version + "\"}");
    Add(archive, "AdofaiIpc/AdofaiIpc.Shim.dll", "shim");
    Add(archive, "AdofaiIpc/Launcher/versions/" + version + "/AdofaiIpc.Launcher.dll", "launcher");
    Add(archive, "AdofaiIpc/Runtime/versions/" + version + "/AdofaiIpc.dll", "runtime");
    if (unsafeEntry) Add(archive, "../escape.txt", "unsafe");
  }

  private static void Add(ZipArchive archive, string path, string content)
  {
    using StreamWriter writer = new(archive.CreateEntry(path).Open(), Encoding.UTF8);
    writer.Write(content);
  }

  private static UpdateManifest Manifest(string version, long size, string sha) =>
    UpdateManifest.Parse(Json(version,
      "https://github.com/KGH1113/adofai-ipc/releases/download/v" + version + "/AdofaiIpc.zip", size, sha));

  private static string Json(string version, string url, long size, string sha) =>
    "{\"SchemaVersion\":1,\"Version\":\"" + version + "\",\"PackageUrl\":\"" + url +
    "\",\"PackageSize\":" + size + ",\"Sha256\":\"" + sha + "\"}";

  private static string Sha256(string path)
  {
    using FileStream stream = File.OpenRead(path);
    return string.Concat(SHA256.HashData(stream).Select(value => value.ToString("x2")));
  }

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

  private static void AssertThrows<T>(Action action) where T : Exception
  {
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
  }

  private sealed class TemporaryDirectory : IDisposable
  {
    public TemporaryDirectory()
    {
      Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AdofaiIpc-tests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
  }
}
