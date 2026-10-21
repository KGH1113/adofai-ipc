using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Retirement;

// Runs after startup has returned to Unity, never inside the old Shim/Launcher call stack.
internal static class LegacyRetirement
{
  private static readonly string[] Consumers = { "TUFHelperLite", "TUFReplay" };

  internal static bool Ready(string installPath, out string reason)
  {
    reason = null;
    string root = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (!string.Equals(Path.GetFileName(root), "AdofaiIpc", StringComparison.Ordinal))
      throw new InvalidDataException("Refusing to retire a directory other than AdofaiIpc.");
    RejectLinks(root);
    JObject owner = JObject.Parse(File.ReadAllText(Path.Combine(root, "Info.json")));
    // The published updater stages versioned DLLs but deliberately leaves root Info.json
    // unchanged. Its on-disk product version can still be 0.3.0/0.4.0/0.4.1 in this run.
    if ((string)owner["Id"] != "AdofaiIpc" || !Version.TryParse((string)owner["Version"], out Version version) ||
        version < new Version(0, 3, 0) || version > new Version(0, 4, 3))
      throw new InvalidDataException("Refusing to retire an unexpected IPC installation.");
    string mods = Path.GetDirectoryName(root);
    foreach (string mod in Directory.EnumerateDirectories(mods))
    {
      string infoPath = Path.Combine(mod, "Info.json");
      if (!File.Exists(infoPath)) continue;
      JObject info = JObject.Parse(File.ReadAllText(infoPath));
      string id = (string)info["Id"];
      if (Array.IndexOf(Consumers, id) < 0) continue;
      if ((File.GetAttributes(mod) & FileAttributes.ReparsePoint) != 0)
      { reason = id + " is installed through a linked directory"; return false; }
      string entry = (string)info["EntryMethod"];
      string expected = id == "TUFHelperLite" ? "TUFHelperLite.Launcher.EntryPoint.Load" : "TUFReplay.Bootstrap.Bootstrap.Load";
      if ((string)info["Id"] != id || entry != expected)
      { reason = id + " still uses its legacy dependency entry point"; return false; }
      string assemblyName = (string)info["AssemblyName"];
      if (string.IsNullOrWhiteSpace(assemblyName))
      { reason = id + " has no SDK launcher entry point"; return false; }
      string launcher = Path.GetFullPath(Path.Combine(mod, assemblyName));
      if (!launcher.StartsWith(Path.GetFullPath(mod) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(launcher))
      { reason = id + " has not finished installing its SDK launcher"; return false; }
      foreach (string file in new[] { "AdofaiIpc.Contracts.dll", "AdofaiIpc.Loader.dll", "ipc/manifest.json", "ipc/AdofaiIpc.Runtime.dll" })
        if (!File.Exists(Path.Combine(mod, file)))
        { reason = id + " has not finished installing the SDK"; return false; }
    }
    return true;
  }

  internal static void Delete(string installPath)
  {
    if (!Ready(installPath, out string reason)) throw new InvalidOperationException(reason);
    // Preserve the original installation intact if the OS cannot even rename its loaded directory.
    // On systems which lock a mapped DLL, detach the folder from Mods first; locked bytes remain
    // in the private retirement directory and are retried by the SDK consumer on its next launch.
    string root = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    string game = Path.GetDirectoryName(Path.GetDirectoryName(root));
    string retired = Path.Combine(game, ".adofai-ipc-retired");
    Directory.CreateDirectory(retired);
    if ((File.GetAttributes(retired) & FileAttributes.ReparsePoint) != 0)
      throw new InvalidDataException("Refusing to detach IPC into a linked retirement directory.");
    string detached = Path.Combine(retired, "AdofaiIpc-" + Guid.NewGuid().ToString("N"));
    Directory.Move(root, detached);
    try { Directory.Delete(detached, true); }
    catch (IOException) { /* Loaded files may remain locked until the process exits. */ }
    catch (UnauthorizedAccessException) { /* Keep the detached directory for a later retry. */ }
    if (Directory.Exists(root)) throw new IOException("The legacy IPC mod directory was recreated during retirement.");
  }

  private static void RejectLinks(string root)
  {
    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
      throw new InvalidDataException("Refusing to retire a linked IPC directory.");
    foreach (string path in Directory.EnumerateFileSystemEntries(root))
    {
      FileAttributes attributes = File.GetAttributes(path);
      if ((attributes & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Refusing to retire an IPC installation containing a link.");
      if ((attributes & FileAttributes.Directory) != 0) RejectLinks(path);
    }
  }
}
