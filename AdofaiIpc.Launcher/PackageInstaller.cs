using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Launcher;

internal static class PackageInstaller
{
  public const long MaximumPackageBytes = 64L * 1024 * 1024;
  private const long MaximumExpandedBytes = 192L * 1024 * 1024;
  private const long MaximumEntryBytes = 64L * 1024 * 1024;
  private const int MaximumEntries = 512;

  public static void VerifyPackage(string packagePath, UpdateManifest manifest)
  {
    if (new FileInfo(packagePath).Length != manifest.PackageSize)
      throw new InvalidDataException("AdofaiIpc package size does not match.");
    using SHA256 sha = SHA256.Create();
    using FileStream stream = File.OpenRead(packagePath);
    string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package checksum does not match.");
  }

  public static string ExtractAndValidate(string packagePath, string stagingRoot, UpdateManifest manifest)
  {
    string extractRoot = Path.Combine(stagingRoot, "extract");
    Directory.CreateDirectory(extractRoot);
    string root = EnsureSeparator(Path.GetFullPath(extractRoot));
    long expanded = 0;
    int count = 0;
    HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

    using ZipArchive archive = ZipFile.OpenRead(packagePath);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      if (++count > MaximumEntries) throw new InvalidDataException("AdofaiIpc package contains too many entries.");
      if (entry.Length > MaximumEntryBytes || expanded > MaximumExpandedBytes - entry.Length)
        throw new InvalidDataException("AdofaiIpc package expands beyond the allowed size.");
      expanded += entry.Length;
      string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
      if (!path.StartsWith(root, StringComparison.Ordinal) || !paths.Add(path))
        throw new InvalidDataException("Unsafe or duplicate archive entry: " + entry.FullName);
      if ((entry.ExternalAttributes & 0xF0000000) == 0xA0000000)
        throw new InvalidDataException("Symbolic links are not allowed in the package.");
      if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      using Stream source = entry.Open();
      using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      CopyEntry(source, target, entry.Length);
    }

    string packageRoot = Path.Combine(extractRoot, "AdofaiIpc");
    string infoPath = Path.Combine(packageRoot, "Info.json");
    string shimPath = Path.Combine(packageRoot, "AdofaiIpc.Shim.dll");
    string launcherPath = Path.Combine(packageRoot, "Launcher", "versions", manifest.Version, "AdofaiIpc.Launcher.dll");
    string runtimePath = Path.Combine(packageRoot, "Runtime", "versions", manifest.Version, "AdofaiIpc.dll");
    if (!File.Exists(infoPath) || !File.Exists(shimPath) || !File.Exists(launcherPath) || !File.Exists(runtimePath))
      throw new InvalidDataException("AdofaiIpc package is missing required files.");
    JObject info = JObject.Parse(File.ReadAllText(infoPath));
    if ((string)info["Id"] != "AdofaiIpc" || (string)info["Version"] != manifest.Version ||
        (string)info["AssemblyName"] != "AdofaiIpc.Shim.dll" ||
        (string)info["EntryMethod"] != "AdofaiIpc.Shim.Shim.Load")
      throw new InvalidDataException("AdofaiIpc package metadata does not match the manifest.");
    ValidateAssembly(launcherPath, "AdofaiIpc.Launcher", manifest.Version);
    ValidateAssembly(runtimePath, "AdofaiIpc", manifest.Version);
    return packageRoot;
  }

  public static void InstallCandidate(string installPath, string packageRoot, UpdateManifest manifest)
  {
    MoveIfMissing(Path.Combine(packageRoot, "Launcher", "versions", manifest.Version),
      Path.Combine(installPath, "Launcher", "versions", manifest.Version), "AdofaiIpc.Launcher.dll");
    MoveIfMissing(Path.Combine(packageRoot, "Runtime", "versions", manifest.Version),
      Path.Combine(installPath, "Runtime", "versions", manifest.Version), "AdofaiIpc.dll");
    UpdateStateStore store = new(installPath);
    UpdateState state = store.Load();
    if (SemanticVersion.Parse(state.Current).CompareTo(SemanticVersion.Parse(manifest.Version)) < 0)
    { state.Trial = manifest.Version; store.Save(state); }
  }

  private static void ValidateAssembly(string path, string name, string version)
  {
    if (AssemblyName.GetAssemblyName(path).Name != name ||
        FileVersionInfo.GetVersionInfo(path).ProductVersion != version)
      throw new InvalidDataException(name + " version does not match the manifest.");
  }

  private static void MoveIfMissing(string source, string target, string requiredFile)
  {
    if (Directory.Exists(target))
    {
      if (!File.Exists(Path.Combine(target, requiredFile)))
        throw new InvalidDataException("An incomplete version directory exists: " + target);
      return;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(target));
    Directory.Move(source, target);
  }

  private static void CopyEntry(Stream source, Stream destination, long expected)
  {
    byte[] buffer = new byte[81920];
    long total = 0;
    int read;
    while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
    {
      total += read;
      if (total > expected || total > MaximumEntryBytes)
        throw new InvalidDataException("Archive entry exceeds its declared size.");
      destination.Write(buffer, 0, read);
    }
    if (total != expected) throw new InvalidDataException("Archive entry size does not match.");
  }

  private static string EnsureSeparator(string path) =>
    path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
}
