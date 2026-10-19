using System;
using System.IO;
using System.Linq;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Loader;
internal sealed class RuntimeCandidate {
  internal RuntimeInfo Info { get; }
  internal SemanticVersion Version { get; }
  internal string ContractAssemblyVersion { get; }
  internal string AssemblyPath { get; }
  internal RuntimeCandidate(RuntimeInfo info, string contractAssemblyVersion, string assemblyPath) {
    Info = info; Version = SemanticVersion.Parse(info.Version); ContractAssemblyVersion = contractAssemblyVersion; AssemblyPath = assemblyPath;
  }
  internal void Verify(RuntimeInfo actual) {
    if (actual == null || actual.Version != Info.Version || actual.ContractMajor != Info.ContractMajor || actual.WireMajor != Info.WireMajor ||
      !actual.Capabilities.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(Info.Capabilities.OrderBy(x => x, StringComparer.Ordinal)))
      throw new InvalidDataException("Runtime DLL and manifest compatibility metadata differ.");
  }
}
