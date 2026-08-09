using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AdofaiIpc.Launcher;

internal sealed class UpdateManifest
{
  private static readonly Regex ShaPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
  public int SchemaVersion { get; set; }
  public string Version { get; set; }
  public string PackageUrl { get; set; }
  public long PackageSize { get; set; }
  public string Sha256 { get; set; }

  public static UpdateManifest Parse(string json)
  {
    UpdateManifest manifest = JsonConvert.DeserializeObject<UpdateManifest>(json) ??
                              throw new InvalidDataException("AdofaiIpc update manifest is invalid.");
    if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported update manifest schema.");
    SemanticVersion version = SemanticVersion.Parse(manifest.Version);
    if (version.IsPrerelease) throw new InvalidDataException("The stable manifest contains a prerelease.");
    if (manifest.PackageSize <= 0 || manifest.PackageSize > PackageInstaller.MaximumPackageBytes)
      throw new InvalidDataException("AdofaiIpc package size is invalid.");
    if (!ShaPattern.IsMatch(manifest.Sha256 ?? string.Empty))
      throw new InvalidDataException("AdofaiIpc package checksum is invalid.");
    if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out Uri uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
        !uri.AbsolutePath.StartsWith("/KGH1113/adofai-ipc/releases/", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package URL is not an official release URL.");
    return manifest;
  }
}
