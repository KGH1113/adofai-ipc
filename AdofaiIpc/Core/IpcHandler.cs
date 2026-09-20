using System;

namespace AdofaiIpc.Core;

public sealed class IpcHandler
{
  private readonly Func<IpcRequest, object> _handler;

  public bool RequiresMainThread { get; }
  public bool IsDownload { get; }

  public IpcHandler(Func<IpcRequest, object> handler, bool requiresMainThread)
    : this(handler, requiresMainThread, false)
  {
  }

  public IpcHandler(Func<IpcRequest, object> handler, bool requiresMainThread, bool isDownload)
  {
    _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    RequiresMainThread = requiresMainThread;
    IsDownload = isDownload;
  }

  public object Invoke(IpcRequest request)
  {
    return _handler(request);
  }
}
