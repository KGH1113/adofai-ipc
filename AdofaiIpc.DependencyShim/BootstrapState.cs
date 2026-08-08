namespace AdofaiIpc.DependencyShim;

internal sealed class BootstrapState
{
  public int SchemaVersion { get; set; } = 1;
  public string Current { get; set; }
  public string Previous { get; set; }
  public string Trial { get; set; }
}
