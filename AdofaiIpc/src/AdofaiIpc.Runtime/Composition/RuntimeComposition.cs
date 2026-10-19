using System;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
namespace AdofaiIpc.Runtime;
internal static class RuntimeComposition {
  internal static RuntimeFeatures Create(IpcRegistry registry, RuntimeInfo info, Action<Action> dispatch, Func<bool> isGameThread)
    => new RuntimeFeatures(registry, info, dispatch, isGameThread, new FeatureAdapterFactory(), new LocalPeerFactory(registry));
}
