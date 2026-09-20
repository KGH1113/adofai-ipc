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
  [JsonIgnore]
  internal int StatusCode;
  [JsonIgnore]
  internal bool IsDownload;

  public static IpcResponse Success(string id, object result)
  {
    return Success(id, result, false);
  }

  public static IpcResponse Success(string id, object result, bool isDownload)
  {
    return new IpcResponse
    {
      Ok = true,
      Result = result,
      Id = id,
      IsDownload = isDownload
    };
  }

  public static IpcResponse Fail(string id, string code, string message)
  {
    return Fail(id, code, message, 0);
  }

  public static IpcResponse Fail(string id, string code, string message, int statusCode)
  {
    return new IpcResponse
    {
      Ok = false,
      Error = new IpcError(code, message),
      Id = id,
      StatusCode = statusCode
    };
  }
}
