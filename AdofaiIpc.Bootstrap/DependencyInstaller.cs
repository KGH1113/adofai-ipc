using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    byte[] archive;
    string checksum;

    using (HttpClient client = new())
    {
      client.Timeout = TimeSpan.FromSeconds(30);
      client.DefaultRequestHeaders.UserAgent.ParseAdd(
        $"AdofaiIpc.Bootstrap/{typeof(DependencyInstaller).Assembly.GetName().Version}");

      archive = await client.GetByteArrayAsync(DownloadUrl).ConfigureAwait(false);
      if (archive.Length > MaximumArchiveBytes)
        throw new InvalidDataException("The AdofaiIpc package exceeds the size limit.");
      checksum = await client.GetStringAsync(ChecksumUrl).ConfigureAwait(false);
    }

    VerifyChecksum(archive, checksum);

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
      ExtractArchive(archive, stagingRoot);

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

  private static void VerifyChecksum(byte[] archive, string checksumFile)
  {
    Match match = ChecksumPattern.Match(checksumFile.Trim());
    if (!match.Success) throw new InvalidDataException("The AdofaiIpc checksum file is invalid.");

    using SHA256 sha256 = SHA256.Create();
    string actual = BitConverter.ToString(sha256.ComputeHash(archive)).Replace("-", string.Empty);
    if (!actual.Equals(match.Value, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("The AdofaiIpc package checksum does not match.");
  }

  private static void ExtractArchive(byte[] archiveBytes, string destination)
  {
    string root = EnsureTrailingSeparator(Path.GetFullPath(destination));
    using MemoryStream stream = new(archiveBytes, false);
    using ZipArchive archive = new(stream, ZipArchiveMode.Read, false);

    if (archive.Entries.Count > MaximumEntries)
      throw new InvalidDataException("The AdofaiIpc package contains too many files.");
    long expandedBytes = 0;

    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      expandedBytes = checked(expandedBytes + entry.Length);
      if (expandedBytes > MaximumExpandedBytes)
        throw new InvalidDataException("The AdofaiIpc package exceeds the expanded size limit.");
      string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
      if (!path.StartsWith(root, StringComparison.Ordinal))
        throw new InvalidDataException($"Unsafe archive entry: {entry.FullName}");

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
    return File.Exists(Path.Combine(path, "Info.json")) &&
           File.Exists(Path.Combine(path, "AdofaiIpc.dll"));
  }

  private static string EnsureTrailingSeparator(string path)
  {
    return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;
  }
}
