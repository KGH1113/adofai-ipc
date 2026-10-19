using System;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
namespace AdofaiIpc.Runtime;
internal sealed class RoutedRuntime : IIpcRuntime {
  private readonly RuntimeFeatures _features;
  internal RoutedRuntime(IpcRegistry registry, RuntimeInfo info, Action<Action> dispatch, Func<bool> isGameThread)
    { _features = RuntimeComposition.Create(registry, info, dispatch, isGameThread); }
  public RuntimeInfo Info => _features.Info;
  public bool IsGameThread => _features.IsGameThread;
  public void RunOnGameThread(Action action) => _features.RunOnGameThread(action);
  public IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure) => _features.RegisterFeature(description, configure);
  public ILocalPeer CreateLocalPeer(string name) => _features.CreateLocalPeer(name);
  internal void Shutdown() => _features.Shutdown();
}
