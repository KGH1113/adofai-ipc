using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

/// <summary>Frozen ABI identity; product releases and wire protocols are versioned separately.</summary>
public static class IpcContract
{
  public const int Major = 1;
  public const int WireMajor = 3;
  public const string AssemblyVersion = "1.0.0.0";
}

public sealed class RuntimeRequirements
{
  public int ContractMajor { get; }
  public int WireMajor { get; }
  public string MinimumRuntimeVersion { get; }
  public string[] RequiredCapabilities => (string[])_capabilities.Clone();
  private readonly string[] _capabilities;

  public RuntimeRequirements(string minimumRuntimeVersion = "2.0.0", int contractMajor = IpcContract.Major,
    int wireMajor = IpcContract.WireMajor, params string[] requiredCapabilities)
  {
    MinimumRuntimeVersion = minimumRuntimeVersion ?? throw new ArgumentNullException(nameof(minimumRuntimeVersion));
    ContractMajor = contractMajor; WireMajor = wireMajor;
    _capabilities = (string[])(requiredCapabilities ?? Array.Empty<string>()).Clone();
  }
}

public sealed class RuntimeInfo
{
  public string Version { get; }
  public int ContractMajor { get; }
  public int WireMajor { get; }
  public string[] Capabilities => (string[])_capabilities.Clone();
  private readonly string[] _capabilities;

  public RuntimeInfo(string version, int contractMajor, int wireMajor, params string[] capabilities)
  {
    Version = version; ContractMajor = contractMajor; WireMajor = wireMajor;
    _capabilities = (string[])(capabilities ?? Array.Empty<string>()).Clone();
  }
  public bool Supports(string capability) => _capabilities.Contains(capability, StringComparer.Ordinal);
}

public sealed class FeatureDescription
{
  public string Name { get; }
  public string Version { get; }
  public string DisplayName { get; }
  public int ProtocolMajor { get; }
  public bool ExactOrigins { get; set; }
  public string[] AllowedOrigins => _origins == null ? null : (string[])_origins.Clone();
  private readonly string[] _origins;

  public FeatureDescription(string name, string version, int protocolMajor = 1,
    string displayName = null, string[] allowedOrigins = null)
  {
    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A feature name is required.", nameof(name));
    if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("A feature version is required.", nameof(version));
    if (protocolMajor < 1) throw new ArgumentOutOfRangeException(nameof(protocolMajor));
    Name = name; Version = version; ProtocolMajor = protocolMajor; DisplayName = displayName ?? name;
    _origins = allowedOrigins == null ? null : (string[])allowedOrigins.Clone();
  }
}

/// <summary>Consumers own registrations, never the process host or its server.</summary>
public interface IIpcRuntime
{
  RuntimeInfo Info { get; }
  bool IsGameThread { get; }
  void RunOnGameThread(Action action);
  IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure);
  ILocalPeer CreateLocalPeer(string name);
}

public enum IpcExecutionContext { Background, GameThread }

public interface IFeatureBuilder
{
  // GameThread selects the invocation thread. Async continuations must dispatch explicitly.
  void OnCommand(string name, IpcExecutionContext context, Func<IIncomingMessage, Task> handler);
  void OnClientAttached(Action<IClientConnection> handler);
  void OnClientDetached(Action<IClientConnection> handler);
  void OnEvent(Action<PeerEvent> handler);
}

public interface IFeatureRegistration : IDisposable
{
  string Name { get; }
  void MarkInitializing();
  void MarkReady();
  void MarkError(string code, string message);
  void Publish(string name, string json);
  void SendToPeer(string peerId, string name, string json, string correlationId = null);
  string SendCommandToPeer(string peerId, string name, string json);
}

public interface IIncomingMessage
{
  string Id { get; }
  string Name { get; }
  string Json { get; }
  IClientConnection Client { get; }
  CancellationToken CancellationToken { get; }
  void Reply(string name, string json);
  void Reject(string code, string message);
  void Reject(string code, string message, string detailsJson);
  // Ownership of the source transfers to the runtime, including failure and cancellation paths.
  void ReplyDownload(string name, Stream source, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null);
  void ReplyDownload(string name, Action<Stream> writer, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null);
}

public interface IClientConnection
{
  string PeerId { get; }
  string ConnectionId { get; }
  string Origin { get; }
  void Send(string name, string json, string correlationId = null);
  string SendCommand(string name, string json);
}

/// <summary>Optional runtime extension for a captured connection's live transport lifetime.</summary>
public interface IClientConnectionState
{
  bool IsConnected { get; }
  CancellationToken CancellationToken { get; }
}

public sealed class PeerEvent
{
  public string PeerId { get; }
  public string ConnectionId { get; }
  public string Name { get; }
  public string Id { get; }
  public string CorrelationId { get; }
  public string Json { get; }
  public PeerEvent(string peerId, string connectionId, string name, string id, string correlationId, string json)
  { PeerId = peerId; ConnectionId = connectionId; Name = name; Id = id; CorrelationId = correlationId; Json = json; }
}

public sealed class LocalMessage
{
  public string Kind { get; }
  public string Namespace { get; }
  public string Name { get; }
  public string Id { get; }
  public string CorrelationId { get; }
  public string Json { get; }
  public LocalMessage(string kind, string ns, string name, string id, string correlationId, string json)
  { Kind = kind; Namespace = ns; Name = name; Id = id; CorrelationId = correlationId; Json = json; }
}

public interface ILocalPeer : IDisposable
{
  bool IsClosed { get; }
  event Action<LocalMessage> Message;
  void Subscribe(string feature);
  void Unsubscribe(string feature);
  string SendCommand(string feature, string name, string json);
  void SendEvent(string feature, string name, string json, string correlationId = null);
}
