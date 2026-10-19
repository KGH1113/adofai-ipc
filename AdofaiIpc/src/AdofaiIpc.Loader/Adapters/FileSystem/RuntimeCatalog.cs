using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AdofaiIpc.Contracts;

namespace AdofaiIpc.Loader;

internal sealed class RuntimeCatalog : IRuntimeCatalog
{
  public IEnumerable<RuntimeCandidate> Discover(string modsDirectory)
  {
    string root = Path.GetFullPath(modsDirectory);
    if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
    foreach (string mod in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.Ordinal))
    {
      string path = Path.Combine(mod, "ipc", "manifest.json");
      if (!File.Exists(path)) continue;
      RuntimeCandidate candidate;
      try { candidate = RuntimeManifest.Read(path); }
      catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException ||
        error is Newtonsoft.Json.JsonException || error is BadImageFormatException || error is ArgumentException)
      { Trace.TraceWarning("Ignoring invalid IPC candidate " + path + ": " + error.Message); continue; }
      yield return candidate;
    }
  }

}
