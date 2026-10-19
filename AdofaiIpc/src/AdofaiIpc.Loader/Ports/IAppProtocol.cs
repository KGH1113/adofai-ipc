namespace AdofaiIpc.Ports;
internal sealed class AppRegistrationRequest {
  internal string AppId, DisplayName, ContractHash;
}
internal interface IAppProtocol {
  AppRegistrationRequest ReadRegistration(string json);
  string WriteRegistration(string appId, string sessionId);
  string WriteAppCall(string callId, string sessionId, string args);
  IpcCallException ReadError(string json);
}
