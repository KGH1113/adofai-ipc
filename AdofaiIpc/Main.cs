using System;
using Newtonsoft.Json.Linq;
using AdofaiIpc.Server;
using AdofaiIpc.Unity;
using UnityModManagerNet;

namespace AdofaiIpc;

public sealed class Main
{
  public static Main Instance { get; private set; }
  public static IpcServer Server { get; private set; }

  public UnityModManager.ModEntry ModEntry { get; }
  public string Version => ModEntry.Info.Version;

  private bool _enabled;

  private Main(UnityModManager.ModEntry modEntry)
  {
    ModEntry = modEntry;
  }

  public static bool Load(UnityModManager.ModEntry modEntry)
  {
    try
    {
      Instance = new Main(modEntry);
      modEntry.OnToggle = OnToggle;
      modEntry.OnUnload = OnUnload;

      MainThreadDispatcher.Initialize();
      RegisterBuiltInNamespace();
      Instance.Enable();
      return true;
    }
    catch (Exception e)
    {
      modEntry.Logger.Error(e.ToString());
      return false;
    }
  }

  public void Log(string message)
  {
    ModEntry.Logger.Log(message);
  }

  public void Warning(string message)
  {
    ModEntry.Logger.Warning(message);
  }

  public void LogException(Exception exception)
  {
    ModEntry.Logger.Error(exception.ToString());
  }

  private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
  {
    try
    {
      if (value) Instance.Enable();
      else Instance.Disable();
      return true;
    }
    catch (Exception e)
    {
      modEntry.Logger.Error(e.ToString());
      return false;
    }
  }

  private static bool OnUnload(UnityModManager.ModEntry modEntry)
  {
    try
    {
      Instance?.Disable();
      MainThreadDispatcher.Shutdown();
      return true;
    }
    catch (Exception e)
    {
      modEntry.Logger.Error(e.ToString());
      return false;
    }
  }

  private void Enable()
  {
    if (_enabled) return;

    Server = new IpcServer();
    Server.Start();
    _enabled = true;
  }

  private void Disable()
  {
    if (!_enabled) return;

    Server?.Stop();
    Server = null;
    _enabled = false;
  }

  private static void RegisterBuiltInNamespace()
  {
    AdofaiIpcNamespace ipc = AdofaiIpc.RegisterNamespace("adofai-ipc", new IpcNamespaceInfo
    {
      DisplayName = "AdofaiIpc",
      Version = Instance.Version
    });

    ipc.Register("ping", request => new
    {
      pong = true,
      server = "AdofaiIpc",
      protocolVersion = 1,
      port = Server?.Port ?? 0
    });

    ipc.Register("echo", request => request.Params ?? new JObject());
  }
}
