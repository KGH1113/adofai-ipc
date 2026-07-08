using System;
using AdofaiIpc.Core;

namespace AdofaiIpc;

public sealed class AdofaiIpcNamespace
{
  private readonly IpcRegistry _registry;

  public string Name { get; }
  public IpcNamespaceInfo Info { get; }

  internal AdofaiIpcNamespace(IpcRegistry registry, string name, IpcNamespaceInfo info)
  {
    _registry = registry;
    Name = name;
    Info = info;
  }

  public void Register(string method, Func<IpcRequest, object> handler)
  {
    _registry.RegisterMethod(Name, method, handler, false);
  }

  public void RegisterMainThread(string method, Func<IpcRequest, object> handler)
  {
    _registry.RegisterMethod(Name, method, handler, true);
  }

  public bool Unregister(string method)
  {
    return _registry.UnregisterMethod(Name, method);
  }
}
