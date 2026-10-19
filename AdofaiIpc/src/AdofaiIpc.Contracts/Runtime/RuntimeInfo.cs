using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public sealed class RuntimeInfo
{
  public string Version { get; }
  public int ContractMajor { get; }
  public int WireMajor { get; }
  public string[] Capabilities => (string[])_capabilities.Clone();
  private readonly string[] _capabilities;

  public RuntimeInfo(string version, int contractMajor, int wireMajor, params string[] capabilities)
  {
    Version = version; ContractMajor = contractMajor; WireMajor = wireMajor;
    _capabilities = (string[])(capabilities ?? Array.Empty<string>()).Clone();
  }
  public bool Supports(string capability) => _capabilities.Contains(capability, StringComparer.Ordinal);
}
