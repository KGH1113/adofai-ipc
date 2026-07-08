using System;

namespace AdofaiIpc.Core;

public sealed class IpcHandler
{
  private readonly Func<IpcRequest, object> _handler;

  public bool RequiresMainThread { get; }

  public IpcHandler(Func<IpcRequest, object> handler, bool requiresMainThread)
  {
    _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    RequiresMainThread = requiresMainThread;
  }

  public object Invoke(IpcRequest request)
  {
    return _handler(request);
  }
}
