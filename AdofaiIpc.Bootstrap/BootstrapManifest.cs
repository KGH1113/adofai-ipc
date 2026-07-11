using System;
using System.IO;
using Newtonsoft.Json;

namespace AdofaiIpc.Bootstrap;

internal sealed class BootstrapManifest
{
  private const string FileName = "AdofaiIpcBootstrap.json";

  public string AssemblyName { get; set; }
  public string EntryMethod { get; set; }
  public string MinimumAdofaiIpcVersion { get; set; }
  public string DownloadUrl { get; set; }
  public string ChecksumUrl { get; set; }

  public static BootstrapManifest Load(string modPath)
  {
    string path = Path.Combine(modPath, FileName);
    if (!File.Exists(path))
      throw new FileNotFoundException($"{FileName} was not found.", path);

    BootstrapManifest manifest = JsonConvert.DeserializeObject<BootstrapManifest>(File.ReadAllText(path));
    if (manifest == null)
      throw new InvalidDataException($"{FileName} is empty or invalid.");

    Require(manifest.AssemblyName, nameof(AssemblyName));
    Require(manifest.EntryMethod, nameof(EntryMethod));
    Require(manifest.MinimumAdofaiIpcVersion, nameof(MinimumAdofaiIpcVersion));
    Require(manifest.DownloadUrl, nameof(DownloadUrl));
    Require(manifest.ChecksumUrl, nameof(ChecksumUrl));
    return manifest;
  }

  private static void Require(string value, string propertyName)
  {
    if (string.IsNullOrWhiteSpace(value))
      throw new InvalidDataException($"{FileName} is missing {propertyName}.");
  }
}
