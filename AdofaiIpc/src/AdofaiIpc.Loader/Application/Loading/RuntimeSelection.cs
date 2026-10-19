using System;
using System.Collections.Generic;
using System.Linq;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Loader;

internal static class RuntimeSelection {
  internal static RuntimeCandidate Select(IEnumerable<RuntimeCandidate> candidates) =>
    candidates.Where(candidate => !candidate.Version.IsPrerelease && candidate.Info.ContractMajor == IpcContract.Major &&
      candidate.ContractAssemblyVersion == IpcContract.AssemblyVersion && candidate.Info.WireMajor == IpcContract.WireMajor)
      .OrderByDescending(candidate => candidate.Version)
      .ThenBy(candidate => candidate.AssemblyPath, StringComparer.Ordinal).FirstOrDefault();
  internal static void RequireCompatible(RuntimeInfo info, RuntimeRequirements requirement)
  {
    if (info.ContractMajor != requirement.ContractMajor || info.WireMajor != requirement.WireMajor)
      throw new InvalidOperationException("IPC contract/wire major mismatch. Selected " + info.ContractMajor + "/" + info.WireMajor +
        ", required " + requirement.ContractMajor + "/" + requirement.WireMajor + ".");
    if (SemanticVersion.Parse(info.Version).CompareTo(SemanticVersion.Parse(requirement.MinimumRuntimeVersion)) < 0)
      throw new InvalidOperationException("IPC runtime " + info.Version + " is selected; this mod requires " + requirement.MinimumRuntimeVersion + ". Update the bundled DLLs and restart the game.");
    foreach (string capability in requirement.RequiredCapabilities)
      if (!info.Supports(capability)) throw new InvalidOperationException("Selected IPC runtime lacks required capability: " + capability);
  }
}
