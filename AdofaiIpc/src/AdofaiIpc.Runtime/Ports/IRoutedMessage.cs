namespace AdofaiIpc.Core;

/** Protocol-neutral view used by routing; serialization belongs to adapters. */
internal interface IRoutedMessage {
  string Kind { get; }
  string Namespace { get; }
  string Name { get; }
  string Id { get; }
  string CorrelationId { get; }
  IMessagePayload Payload { get; }
}
internal interface IMessagePayload {
  bool IsObject { get; }
  int? Int32Property(string name);
  object NativeValue { get; }
}
internal interface IMessageProtocol {
  IRoutedMessage Create(string kind, string ns, string name, object payload, string correlationId = null, string id = null);
  IpcPeerEvent Event(IpcPeerInfo peer, IRoutedMessage message);
}
