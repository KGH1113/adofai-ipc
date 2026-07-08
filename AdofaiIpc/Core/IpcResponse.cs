namespace AdofaiIpc.Core;

public sealed class IpcResponse
{
  public bool Ok;
  public object Result;
  public IpcError Error;
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
