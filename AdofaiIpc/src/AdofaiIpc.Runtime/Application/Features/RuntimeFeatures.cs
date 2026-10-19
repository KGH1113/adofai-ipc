using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;

namespace AdofaiIpc.Runtime;

internal sealed class RuntimeFeatures : IIpcRuntime
{
  private readonly object _gate = new object();
  private readonly IpcRegistry _registry;
  private readonly Action<Action> _dispatch;
  private readonly Func<bool> _isGameThread;
  private readonly Dictionary<string, IFeatureRegistration> _features = new Dictionary<string, IFeatureRegistration>();
  private volatile bool _stopped;
  private readonly IFeatureAdapterFactory _factory;
  private readonly ILocalPeerFactory _peers;
  public RuntimeInfo Info { get; }
  public bool IsGameThread => _isGameThread();

  internal RuntimeFeatures(IpcRegistry registry, RuntimeInfo info, Action<Action> dispatch, Func<bool> isGameThread, IFeatureAdapterFactory factory, ILocalPeerFactory peers)
  { _registry = registry; Info = info; _dispatch = dispatch; _isGameThread = isGameThread; _factory = factory; _peers = peers; }

  public void RunOnGameThread(Action action)
  {
    if (action == null) throw new ArgumentNullException(nameof(action));
    lock (_gate)
    {
      ThrowIfStopped();
      _dispatch(() => { if (!_stopped) action(); });
    }
  }

  public IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure)
  {
    if (description == null) throw new ArgumentNullException(nameof(description));
    if (configure == null) throw new ArgumentNullException(nameof(configure));
    IFeatureRegistration registration;
    lock (_gate)
    {
      ThrowIfStopped();
      if (_features.ContainsKey(description.Name)) throw new InvalidOperationException("IPC feature is already registered: " + description.Name);
      var handle = _registry.RegisterNamespace(description.Name, new IpcNamespaceInfo {
        DisplayName = description.DisplayName, Version = description.Version,
        ProtocolMajor = description.ProtocolMajor, AllowedOrigins = description.AllowedOrigins, ExactOrigins = description.ExactOrigins });
      registration = _factory.Create(Release, handle, _dispatch);
      _features.Add(description.Name, registration);
    }
    try { _factory.Configure(registration, configure); return registration; }
    catch { registration.Dispose(); throw; }
  }

  public ILocalPeer CreateLocalPeer(string name)
  {
    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A peer name is required.", nameof(name));
    lock (_gate) { ThrowIfStopped(); return _peers.Create(name); }
  }

  internal void Release(IFeatureRegistration registration)
  {
    lock (_gate)
    {
      if (_features.TryGetValue(registration.Name, out var current) && ReferenceEquals(current, registration))
      { _features.Remove(registration.Name); _registry.UnregisterNamespace(registration.Name); }
    }
  }

  internal void Shutdown()
  {
    IFeatureRegistration[] registrations;
    lock (_gate)
    {
      if (_stopped) return;
      _stopped = true;
      registrations = new List<IFeatureRegistration>(_features.Values).ToArray();
    }
    foreach (var registration in registrations)
    {
      try { registration.Dispose(); }
      catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
    }
  }
  private void ThrowIfStopped() { if (_stopped) throw new InvalidOperationException("The IPC process host has stopped."); }

}
