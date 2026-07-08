namespace AdofaiIpc.Core;

public sealed class IpcError
{
  public string Code;
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
