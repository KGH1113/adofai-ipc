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

internal sealed class LocalPeerAdapter : ILocalPeer
{
  private readonly IpcLocalPeer _peer;
  public bool IsClosed => _peer.IsClosed;
  public event Action<LocalMessage> Message;
  internal LocalPeerAdapter(IpcLocalPeer peer) { _peer = peer; peer.Message += Receive; }
  private void Receive(IpcPeerMessage message) => Message?.Invoke(new LocalMessage(message.Kind, message.Namespace,
    message.Name, message.Id, message.CorrelationId, RuntimeJson.Json(message.Payload)));
  public void Subscribe(string feature) => _peer.Subscribe(feature);
  public void Unsubscribe(string feature) => _peer.Unsubscribe(feature);
  public string SendCommand(string feature, string name, string json) => _peer.SendCommand(feature, name, RuntimeJson.Parse(json));
  public void SendEvent(string feature, string name, string json, string correlationId = null) => _peer.SendEvent(feature, name, RuntimeJson.Parse(json), correlationId);
  public void Dispose() { _peer.Dispose(); _peer.Message -= Receive; Message = null; }
}
