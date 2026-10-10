namespace AdofaiIpc;

public static class IpcProtocol
{
  public const int Version = 3;
  public const string ProductVersion = "2.0.0";
  public const string Subprotocol = "adofai-ipc.v3";
  public const string WebSocketPath = "/ipc/ws";
  public const int MaxMessageBytes = 2 * 1024 * 1024;
}
