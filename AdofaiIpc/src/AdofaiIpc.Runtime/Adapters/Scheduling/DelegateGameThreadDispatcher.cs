using System;
namespace AdofaiIpc.Core;
internal sealed class DelegateGameThreadDispatcher : IGameThreadDispatcher {
  private readonly Action<Action> _dispatch;
  internal DelegateGameThreadDispatcher(Action<Action> dispatch) { _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch)); }
  public void Run(Action action) => _dispatch(action);
}
