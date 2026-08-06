using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyInstaller
{
  private const string ModDirectoryName = "AdofaiIpc";
  private const long MaximumPackageBytes = 64L * 1024 * 1024;
  private const long MaximumExtractedBytes = 192L * 1024 * 1024;
  private const int MaximumEntries = 512;
  private static readonly Regex ShaPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

  public static async Task InstallAsync(UnityModManager.ModEntry owner, BootstrapManifest bootstrap)
  {
    string modsPath = Path.GetFullPath(UnityModManager.modsPath);
    string targetPath = Path.Combine(modsPath, ModDirectoryName);
    string lockPath = Path.Combine(modsPath, ".AdofaiIpc-install.lock");
    using FileStream installLock = await AcquireLockAsync(lockPath).ConfigureAwait(false);
    foreach (string pending in Directory.GetDirectories(modsPath, ".AdofaiIpc-install-*")) TryDelete(pending);
    if (HasRequiredFiles(targetPath)) return;
    if (Directory.Exists(targetPath))
      throw new IOException("An incomplete AdofaiIpc directory already exists: " + targetPath);

    string stagingRoot = Path.Combine(modsPath, ".AdofaiIpc-install-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stagingRoot);
    try
    {
      using HttpClient client = CreateClient();
      owner.Info.DisplayName = Status(owner, "Checking AdofaiIpc...");
      string json = await client.GetStringAsync(bootstrap.UpdateManifestUrl).ConfigureAwait(false);
      ReleaseManifest manifest = ReleaseManifest.Parse(json);
      string packagePath = Path.Combine(stagingRoot, "AdofaiIpc.zip");
      owner.Info.DisplayName = Status(owner, "Downloading AdofaiIpc...");
      await DownloadAsync(client, manifest.PackageUrl, packagePath, manifest.PackageSize).ConfigureAwait(false);
      VerifyPackage(packagePath, manifest);
      string stagedModPath = ExtractAndValidate(packagePath, stagingRoot, manifest);
      owner.Info.DisplayName = Status(owner, "Installing AdofaiIpc...");
      try
      {
        Directory.Move(stagedModPath, targetPath);
      }
      catch (IOException) when (HasRequiredFiles(targetPath))
      {
        // Another bootstrap completed the same installation.
      }
    }
    finally
    {
      TryDelete(stagingRoot);
    }
  }

  private static async Task<FileStream> AcquireLockAsync(string path)
  {
    for (int attempt = 0; attempt < 120; attempt++)
    {
      try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
      catch (IOException) when (attempt < 119) { await Task.Delay(250).ConfigureAwait(false); }
    }
    throw new IOException("Timed out waiting for the AdofaiIpc installation lock.");
  }

  private static HttpClient CreateClient()
  {
    HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
      "AdofaiIpc.Bootstrap/" + typeof(DependencyInstaller).Assembly.GetName().Version);
    return client;
  }

  private static async Task DownloadAsync(HttpClient client, string url, string path, long expectedBytes)
  {
    using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    byte[] buffer = new byte[81920];
    long total = 0;
    while (true)
    {
      int read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
      if (read == 0) break;
      total += read;
      if (total > expectedBytes || total > MaximumPackageBytes)
        throw new InvalidDataException("AdofaiIpc package exceeds its declared size.");
      await destination.WriteAsync(buffer, 0, read).ConfigureAwait(false);
    }
    if (total != expectedBytes) throw new InvalidDataException("AdofaiIpc package size does not match.");
  }

  private static void VerifyPackage(string path, ReleaseManifest manifest)
  {
    if (new FileInfo(path).Length != manifest.PackageSize)
      throw new InvalidDataException("AdofaiIpc package size does not match.");
    using SHA256 sha256 = SHA256.Create();
    using FileStream stream = File.OpenRead(path);
    string actual = string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
    if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package checksum does not match.");
  }

  private static string ExtractAndValidate(string packagePath, string stagingRoot, ReleaseManifest manifest)
  {
    string extractionRoot = Path.Combine(stagingRoot, "extract");
    Directory.CreateDirectory(extractionRoot);
    string root = EnsureTrailingSeparator(Path.GetFullPath(extractionRoot));
    long total = 0;
    int entries = 0;
    using ZipArchive archive = ZipFile.OpenRead(packagePath);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      if (++entries > MaximumEntries || entry.Length > MaximumPackageBytes || total > MaximumExtractedBytes - entry.Length)
        throw new InvalidDataException("AdofaiIpc package expands beyond the allowed limits.");
      total += entry.Length;
      string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
      if (!path.StartsWith(root, StringComparison.Ordinal))
        throw new InvalidDataException("Unsafe archive entry: " + entry.FullName);
      if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      using Stream source = entry.Open();
      using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      CopyEntry(source, target, entry.Length);
    }
    string modRoot = Path.Combine(extractionRoot, ModDirectoryName);
    string infoPath = Path.Combine(modRoot, "Info.json");
    if (!HasRequiredFiles(modRoot) ||
        !File.Exists(Path.Combine(modRoot, "Launcher", "versions", manifest.Version, "AdofaiIpc.Launcher.dll")) ||
        !File.Exists(Path.Combine(modRoot, "Runtime", "versions", manifest.Version, "AdofaiIpc.dll")))
      throw new InvalidDataException("The AdofaiIpc package does not contain the expected files.");
    JObject info = JObject.Parse(File.ReadAllText(infoPath));
    if (!string.Equals((string)info["Id"], ModDirectoryName, StringComparison.Ordinal) ||
        !string.Equals((string)info["Version"], manifest.Version, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package metadata does not match its manifest.");
    return modRoot;
  }

  private static bool HasRequiredFiles(string path) =>
    File.Exists(Path.Combine(path, "Info.json")) && File.Exists(Path.Combine(path, "AdofaiIpc.Shim.dll"));

  private static void CopyEntry(Stream source, Stream destination, long expectedBytes)
  {
    byte[] buffer = new byte[81920];
    long total = 0;
    while (true)
    {
      int read = source.Read(buffer, 0, buffer.Length);
      if (read == 0) break;
      total += read;
      if (total > expectedBytes || total > MaximumPackageBytes)
        throw new InvalidDataException("AdofaiIpc archive entry exceeds its declared size.");
      destination.Write(buffer, 0, read);
    }
    if (total != expectedBytes) throw new InvalidDataException("AdofaiIpc archive entry size does not match.");
  }

  private static string Status(UnityModManager.ModEntry owner, string status) =>
    owner.Info.Id + " <color=grey>[" + status + "]</color>";

  private static string EnsureTrailingSeparator(string path) =>
    path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;

  private static void TryDelete(string path)
  {
    try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
  }

  private sealed class ReleaseManifest
  {
    public int SchemaVersion { get; set; }
    public string Version { get; set; }
    public string PackageUrl { get; set; }
    public long PackageSize { get; set; }
    public string Sha256 { get; set; }

    public static ReleaseManifest Parse(string json)
    {
      ReleaseManifest manifest = JsonConvert.DeserializeObject<ReleaseManifest>(json)
        ?? throw new InvalidDataException("AdofaiIpc update manifest is invalid.");
      if (manifest.SchemaVersion != 1 || !System.Version.TryParse(manifest.Version, out _) ||
          manifest.Version.Contains("-") || manifest.PackageSize <= 0 || manifest.PackageSize > MaximumPackageBytes ||
          !ShaPattern.IsMatch(manifest.Sha256 ?? string.Empty))
        throw new InvalidDataException("AdofaiIpc update manifest contains invalid fields.");
      if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out Uri uri) ||
          uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
          !uri.AbsolutePath.StartsWith("/KGH1113/adofai-ipc/releases/", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("AdofaiIpc package URL is not an official release URL.");
      return manifest;
    }
  }
}
