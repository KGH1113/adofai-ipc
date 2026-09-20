using System;
using AdofaiIpc.Core;

namespace AdofaiIpc;

public sealed class AdofaiIpcNamespace
{
  private readonly IpcRegistry _registry;

  public string Name { get; }
  public IpcNamespaceInfo Info { get; }
  public IpcNamespaceStatus Status => _registry.GetNamespaceStatus(Name);

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

  public void RegisterDownload(string method, Func<IpcRequest, object> handler)
  {
    _registry.RegisterDownloadMethod(Name, method, handler, false);
  }

  public void RegisterDownloadMainThread(string method, Func<IpcRequest, object> handler)
  {
    _registry.RegisterDownloadMethod(Name, method, handler, true);
  }

  public void MarkInitializing()
  {
    _registry.SetNamespaceInitializing(Name);
  }

  public void MarkReady()
  {
    _registry.SetNamespaceReady(Name);
  }

  public void MarkError(string code, string message)
  {
    _registry.SetNamespaceError(Name, code, message);
  }

  public bool Unregister(string method)
  {
    return _registry.UnregisterMethod(Name, method);
  }
}
