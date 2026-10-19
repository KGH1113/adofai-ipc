namespace AdofaiIpc;

public sealed class IpcNamespaceInfo
{
  public string DisplayName;
  public string Version;
  public int ProtocolMajor = 1;
  public string[] AllowedOrigins;
  public bool ExactOrigins;
}
