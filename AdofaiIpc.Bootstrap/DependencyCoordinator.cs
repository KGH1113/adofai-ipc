using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyCoordinator
{
  private const string InstallTaskKey = "AdofaiIpc.DependencyInstallTask.v1";

  public static async Task RunAsync(UnityModManager.ModEntry owner, string displayName,
    SynchronizationContext mainThread)
  {
    BootstrapManifest manifest = null;
    try
    {
      manifest = BootstrapManifest.Load(owner.Path);
      DependencyIssue issue = DependencyProbe.Probe(owner, manifest);
      if (issue != null)
      {
        Report(owner, issue, displayName, mainThread);
        return;
      }

      if (ModActivator.Find() == null && !ModActivator.IsLoaded())
      {
        try { await EnsureInstalledAsync(owner).ConfigureAwait(false); }
        catch (Exception exception)
        {
          Report(owner, DependencyProbe.Issue(owner, manifest, DependencyIssueKind.InstallFailure,
            null, exception.Message), displayName, mainThread);
          return;
        }
      }

      issue = DependencyProbe.Probe(owner, manifest);
      if (issue != null)
      {
        Report(owner, issue, displayName, mainThread);
        return;
      }

      try
      {
        await RunOnMainThread(mainThread, () => ModActivator.EnsureActive(
          manifest.MinimumAdofaiIpcVersion)).ConfigureAwait(false);
      }
      catch (Exception exception)
      {
        Report(owner, DependencyProbe.Issue(owner, manifest, DependencyIssueKind.LoadFailure,
          ModActivator.Find()?.Info.Version, exception.Message), displayName, mainThread);
        return;
      }

      owner.Info.DisplayName = displayName;
      try
      {
        await RunOnMainThread(mainThread, () => DependencyModLoader.Load(owner, manifest))
          .ConfigureAwait(false);
      }
      catch (Exception exception)
      {
        owner.Info.DisplayName = Bootstrap.Status(owner, "Load Error");
        owner.Logger.Error("Dependent mod core failed to load: " + exception);
      }
    }
    catch (Exception exception)
    {
      owner.Info.DisplayName = Bootstrap.Status(owner, "AdofaiIpc Error");
      owner.Logger.Error(exception.ToString());
      if (manifest != null)
        Report(owner, DependencyProbe.Issue(owner, manifest, DependencyIssueKind.LoadFailure,
          ModActivator.Find()?.Info.Version, exception.Message), displayName, mainThread);
    }
  }

  private static Task EnsureInstalledAsync(UnityModManager.ModEntry owner)
  {
    lock (AppDomain.CurrentDomain)
    {
      Task existing = AppDomain.CurrentDomain.GetData(InstallTaskKey) as Task;
      if (existing != null && !existing.IsFaulted && !existing.IsCanceled) return existing;
      Task created = DependencyInstaller.InstallAsync(owner);
      AppDomain.CurrentDomain.SetData(InstallTaskKey, created);
      return created;
    }
  }

  private static void Report(UnityModManager.ModEntry owner, DependencyIssue issue, string displayName,
    SynchronizationContext mainThread)
  {
    owner.Info.DisplayName = Bootstrap.Status(owner, "AdofaiIpc Error");
    owner.Logger.Error(issue.Detail);
    issue.DisplayName = displayName;
    DependencyIssueRegistry.Report(issue);
    DependencyIssuePresenter.RequestRefresh(mainThread, owner.Logger);
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
      try { action(); completion.SetResult(true); }
      catch (Exception exception) { completion.SetException(exception); }
    }, null);
    return completion.Task;
  }
}
