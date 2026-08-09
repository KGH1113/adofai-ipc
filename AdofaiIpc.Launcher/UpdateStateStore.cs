using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace AdofaiIpc.Launcher;

internal sealed class UpdateState
{
  public int SchemaVersion { get; set; } = 1;
  public string Current { get; set; }
  public string Previous { get; set; }
  public string Trial { get; set; }
}

internal sealed class UpdateStateStore
{
  private readonly string _statePath;
  public UpdateStateStore(string installPath) =>
    _statePath = Path.Combine(Path.GetFullPath(installPath), "Update", "state.json");

  public UpdateState Load()
  {
    try { return Read(_statePath); }
    catch
    {
      string backup = _statePath + ".bak";
      if (!File.Exists(backup)) throw;
      UpdateState state = Read(backup);
      Save(state);
      return state;
    }
  }

  public void Save(UpdateState state)
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

  private static UpdateState Read(string path)
  {
    if (!File.Exists(path)) throw new FileNotFoundException("AdofaiIpc update state is missing.", path);
    UpdateState state = JsonConvert.DeserializeObject<UpdateState>(File.ReadAllText(path));
    Validate(state);
    return state;
  }

  private static void Validate(UpdateState state)
  {
    if (state == null || state.SchemaVersion != 1 || !SemanticVersion.TryParse(state.Current, out _))
      throw new InvalidDataException("AdofaiIpc update state is invalid.");
    if (!string.IsNullOrEmpty(state.Previous) && !SemanticVersion.TryParse(state.Previous, out _) ||
        !string.IsNullOrEmpty(state.Trial) && !SemanticVersion.TryParse(state.Trial, out _))
      throw new InvalidDataException("AdofaiIpc update state contains an invalid version.");
  }
}
