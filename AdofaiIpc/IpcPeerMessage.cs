using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc;

public sealed class IpcPeerMessage
{
  [JsonProperty("kind")] public string Kind;
  [JsonProperty("namespace")] public string Namespace;
  [JsonProperty("name")] public string Name;
  [JsonProperty("id")] public string Id;
  [JsonProperty("correlationId", NullValueHandling = NullValueHandling.Ignore)] public string CorrelationId;
  [JsonProperty("payload")] public JToken Payload;

  internal static IpcPeerMessage Create(string kind, string ns, string name, object payload, string correlationId = null, string id = null)
  {
    return new IpcPeerMessage { Kind = kind, Namespace = ns, Name = name,
      Id = id ?? Guid.NewGuid().ToString("N"), CorrelationId = correlationId,
      Payload = payload is JToken token ? token : payload == null ? new JObject() : JToken.FromObject(payload) };
  }
}

public sealed class IpcPeerInfo
{
  public string PeerId { get; internal set; }
  public string ConnectionId { get; internal set; }
  public string Origin { get; internal set; }
}

public sealed class IpcPeerEvent
{
  public string PeerId { get; internal set; }
  public string ConnectionId { get; internal set; }
  public string Name { get; internal set; }
  public string Id { get; internal set; }
  public string CorrelationId { get; internal set; }
  public JToken Payload { get; internal set; }
}
