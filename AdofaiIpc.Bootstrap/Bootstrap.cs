using System;
using System.Threading;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

public static class Bootstrap
{
  private static readonly object Sync = new();
  private static Task _dependencyTask;

  public static bool Load(UnityModManager.ModEntry modEntry)
  {
    string displayName = modEntry.Info.DisplayName;
    SynchronizationContext mainThread = SynchronizationContext.Current;
    modEntry.Info.DisplayName = Status(modEntry, "Checking AdofaiIpc...");
    _ = LoadAsync(modEntry, displayName, mainThread);
    return true;
  }

  private static async Task LoadAsync(
    UnityModManager.ModEntry modEntry,
    string displayName,
    SynchronizationContext mainThread)
  {
    try
    {
      BootstrapManifest manifest = BootstrapManifest.Load(modEntry.Path);
      UnityModManager.ModEntry dependency = ModActivator.Find();
      ModActivator.EnsureVersion(dependency, manifest.MinimumAdofaiIpcVersion);

      if (dependency != null && !dependency.Enabled)
        throw new InvalidOperationException("AdofaiIpc is installed but disabled.");

      if (!ModActivator.IsLoaded())
      {
        await EnsureDependencyAsync(modEntry, manifest).ConfigureAwait(false);
        modEntry.Info.DisplayName = Status(modEntry, "Loading AdofaiIpc...");
        await RunOnMainThread(mainThread, () => ModActivator.EnsureActive(
          manifest.MinimumAdofaiIpcVersion)).ConfigureAwait(false);
      }

      modEntry.Info.DisplayName = displayName;
      await RunOnMainThread(mainThread, () => DependencyModLoader.Load(modEntry, manifest))
        .ConfigureAwait(false);
    }
    catch (Exception exception)
    {
      modEntry.Info.DisplayName = Status(modEntry, "AdofaiIpc Error");
      modEntry.Logger.Error(exception.ToString());
    }
  }

  private static Task EnsureDependencyAsync(
    UnityModManager.ModEntry owner,
    BootstrapManifest manifest)
  {
    lock (Sync)
    {
      if (_dependencyTask == null || _dependencyTask.IsFaulted || _dependencyTask.IsCanceled)
        _dependencyTask = EnsureDependencyCoreAsync(owner, manifest);
      return _dependencyTask;
    }
  }

  private static async Task EnsureDependencyCoreAsync(
    UnityModManager.ModEntry owner,
    BootstrapManifest manifest)
  {
    UnityModManager.ModEntry dependency = ModActivator.Find();
    if (dependency != null || ModActivator.IsLoaded()) return;

    string installedInfo = System.IO.Path.Combine(
      UnityModManager.modsPath,
      "AdofaiIpc",
      "Info.json");
    if (System.IO.File.Exists(installedInfo)) return;

    await DependencyInstaller.InstallAsync(owner, manifest).ConfigureAwait(false);
  }

  private static Task RunOnMainThread(SynchronizationContext context, Action action)
  {
    if (context == null || SynchronizationContext.Current == context)
    {
      action();
      return Task.CompletedTask;
    }

    TaskCompletionSource<bool> completion = new();
    context.Post(_ =>
    {
      try
      {
        action();
        completion.SetResult(true);
      }
      catch (Exception exception)
      {
        completion.SetException(exception);
      }
    }, null);
    return completion.Task;
  }

  private static string Status(UnityModManager.ModEntry modEntry, string status)
  {
    return $"{modEntry.Info.Id} <color=grey>[{status}]</color>";
  }
}
