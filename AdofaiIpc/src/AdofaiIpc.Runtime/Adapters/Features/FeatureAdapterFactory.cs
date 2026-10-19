using System;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Runtime;
internal sealed class FeatureAdapterFactory : IFeatureAdapterFactory {
  public IFeatureRegistration Create(Action<IFeatureRegistration> release, AdofaiIpcNamespace handle, Action<Action> dispatch)
    => new FeatureRegistration(release, handle, dispatch);
  public void Configure(IFeatureRegistration registration, Action<IFeatureBuilder> configure) {
    var builder = new FeatureBuilder((FeatureRegistration)registration);
    try { configure(builder); } finally { builder.Seal(); }
  }
}
