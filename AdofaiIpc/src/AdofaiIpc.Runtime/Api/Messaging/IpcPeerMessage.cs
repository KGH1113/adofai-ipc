using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc;

public sealed class IpcPeerMessage : AdofaiIpc.Core.IRoutedMessage
{
  [JsonProperty("kind")] public string Kind;
  [JsonProperty("namespace")] public string Namespace;
  [JsonProperty("name")] public string Name;
  [JsonProperty("id")] public string Id;
  [JsonProperty("correlationId", NullValueHandling = NullValueHandling.Ignore)] public string CorrelationId;
  [JsonProperty("payload")] public JToken Payload;

  string Core.IRoutedMessage.Kind => Kind;
  string Core.IRoutedMessage.Namespace => Namespace;
  string Core.IRoutedMessage.Name => Name;
  string Core.IRoutedMessage.Id => Id;
  string Core.IRoutedMessage.CorrelationId => CorrelationId;
  private JToken _routedSource;
  private Core.IMessagePayload _routedPayload;
  Core.IMessagePayload Core.IRoutedMessage.Payload {
    get {
      if (Payload == null) return null;
      if (!ReferenceEquals(Payload, _routedSource)) {
        _routedSource = Payload; _routedPayload = new Core.JsonMessagePayload(Payload);
      }
      return _routedPayload;
    }
  }

  internal static IpcPeerMessage Create(string kind, string ns, string name, object payload, string correlationId = null, string id = null)
  {
    return new IpcPeerMessage { Kind = kind, Namespace = ns, Name = name,
      Id = id ?? Guid.NewGuid().ToString("N"), CorrelationId = correlationId,
      Payload = payload is JToken token ? token : payload == null ? new JObject() : JToken.FromObject(payload) };
  }
}
