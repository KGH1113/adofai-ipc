using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AdofaiIpc.Launcher;

internal sealed class UpdateManifest
{
  private static readonly Regex ShaPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

  public int SchemaVersion { get; set; }
  public string Version { get; set; }
  public string PackageUrl { get; set; }
  public long PackageSize { get; set; }
  public string Sha256 { get; set; }

  public static UpdateManifest Parse(string json)
  {
    UpdateManifest manifest = JsonConvert.DeserializeObject<UpdateManifest>(json)
      ?? throw new InvalidDataException("AdofaiIpc update manifest is empty or invalid.");
    if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported AdofaiIpc manifest schema.");
    SemanticVersion version = SemanticVersion.Parse(manifest.Version);
    if (version.IsPrerelease) throw new InvalidDataException("The stable update manifest contains a prerelease.");
    if (manifest.PackageSize <= 0 || manifest.PackageSize > PackageInstaller.MaximumPackageBytes)
      throw new InvalidDataException("AdofaiIpc package size is invalid.");
    if (!ShaPattern.IsMatch(manifest.Sha256 ?? string.Empty))
      throw new InvalidDataException("AdofaiIpc package checksum is invalid.");
    if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out Uri packageUri) ||
        packageUri.Scheme != Uri.UriSchemeHttps ||
        !string.Equals(packageUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
        !packageUri.AbsolutePath.StartsWith("/KGH1113/adofai-ipc/releases/", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("AdofaiIpc package URL is not an official HTTPS release URL.");
    return manifest;
  }
}
