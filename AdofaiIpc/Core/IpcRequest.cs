using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Core;

public sealed class IpcRequest
{
  [JsonProperty("namespace")]
  public string Namespace;

  [JsonProperty("method")]
  public string Method;

  [JsonProperty("params")]
  public JToken Params;

  [JsonProperty("id")]
  public string Id;
}
