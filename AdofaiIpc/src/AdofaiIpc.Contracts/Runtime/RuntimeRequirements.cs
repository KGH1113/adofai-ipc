using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public sealed class RuntimeRequirements
{
  public int ContractMajor { get; }
  public int WireMajor { get; }
  public string MinimumRuntimeVersion { get; }
  public string[] RequiredCapabilities => (string[])_capabilities.Clone();
  private readonly string[] _capabilities;

  public RuntimeRequirements(string minimumRuntimeVersion = "2.0.0", int contractMajor = IpcContract.Major,
    int wireMajor = IpcContract.WireMajor, params string[] requiredCapabilities)
  {
    MinimumRuntimeVersion = minimumRuntimeVersion ?? throw new ArgumentNullException(nameof(minimumRuntimeVersion));
    ContractMajor = contractMajor; WireMajor = wireMajor;
    _capabilities = (string[])(requiredCapabilities ?? Array.Empty<string>()).Clone();
  }
}
