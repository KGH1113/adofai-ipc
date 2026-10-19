using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

/// <summary>Frozen ABI identity; product releases and wire protocols are versioned separately.</summary>
public static class IpcContract
{
  public const int Major = 1;
  public const int WireMajor = 3;
  public const string AssemblyVersion = "1.0.0.0";
}
