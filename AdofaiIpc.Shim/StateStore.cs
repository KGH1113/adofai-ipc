using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace AdofaiIpc.Shim;

internal sealed class StateStore
{
  private readonly string _installPath;
  private readonly string _statePath;

  public StateStore(string installPath)
  {
    _installPath = Path.GetFullPath(installPath);
    _statePath = Path.Combine(_installPath, "Update", "state.json");
  }

  public RuntimeState Load()
  {
    string backup = _statePath + ".bak";
    if (!File.Exists(_statePath) && File.Exists(backup)) File.Move(backup, _statePath);
    if (!File.Exists(_statePath)) throw new FileNotFoundException("AdofaiIpc update state is missing.", _statePath);

    RuntimeState state = JsonConvert.DeserializeObject<RuntimeState>(File.ReadAllText(_statePath));
    if (state == null || state.SchemaVersion != 1 || string.IsNullOrWhiteSpace(state.Current))
      throw new InvalidDataException("AdofaiIpc update state is invalid.");
    return state;
  }

  public void Save(RuntimeState state)
  {
    string directory = Path.GetDirectoryName(_statePath);
    Directory.CreateDirectory(directory);
    string temporary = _statePath + ".tmp-" + Guid.NewGuid().ToString("N");
    string backup = _statePath + ".bak";
    File.WriteAllText(temporary, JsonConvert.SerializeObject(state, Formatting.Indented) + Environment.NewLine, Encoding.UTF8);
    try
    {
      if (File.Exists(_statePath))
      {
        if (File.Exists(backup)) File.Delete(backup);
        File.Replace(temporary, _statePath, backup, true);
      }
      else
      {
        File.Move(temporary, _statePath);
      }
    }
    finally
    {
      if (File.Exists(temporary)) File.Delete(temporary);
    }
  }

  public string LauncherPath(string version) => ContainedPath("Launcher", version, "AdofaiIpc.Launcher.dll");

  public string RuntimePath(string version) => ContainedPath("Runtime", version, "AdofaiIpc.dll");

  private string ContainedPath(string kind, string version, string fileName)
  {
    if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
        version.Contains("/") || version.Contains("\\") || version == "." || version == "..")
      throw new InvalidDataException("AdofaiIpc state contains an invalid version.");
    return Path.Combine(_installPath, kind, "versions", version, fileName);
  }
}
