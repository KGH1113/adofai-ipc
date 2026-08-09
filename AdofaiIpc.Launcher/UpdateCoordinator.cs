using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace AdofaiIpc.Launcher;

internal static class UpdateCoordinator
{
  internal const string ManifestUrl = "https://github.com/KGH1113/adofai-ipc/releases/latest/download/AdofaiIpc.update.json";

  public static string TryStageLatest(UnityModManager.ModEntry owner, string currentVersion)
  {
    string lockPath = Path.Combine(owner.Path, "Update", "update.lock");
    Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
    try
    {
      using FileStream updateLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
      CleanupPending(owner.Path);
      UpdateState state = new UpdateStateStore(owner.Path).Load();
      SemanticVersion installed = SemanticVersion.Parse(state.Current);
      SemanticVersion running = SemanticVersion.Parse(currentVersion);
      SemanticVersion effective = running.CompareTo(installed) > 0 ? running : installed;
      using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
      client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiIpc-Launcher/" + currentVersion);
      UpdateManifest manifest = UpdateManifest.Parse(
        client.GetStringAsync(ManifestUrl).ConfigureAwait(false).GetAwaiter().GetResult());
      if (effective.CompareTo(SemanticVersion.Parse(manifest.Version)) >= 0) return null;

      string staging = Path.Combine(owner.Path, "Update", "pending-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(staging);
      try
      {
        string packagePath = Path.Combine(staging, "AdofaiIpc.zip");
        DownloadAsync(client, manifest.PackageUrl, packagePath, manifest.PackageSize)
          .ConfigureAwait(false).GetAwaiter().GetResult();
        PackageInstaller.VerifyPackage(packagePath, manifest);
        string packageRoot = PackageInstaller.ExtractAndValidate(packagePath, staging, manifest);
        PackageInstaller.InstallCandidate(owner.Path, packageRoot, manifest);
        return manifest.Version;
      }
      finally { TryDelete(staging); }
    }
    catch (Exception exception)
    {
      owner.Logger.Warning("[AutoUpdate] Update check failed; using the installed version: " + exception.Message);
      return null;
    }
  }

  public static void Commit(UnityModManager.ModEntry owner, string version)
  {
    UpdateStateStore store = new(owner.Path);
    UpdateState state = store.Load();
    if (state.Trial != version) return;
    state.Previous = state.Current;
    state.Current = version;
    state.Trial = null;
    store.Save(state);
  }

  public static void Discard(UnityModManager.ModEntry owner, string version)
  {
    UpdateStateStore store = new(owner.Path);
    UpdateState state = store.Load();
    if (state.Trial != version) return;
    state.Trial = null;
    store.Save(state);
  }

  private static async Task DownloadAsync(HttpClient client, string url, string path, long expected)
  {
    using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    byte[] buffer = new byte[81920];
    long total = 0;
    int read;
    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
    {
      total += read;
      if (total > expected || total > PackageInstaller.MaximumPackageBytes)
        throw new InvalidDataException("AdofaiIpc package exceeds its declared size.");
      await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
    }
    if (total != expected) throw new InvalidDataException("AdofaiIpc package size does not match.");
  }

  private static void CleanupPending(string installPath)
  {
    string update = Path.Combine(installPath, "Update");
    if (!Directory.Exists(update)) return;
    foreach (string path in Directory.GetDirectories(update, "pending-*")) TryDelete(path);
  }

  private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
