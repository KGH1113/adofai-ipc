using System;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Runtime;

internal interface IFeatureAdapterFactory {
  IFeatureRegistration Create(Action<IFeatureRegistration> release, AdofaiIpcNamespace handle, Action<Action> dispatch);
  void Configure(IFeatureRegistration registration, Action<IFeatureBuilder> configure);
}
internal interface ILocalPeerFactory { ILocalPeer Create(string name); }
