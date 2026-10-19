using AdofaiIpc.Ports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AdofaiIpc;

internal sealed class JsonAppProtocol : IAppProtocol {
  private static readonly IpcSchema Registration = IpcSchema.Object(
    IpcSchema.Field("appId", IpcSchema.String), IpcSchema.Field("displayName", IpcSchema.String), IpcSchema.Field("contractHash", IpcSchema.String));
  public AppRegistrationRequest ReadRegistration(string json) {
    try {
      var value = IpcJson.Parse(json); IpcSchemaValidator.Validate(Registration, value, JsonValueReader.Instance);
      return new AppRegistrationRequest { AppId = (string)value["appId"], DisplayName = (string)value["displayName"], ContractHash = (string)value["contractHash"] };
    } catch (JsonException error) { throw new IpcValidationException(error.Message); }
  }
  public string WriteRegistration(string appId, string sessionId) => new JObject { ["appId"] = appId, ["sessionId"] = sessionId }.ToString(Formatting.None);
  public string WriteAppCall(string callId, string sessionId, string args) => new JObject { ["callId"] = callId, ["sessionId"] = sessionId, ["args"] = IpcJson.Parse(args) }.ToString(Formatting.None);
  public IpcCallException ReadError(string json) {
    var value = IpcJson.Parse(json);
    return new IpcCallException((string)value["code"] ?? "handler_failed", (string)value["message"] ?? "App method failed.");
  }
}
