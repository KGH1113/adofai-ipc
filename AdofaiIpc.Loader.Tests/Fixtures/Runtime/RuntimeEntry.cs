using System;
using System.Reflection;
using AdofaiIpc.Contracts;

namespace AdofaiIpc.Runtime;

public static class RuntimeEntry
{
  private static readonly IIpcRuntime Instance = new TestRuntime();
  public static RuntimeInfo Describe() => new RuntimeInfo(
    typeof(RuntimeEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion, 1, 3, "commands");
  public static IIpcRuntime Start() => Instance;
  private sealed class TestRuntime : IIpcRuntime
  {
    public RuntimeInfo Info => Describe();
    public bool IsGameThread => true;
    public void RunOnGameThread(Action action) => action();
    public IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure) => throw new NotSupportedException();
    public ILocalPeer CreateLocalPeer(string name) => throw new NotSupportedException();
  }
}
