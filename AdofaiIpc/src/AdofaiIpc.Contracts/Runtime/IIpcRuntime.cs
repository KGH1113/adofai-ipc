using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

/// <summary>Consumers own registrations, never the process host or its server.</summary>
public interface IIpcRuntime
{
  RuntimeInfo Info { get; }
  bool IsGameThread { get; }
  void RunOnGameThread(Action action);
  IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure);
  ILocalPeer CreateLocalPeer(string name);
}
