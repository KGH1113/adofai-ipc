using Newtonsoft.Json;

namespace AdofaiIpc.Core;

public sealed class IpcError
{
  [JsonProperty("code")]
  public string Code;
  [JsonProperty("message")]
  public string Message;

  public IpcError()
  {
  }

  public IpcError(string code, string message)
  {
    Code = code;
    Message = message;
  }
}
