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
  private readonly string _path;

  public UpdateStateStore(string installPath) => _path = Path.Combine(installPath, "Update", "state.json");

  public UpdateState Load()
  {
    UpdateState state = JsonConvert.DeserializeObject<UpdateState>(File.ReadAllText(_path));
    if (state == null || state.SchemaVersion != 1 || string.IsNullOrWhiteSpace(state.Current))
      throw new InvalidDataException("AdofaiIpc update state is invalid.");
    return state;
  }

  public void Save(UpdateState state)
  {
    string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
    string backup = _path + ".bak";
    Directory.CreateDirectory(Path.GetDirectoryName(_path));
    File.WriteAllText(temporary, JsonConvert.SerializeObject(state, Formatting.Indented) + Environment.NewLine, Encoding.UTF8);
    try
    {
      if (File.Exists(_path))
      {
        if (File.Exists(backup)) File.Delete(backup);
        File.Replace(temporary, _path, backup, true);
      }
      else File.Move(temporary, _path);
    }
    finally
    {
      if (File.Exists(temporary)) File.Delete(temporary);
    }
  }
}
