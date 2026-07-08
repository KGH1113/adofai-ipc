using JALib.Core;
using Newtonsoft.Json.Linq;
using AdofaiIpc.Server;

namespace AdofaiIpc;

public sealed class Main : JAMod
{
  public static Main Instance;
  public static IpcServer Server;

  protected override void OnSetup()
  {
    Instance = this;
    RegisterBuiltInNamespace();
  }

  protected override void OnEnable()
  {
    if (Server != null) return;

    Server = new IpcServer();
    Server.Start();
  }

  protected override void OnDisable()
  {
    Server?.Stop();
    Server = null;
  }

  private static void RegisterBuiltInNamespace()
  {
    AdofaiIpcNamespace ipc = AdofaiIpc.RegisterNamespace("adofai-ipc", new IpcNamespaceInfo
    {
      DisplayName = "AdofaiIpc",
      Version = Instance.Version.ToString()
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
