using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AdofaiIpc.Contracts;
using Newtonsoft.Json;

namespace AdofaiIpc.Loader;

internal sealed class RuntimeManifest
{
  public int SchemaVersion { get; set; }
  public string RuntimeVersion { get; set; }
  public int ContractMajor { get; set; }
  public string ContractAssemblyVersion { get; set; }
  public int WireMajor { get; set; }
  public string[] Capabilities { get; set; }
  [JsonIgnore] internal string DirectoryPath;
  [JsonIgnore] internal SemanticVersion Version;
  [JsonIgnore] internal string AssemblyPath => Path.Combine(DirectoryPath, "AdofaiIpc.Runtime.dll");

  internal static RuntimeManifest Read(string path)
  {
    if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("IPC manifest exceeds 64 KiB.");
    var manifest = JsonConvert.DeserializeObject<RuntimeManifest>(File.ReadAllText(path),
      new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error, MaxDepth = 16, TypeNameHandling = TypeNameHandling.None });
    if (manifest == null || manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported IPC manifest schema.");
    manifest.Version = SemanticVersion.Parse(manifest.RuntimeVersion);
    if (manifest.ContractMajor < 1 || manifest.WireMajor < 1 || manifest.Capabilities == null ||
      manifest.Capabilities.Any(string.IsNullOrWhiteSpace) || manifest.Capabilities.Distinct(StringComparer.Ordinal).Count() != manifest.Capabilities.Length)
      throw new InvalidDataException("Invalid IPC manifest compatibility metadata.");
    manifest.DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(path));
    AssemblyName assembly = AssemblyName.GetAssemblyName(manifest.AssemblyPath);
    if (assembly.Name != "AdofaiIpc.Runtime") throw new InvalidDataException("Unexpected runtime assembly name.");
    return manifest;
  }

  internal RuntimeInfo ToInfo() => new RuntimeInfo(RuntimeVersion, ContractMajor, WireMajor, Capabilities);

  internal void Verify(RuntimeInfo actual)
  {
    if (actual == null || actual.Version != RuntimeVersion || actual.ContractMajor != ContractMajor || actual.WireMajor != WireMajor ||
      !actual.Capabilities.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(Capabilities.OrderBy(x => x, StringComparer.Ordinal)))
      throw new InvalidDataException("Runtime DLL and manifest compatibility metadata differ.");
  }
}
