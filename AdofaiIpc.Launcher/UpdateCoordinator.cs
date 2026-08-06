using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace AdofaiIpc.Launcher;

internal static class UpdateCoordinator
{
  private const string ManifestUrl =
    "https://github.com/KGH1113/adofai-ipc/releases/latest/download/AdofaiIpc.update.json";
  private static readonly object Sync = new();
  private static Task _operation;

  public static void Schedule(UnityModManager.ModEntry modEntry, string currentVersion)
  {
    lock (Sync)
    {
      if (_operation == null) _operation = Task.Run(() => RunAsync(modEntry, currentVersion));
    }
  }

  private static async Task RunAsync(UnityModManager.ModEntry modEntry, string currentVersion)
  {
    string lockPath = Path.Combine(modEntry.Path, "Update", "update.lock");
    Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
    try
    {
      using FileStream updateLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
      CleanupPending(modEntry.Path);
      using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
      client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiIpc-Launcher/0.3.0");
      string manifestJson = await client.GetStringAsync(ManifestUrl).ConfigureAwait(false);
      UpdateManifest manifest = UpdateManifest.Parse(manifestJson);
      if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current) ||
          current.CompareTo(SemanticVersion.Parse(manifest.Version)) >= 0) return;

      string staging = Path.Combine(modEntry.Path, "Update", "pending-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(staging);
      try
      {
        string packagePath = Path.Combine(staging, "AdofaiIpc.zip");
        await DownloadAsync(client, manifest.PackageUrl, packagePath, manifest.PackageSize).ConfigureAwait(false);
        PackageInstaller.VerifyPackage(packagePath, manifest);
        string packageRoot = PackageInstaller.ExtractAndValidate(packagePath, staging, manifest);
        PackageInstaller.InstallCandidate(modEntry.Path, packageRoot, manifest);
        modEntry.Logger.Log("[AutoUpdate] AdofaiIpc " + manifest.Version + " is ready for the next game launch.");
      }
      finally
      {
        TryDelete(staging);
      }
    }
    catch (IOException exception)
    {
      modEntry.Logger.Warning("[AutoUpdate] Another updater is active or the update files are unavailable: " + exception.Message);
    }
    catch (Exception exception)
    {
      modEntry.Logger.Warning("[AutoUpdate] AdofaiIpc update failed; the current runtime remains active: " + exception);
    }
  }

  private static async Task DownloadAsync(HttpClient client, string url, string path, long expectedBytes)
  {
    using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    byte[] buffer = new byte[81920];
    long total = 0;
    while (true)
    {
      int read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
      if (read == 0) break;
      total += read;
      if (total > expectedBytes || total > PackageInstaller.MaximumPackageBytes)
        throw new InvalidDataException("AdofaiIpc package exceeds its declared size.");
      await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
    }
    if (total != expectedBytes) throw new InvalidDataException("AdofaiIpc package size does not match.");
  }

  private static void TryDelete(string path)
  {
    try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
  }

  private static void CleanupPending(string installPath)
  {
    string updateRoot = Path.Combine(installPath, "Update");
    if (!Directory.Exists(updateRoot)) return;
    foreach (string directory in Directory.GetDirectories(updateRoot, "pending-*")) TryDelete(directory);
  }
}
