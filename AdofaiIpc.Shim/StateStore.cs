using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AdofaiIpc.Shim;

internal sealed class StateStore
{
  private static readonly Regex VersionPattern = new(
    @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$",
    RegexOptions.CultureInvariant);

  private readonly string _installPath;
  private readonly string _statePath;

  public StateStore(string installPath)
  {
    _installPath = Path.GetFullPath(installPath);
    _statePath = Path.Combine(_installPath, "Update", "state.json");
  }

  public RuntimeState Load()
  {
    try { return Read(_statePath); }
    catch
    {
      string backup = _statePath + ".bak";
      if (!File.Exists(backup)) throw;
      RuntimeState recovered = Read(backup);
      Save(recovered);
      return recovered;
    }
  }

  public void Save(RuntimeState state)
  {
    Validate(state);
    Directory.CreateDirectory(Path.GetDirectoryName(_statePath));
    string temporary = _statePath + ".tmp-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(temporary, JsonConvert.SerializeObject(state, Formatting.Indented) + Environment.NewLine,
      new UTF8Encoding(false));
    try
    {
      if (File.Exists(_statePath)) File.Replace(temporary, _statePath, _statePath + ".bak", true);
      else File.Move(temporary, _statePath);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
  }

  public string LauncherPath(string version) =>
    ContainedPath("Launcher", version, "AdofaiIpc.Launcher.dll");

  private RuntimeState Read(string path)
  {
    if (!File.Exists(path)) throw new FileNotFoundException("AdofaiIpc update state is missing.", path);
    RuntimeState state = JsonConvert.DeserializeObject<RuntimeState>(File.ReadAllText(path));
    Validate(state);
    return state;
  }

  private static void Validate(RuntimeState state)
  {
    if (state == null || state.SchemaVersion != 1 || !ValidVersion(state.Current))
      throw new InvalidDataException("AdofaiIpc update state is invalid.");
    if (!string.IsNullOrEmpty(state.Previous) && !ValidVersion(state.Previous) ||
        !string.IsNullOrEmpty(state.Trial) && !ValidVersion(state.Trial))
      throw new InvalidDataException("AdofaiIpc update state contains an invalid version.");
  }

  private string ContainedPath(string kind, string version, string fileName)
  {
    if (!ValidVersion(version)) throw new InvalidDataException("AdofaiIpc state contains an invalid version.");
    return Path.Combine(_installPath, kind, "versions", version, fileName);
  }

  private static bool ValidVersion(string version) =>
    !string.IsNullOrWhiteSpace(version) && VersionPattern.IsMatch(version);
}
