using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Launcher;

internal static class InstalledMetadata
{
  internal static void Promote(string installPath, string version)
  {
    SemanticVersion.Parse(version);
    string path = Path.Combine(installPath, "Info.json");
    JObject info = JObject.Parse(File.ReadAllText(path));
    if ((string)info["Id"] != "AdofaiIpc" || (string)info["AssemblyName"] != "AdofaiIpc.Shim.dll" ||
        (string)info["EntryMethod"] != "AdofaiIpc.Shim.Shim.Load")
      throw new InvalidDataException("The installed IPC entry point is invalid.");
    if ((string)info["Version"] == version) return;
    info["Version"] = version;
    string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
    try
    {
      File.WriteAllText(temporary, info.ToString() + Environment.NewLine, new UTF8Encoding(false));
      File.Replace(temporary, path, null);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
  }
}
