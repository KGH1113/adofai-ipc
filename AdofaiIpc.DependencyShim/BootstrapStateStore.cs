using System;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace AdofaiIpc.DependencyShim;

internal static class BootstrapStateStore
{
  private const string RelativeDirectory = "DependencyBootstrap";
  private static readonly Regex CanonicalVersion = new(
    @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$",
    RegexOptions.CultureInvariant);

  public static BootstrapState Read(string modRoot)
  {
    string path = StatePath(modRoot);
    try { return ReadFile(path); }
    catch (Exception)
    {
      string backup = path + ".bak";
      if (!File.Exists(backup)) throw;
      BootstrapState recovered = ReadFile(backup);
      string recovery = path + ".recover-" + Guid.NewGuid().ToString("N");
      File.Copy(backup, recovery, false);
      if (File.Exists(path)) File.Replace(recovery, path, null);
      else File.Move(recovery, path);
      return recovered;
    }
  }

  public static void Write(string modRoot, BootstrapState state)
  {
    if (state == null || state.SchemaVersion != 1)
      throw new InvalidDataException("Unsupported dependency bootstrap state.");

    string path = StatePath(modRoot);
    string directory = Path.GetDirectoryName(path);
    Directory.CreateDirectory(directory);
    string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(temporary, JsonConvert.SerializeObject(state, Formatting.Indented));
    if (File.Exists(path))
    {
      string backup = path + ".bak";
      File.Replace(temporary, path, backup);
    }
    else File.Move(temporary, path);
  }

  public static string CandidatePath(string modRoot, string version)
  {
    ValidateVersion(version);
    return Path.Combine(Path.GetFullPath(modRoot), RelativeDirectory, "versions", version,
      "AdofaiIpc.Bootstrap.dll");
  }

  private static BootstrapState ReadFile(string path)
  {
    if (!File.Exists(path)) throw new FileNotFoundException("Dependency bootstrap state is missing.", path);
    BootstrapState state = JsonConvert.DeserializeObject<BootstrapState>(File.ReadAllText(path));
    if (state == null || state.SchemaVersion != 1 || string.IsNullOrWhiteSpace(state.Current))
      throw new InvalidDataException("Dependency bootstrap state is invalid.");
    ValidateVersion(state.Current);
    if (!string.IsNullOrEmpty(state.Previous)) ValidateVersion(state.Previous);
    if (!string.IsNullOrEmpty(state.Trial)) ValidateVersion(state.Trial);
    return state;
  }

  private static string StatePath(string modRoot) =>
    Path.Combine(Path.GetFullPath(modRoot), RelativeDirectory, "state.json");

  internal static void ValidateVersion(string value)
  {
    if (string.IsNullOrWhiteSpace(value) || !CanonicalVersion.IsMatch(value))
      throw new InvalidDataException("Dependency bootstrap version is invalid.");
  }
}
