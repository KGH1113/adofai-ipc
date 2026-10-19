using Newtonsoft.Json.Linq;
namespace AdofaiIpc.Core;
internal sealed class JsonMessagePayload : IMessagePayload {
  private readonly JToken _value;
  internal JsonMessagePayload(JToken value) { _value = value; }
  public bool IsObject => _value is JObject;
  public int? Int32Property(string name) => (int?)_value?[name];
  public object NativeValue => _value;
}
internal sealed class JsonMessageProtocol : IMessageProtocol {
  public IRoutedMessage Create(string kind, string ns, string name, object payload, string correlationId = null, string id = null)
    => IpcPeerMessage.Create(kind, ns, name, payload, correlationId, id);
  public IpcPeerEvent Event(IpcPeerInfo peer, IRoutedMessage message) => new IpcPeerEvent {
    PeerId = peer.PeerId, ConnectionId = peer.ConnectionId, Name = message.Name, Id = message.Id,
    CorrelationId = message.CorrelationId, Payload = message.Payload?.NativeValue as JToken ?? new JObject()
  };
  internal static IpcPeerMessage ToWire(IRoutedMessage message) => message as IpcPeerMessage ?? new IpcPeerMessage {
    Kind = message.Kind, Namespace = message.Namespace, Name = message.Name, Id = message.Id,
    CorrelationId = message.CorrelationId, Payload = message.Payload?.NativeValue as JToken
  };
}
