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
  internal static RuntimeCandidate Read(string path)
  {
    if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("IPC manifest exceeds 64 KiB.");
    var manifest = JsonConvert.DeserializeObject<RuntimeManifest>(File.ReadAllText(path),
      new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error, MaxDepth = 16, TypeNameHandling = TypeNameHandling.None });
    if (manifest == null || manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported IPC manifest schema.");
    SemanticVersion.Parse(manifest.RuntimeVersion);
    if (manifest.ContractMajor < 1 || manifest.WireMajor < 1 || manifest.Capabilities == null ||
      manifest.Capabilities.Any(string.IsNullOrWhiteSpace) || manifest.Capabilities.Distinct(StringComparer.Ordinal).Count() != manifest.Capabilities.Length)
      throw new InvalidDataException("Invalid IPC manifest compatibility metadata.");
    string assemblyPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "AdofaiIpc.Runtime.dll");
    AssemblyName assembly = AssemblyName.GetAssemblyName(assemblyPath);
    if (assembly.Name != "AdofaiIpc.Runtime") throw new InvalidDataException("Unexpected runtime assembly name.");
    return new RuntimeCandidate(new RuntimeInfo(manifest.RuntimeVersion, manifest.ContractMajor, manifest.WireMajor, manifest.Capabilities), manifest.ContractAssemblyVersion, assemblyPath);
  }

}
