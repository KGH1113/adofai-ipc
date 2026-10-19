using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public sealed class FeatureDescription
{
  public string Name { get; }
  public string Version { get; }
  public string DisplayName { get; }
  public int ProtocolMajor { get; }
  public bool ExactOrigins { get; set; }
  public string[] AllowedOrigins => _origins == null ? null : (string[])_origins.Clone();
  private readonly string[] _origins;

  public FeatureDescription(string name, string version, int protocolMajor = 1,
    string displayName = null, string[] allowedOrigins = null)
  {
    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A feature name is required.", nameof(name));
    if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("A feature version is required.", nameof(version));
    if (protocolMajor < 1) throw new ArgumentOutOfRangeException(nameof(protocolMajor));
    Name = name; Version = version; ProtocolMajor = protocolMajor; DisplayName = displayName ?? name;
    _origins = allowedOrigins == null ? null : (string[])allowedOrigins.Clone();
  }
}
