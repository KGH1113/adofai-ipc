using Newtonsoft.Json;

namespace AdofaiIpc.Core;

public sealed class IpcResponse
{
  [JsonProperty("ok")]
  public bool Ok;
  [JsonProperty("result")]
  public object Result;
  [JsonProperty("error")]
  public IpcError Error;
  [JsonProperty("id")]
  public string Id;

  public static IpcResponse Success(string id, object result)
  {
    return new IpcResponse
    {
      Ok = true,
      Result = result,
      Id = id
    };
  }

  public static IpcResponse Fail(string id, string code, string message)
  {
    return new IpcResponse
    {
      Ok = false,
      Error = new IpcError(code, message),
      Id = id
    };
  }
}
