using System;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Core;

namespace AdofaiIpc;

public sealed class AdofaiIpcNamespace
{
  private readonly IpcRegistry _registry;
  internal readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
  public string Name { get; }
  public IpcNamespaceInfo Info { get; }
  public IpcNamespaceStatus Status => _registry.GetNamespaceStatus(Name);
  public event Action<IpcPeerInfo> PeerSubscribed;
  public event Action<IpcPeerInfo> PeerDisconnected;
  public event Action<IpcPeerEvent> PeerEvent;

  internal AdofaiIpcNamespace(IpcRegistry registry, string name, IpcNamespaceInfo info)
  { _registry = registry; Name = name; Info = info; }

  public void RegisterCommand(string name, Action<IpcCommand> handler)
  { _registry.RegisterCommand(this, name, command => { handler(command); return Task.CompletedTask; }, false); }
  public void RegisterMainThreadCommand(string name, Action<IpcCommand> handler)
  { _registry.RegisterCommand(this, name, command => { handler(command); return Task.CompletedTask; }, true); }
  public void RegisterAsyncCommand(string name, Func<IpcCommand, Task> handler)
  { _registry.RegisterCommand(this, name, handler, false); }
  internal void RegisterGameThreadAsyncCommand(string name, Func<IpcCommand, Task> handler)
  { _registry.RegisterCommand(this, name, handler, true); }
  public void RegisterDownloadCommand(string name, Func<IpcCommand, object> handler)
  { RegisterCommand(name, command => _registry.CreateDownload(this, command, handler(command))); }
  /// <summary>Replies with a named download ticket and small domain metadata. The ticket owns the source.</summary>
  public void ReplyDownload(IpcCommand command, string eventName, IpcDownloadSource source, object metadata = null)
  {
    if (command == null) throw new ArgumentNullException(nameof(command));
    if (command.Namespace != Name) throw new ArgumentException("The command belongs to another namespace.", nameof(command));
    if (!IpcNameValidator.IsValidMethod(eventName)) throw new ArgumentException("Invalid download event name.", nameof(eventName));
    _registry.CreateDownload(this, command, source, eventName, metadata);
  }
  public void MarkInitializing() => _registry.SetStatus(this, IpcNamespaceStatus.Initializing, null, null);
  public void MarkReady() => _registry.SetStatus(this, IpcNamespaceStatus.Ready, null, null);
  public void MarkError(string code, string message) => _registry.SetStatus(this, IpcNamespaceStatus.Error, code, message);
  public bool Unregister(string name) => _registry.UnregisterCommand(this, name);
  public void Publish(string name, object payload) => _registry.Publish(this, name, payload);
  public void SendToPeer(string peerId, string name, object payload, string correlationId = null)
    => _registry.SendToPeer(this, peerId, "event", name, payload, correlationId);
  public string SendCommandToPeer(string peerId, string name, object payload)
    => _registry.SendToPeer(this, peerId, "command", name, payload, null);
  internal string SendToConnection(string peerId, string connectionId, string kind, string name, object payload, string correlationId = null)
    => _registry.SendToPeer(this, peerId, kind, name, payload, correlationId, connectionId);
  internal bool IsConnectionActive(string peerId, string connectionId) => _registry.IsConnectionActive(this, peerId, connectionId);
  internal CancellationToken ConnectionLifetime(string peerId, string connectionId) => _registry.ConnectionLifetime(peerId, connectionId);
  internal void NotifySubscribed(IpcPeerInfo peer) => PeerSubscribed?.Invoke(peer);
  internal void NotifyDisconnected(IpcPeerInfo peer) => PeerDisconnected?.Invoke(peer);
  internal void NotifyEvent(IpcPeerEvent message) => PeerEvent?.Invoke(message);
}
