using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Launcher;

internal static class PackageInstaller
{
  public const long MaximumPackageBytes = 64L * 1024 * 1024;
  private const long MaximumExtractedBytes = 192L * 1024 * 1024;
  private const long MaximumEntryBytes = 64L * 1024 * 1024;
  private const int MaximumEntries = 512;

  public static void VerifyPackage(string packagePath, UpdateManifest manifest)
  {
    FileInfo info = new(packagePath);
    if (info.Length != manifest.PackageSize) throw new InvalidDataException("AdofaiIpc package size does not match.");
    using SHA256 sha256 = SHA256.Create();
    using FileStream stream = File.OpenRead(packagePath);
    string actual = string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
    if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package checksum does not match.");
  }

  public static string ExtractAndValidate(string packagePath, string stagingRoot, UpdateManifest manifest)
  {
    string extractionRoot = Path.Combine(stagingRoot, "extract");
    Directory.CreateDirectory(extractionRoot);
    string fullRoot = EnsureSeparator(Path.GetFullPath(extractionRoot));
    long total = 0;
    int count = 0;
    using ZipArchive archive = ZipFile.OpenRead(packagePath);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      if (++count > MaximumEntries) throw new InvalidDataException("AdofaiIpc package contains too many entries.");
      if (entry.Length > MaximumEntryBytes || total > MaximumExtractedBytes - entry.Length)
        throw new InvalidDataException("AdofaiIpc package expands beyond the allowed size.");
      total += entry.Length;
      string target = Path.GetFullPath(Path.Combine(fullRoot, entry.FullName));
      if (!target.StartsWith(fullRoot, StringComparison.Ordinal))
        throw new InvalidDataException("Unsafe AdofaiIpc archive entry: " + entry.FullName);
      if (string.IsNullOrEmpty(entry.Name))
      {
        Directory.CreateDirectory(target);
        continue;
      }
      Directory.CreateDirectory(Path.GetDirectoryName(target));
      using Stream source = entry.Open();
      using FileStream destination = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      CopyEntry(source, destination, entry.Length);
    }

    string modRoot = Path.Combine(extractionRoot, "AdofaiIpc");
    string infoPath = Path.Combine(modRoot, "Info.json");
    string launcher = Path.Combine(modRoot, "Launcher", "versions", manifest.Version, "AdofaiIpc.Launcher.dll");
    string runtime = Path.Combine(modRoot, "Runtime", "versions", manifest.Version, "AdofaiIpc.dll");
    if (!File.Exists(Path.Combine(modRoot, "AdofaiIpc.Shim.dll")) || !File.Exists(infoPath) ||
        !File.Exists(launcher) || !File.Exists(runtime))
      throw new InvalidDataException("AdofaiIpc package is missing required files.");
    JObject info = JObject.Parse(File.ReadAllText(infoPath));
    if (!string.Equals((string)info["Id"], "AdofaiIpc", StringComparison.Ordinal) ||
        !string.Equals((string)info["Version"], manifest.Version, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package metadata does not match the update manifest.");
    return modRoot;
  }

  public static void InstallCandidate(string installPath, string packageRoot, UpdateManifest manifest)
  {
    string sourceLauncher = Path.Combine(packageRoot, "Launcher", "versions", manifest.Version);
    string sourceRuntime = Path.Combine(packageRoot, "Runtime", "versions", manifest.Version);
    string targetLauncher = Path.Combine(installPath, "Launcher", "versions", manifest.Version);
    string targetRuntime = Path.Combine(installPath, "Runtime", "versions", manifest.Version);
    MoveIfMissing(sourceLauncher, targetLauncher, "AdofaiIpc.Launcher.dll");
    MoveIfMissing(sourceRuntime, targetRuntime, "AdofaiIpc.dll");
    UpdateStateStore store = new(installPath);
    UpdateState state = store.Load();
    if (!SemanticVersion.TryParse(state.Current, out SemanticVersion current) ||
        current.CompareTo(SemanticVersion.Parse(manifest.Version)) >= 0) return;
    state.Trial = manifest.Version;
    store.Save(state);
  }

  private static void MoveIfMissing(string source, string target, string requiredFile)
  {
    if (Directory.Exists(target))
    {
      if (!File.Exists(Path.Combine(target, requiredFile)))
        throw new InvalidDataException("An incomplete AdofaiIpc version directory already exists: " + target);
      return;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(target));
    Directory.Move(source, target);
  }

  private static void CopyEntry(Stream source, Stream destination, long expectedBytes)
  {
    byte[] buffer = new byte[81920];
    long total = 0;
    while (true)
    {
      int read = source.Read(buffer, 0, buffer.Length);
      if (read == 0) break;
      total += read;
      if (total > expectedBytes || total > MaximumEntryBytes)
        throw new InvalidDataException("AdofaiIpc archive entry exceeds its declared size.");
      destination.Write(buffer, 0, read);
    }
    if (total != expectedBytes) throw new InvalidDataException("AdofaiIpc archive entry size does not match.");
  }

  private static string EnsureSeparator(string path) =>
    path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;
}
