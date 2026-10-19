using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Runtime;

internal sealed class ClientConnection : IClientConnection, IClientConnectionState
{
  private readonly FeatureRegistration _registration;
  internal ClientConnection(FeatureRegistration registration, IpcPeerInfo info)
  { _registration = registration; PeerId = info.PeerId; ConnectionId = info.ConnectionId; Origin = info.Origin; CancellationToken = registration.ConnectionLifetime(PeerId, ConnectionId); }
  public string PeerId { get; }
  public string ConnectionId { get; }
  public string Origin { get; }
  public bool IsConnected => _registration.IsConnectionActive(PeerId, ConnectionId);
  public CancellationToken CancellationToken { get; }
  public void Send(string name, string json, string correlationId = null) => _registration.SendToConnection(PeerId, ConnectionId, "event", name, json, correlationId);
  public string SendCommand(string name, string json) => _registration.SendToConnection(PeerId, ConnectionId, "command", name, json);
}
