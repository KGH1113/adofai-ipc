using System;
namespace AdofaiIpc.Core;
public sealed partial class IpcRegistry {
  public IpcRegistry(Action<Action> dispatchMainThread)
    : this(new DelegateGameThreadDispatcher(dispatchMainThread), new JsonMessageProtocol()) { }
}
