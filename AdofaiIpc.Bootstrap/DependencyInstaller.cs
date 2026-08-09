using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyInstaller
{
  private const string ModDirectoryName = "AdofaiIpc";
  internal const string DownloadUrl = "https://github.com/KGH1113/adofai-ipc/releases/latest/download/AdofaiIpc.zip";
  internal const string ChecksumUrl = "https://github.com/KGH1113/adofai-ipc/releases/latest/download/AdofaiIpc.zip.sha256";
  internal const string ReleasePageUrl = "https://github.com/KGH1113/adofai-ipc/releases/latest";
  private const int MaximumArchiveBytes = 32 * 1024 * 1024;
  private const int MaximumEntries = 128;
  private const long MaximumExpandedBytes = 64L * 1024 * 1024;
  private static readonly Regex ChecksumPattern = new("^[0-9a-fA-F]{64}", RegexOptions.Compiled);

  public static async Task InstallAsync(UnityModManager.ModEntry owner)
  {
    string modsPath = Path.GetFullPath(UnityModManager.modsPath);
    string targetPath = Path.Combine(modsPath, ModDirectoryName);
    if (Directory.Exists(targetPath))
    {
      if (HasRequiredFiles(targetPath)) return;
      throw new IOException($"An incomplete {ModDirectoryName} directory already exists: {targetPath}");
    }

    string stagingRoot = Path.Combine(modsPath, $".{ModDirectoryName}-install-{Guid.NewGuid():N}");
    try
    {
      Directory.CreateDirectory(stagingRoot);
      string packagePath = Path.Combine(stagingRoot, "AdofaiIpc.zip");
      string checksum;
      using (HttpClient client = new())
      {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
          $"AdofaiIpc.Bootstrap/{typeof(DependencyInstaller).Assembly.GetName().Version}");
        await DownloadAsync(client, packagePath).ConfigureAwait(false);
        checksum = await client.GetStringAsync(ChecksumUrl).ConfigureAwait(false);
      }
      VerifyChecksum(packagePath, checksum);
      ExtractArchive(packagePath, stagingRoot);

      string stagedModPath = Path.Combine(stagingRoot, ModDirectoryName);
      if (!HasRequiredFiles(stagedModPath))
        throw new InvalidDataException("The AdofaiIpc package does not contain the expected files.");

      try
      {
        Directory.Move(stagedModPath, targetPath);
      }
      catch (IOException) when (HasRequiredFiles(targetPath))
      {
        // Another dependent mod completed the same installation first.
      }
    }
    finally
    {
      if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
    }
  }

  private static async Task DownloadAsync(HttpClient client, string path)
  {
    using HttpResponseMessage response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead)
      .ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaximumArchiveBytes))
      throw new InvalidDataException("The AdofaiIpc package exceeds the size limit.");
    using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    byte[] buffer = new byte[81920];
    int read;
    long total = 0;
    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
    {
      total += read;
      if (total > MaximumArchiveBytes) throw new InvalidDataException("The AdofaiIpc package exceeds the size limit.");
      await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
    }
    if (total == 0) throw new InvalidDataException("The AdofaiIpc package is empty.");
  }

  private static void VerifyChecksum(string archivePath, string checksumFile)
  {
    Match match = ChecksumPattern.Match(checksumFile.Trim());
    if (!match.Success) throw new InvalidDataException("The AdofaiIpc checksum file is invalid.");

    using SHA256 sha256 = SHA256.Create();
    using FileStream archive = File.OpenRead(archivePath);
    string actual = BitConverter.ToString(sha256.ComputeHash(archive)).Replace("-", string.Empty);
    if (!actual.Equals(match.Value, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("The AdofaiIpc package checksum does not match.");
  }

  private static void ExtractArchive(string archivePath, string destination)
  {
    string root = EnsureTrailingSeparator(Path.GetFullPath(destination));
    using FileStream stream = File.OpenRead(archivePath);
    using ZipArchive archive = new(stream, ZipArchiveMode.Read, false);

    if (archive.Entries.Count > MaximumEntries)
      throw new InvalidDataException("The AdofaiIpc package contains too many files.");
    long expandedBytes = 0;
    HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      expandedBytes = checked(expandedBytes + entry.Length);
      if (expandedBytes > MaximumExpandedBytes)
        throw new InvalidDataException("The AdofaiIpc package exceeds the expanded size limit.");
      string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
      if (!path.StartsWith(root, StringComparison.Ordinal) || !paths.Add(path))
        throw new InvalidDataException($"Unsafe archive entry: {entry.FullName}");
      if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
        throw new InvalidDataException($"Symbolic links are not allowed: {entry.FullName}");

      if (string.IsNullOrEmpty(entry.Name))
      {
        Directory.CreateDirectory(path);
        continue;
      }

      string directory = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
      using Stream source = entry.Open();
      using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      source.CopyTo(target);
    }
  }

  private static bool HasRequiredFiles(string path)
  {
    string infoPath = Path.Combine(path, "Info.json");
    if (!File.Exists(infoPath) || !File.Exists(Path.Combine(path, "AdofaiIpc.Shim.dll")) ||
        !File.Exists(Path.Combine(path, "Update", "state.json"))) return false;
    try
    {
      JObject info = JObject.Parse(File.ReadAllText(infoPath));
      string version = (string)info["Version"];
      return !string.IsNullOrWhiteSpace(version) &&
             File.Exists(Path.Combine(path, "Launcher", "versions", version, "AdofaiIpc.Launcher.dll")) &&
             File.Exists(Path.Combine(path, "Runtime", "versions", version, "AdofaiIpc.dll"));
    }
    catch { return false; }
  }

  private static string EnsureTrailingSeparator(string path)
  {
    return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;
  }
}
