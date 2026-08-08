namespace AdofaiIpc.Bootstrap;

internal enum DependencyIssueKind
{
  InstallFailure,
  Disabled,
  Outdated,
  LoadFailure
}

internal sealed class DependencyIssue
{
  public DependencyIssueKind Kind { get; set; }
  public string ModId { get; set; }
  public string DisplayName { get; set; }
  public string MinimumVersion { get; set; }
  public string InstalledVersion { get; set; }
  public string Detail { get; set; }
}
