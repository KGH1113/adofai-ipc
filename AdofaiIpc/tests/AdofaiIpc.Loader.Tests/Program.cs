using System.Diagnostics;
using System.Reflection;
using AdofaiIpc.Contracts;
using AdofaiIpc.Loader;
using Newtonsoft.Json;

internal static class Program
{
  static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
  static void Refuses(Action action)
  {
    try { action(); } catch (InvalidOperationException) { return; }
    throw new Exception("An incompatible runtime was accepted.");
  }
  static int Main(string[] args)
  {
    if (args.Length == 1)
    {
      foreach (string scenario in new[] { "selection", "frozen", "requirements", "capabilities", "manifest-mismatch", "invalid-candidate", "prerelease", "preloaded", "duplicate-contract", "parallel" })
      {
        var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(args[0]); start.ArgumentList.Add(scenario);
        using var child = Process.Start(start); child.WaitForExit();
        if (child.ExitCode != 0) return child.ExitCode;
      }
      return 0;
    }
    string fixtureRoot = args[0], scenarioName = args[1];
    string root = Path.Combine(Path.GetTempPath(), "adofai-ipc-v2-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      Bundle(root, "a-old", fixtureRoot, "2.0.0");
      if (scenarioName != "frozen") Bundle(root, "z-new", fixtureRoot, "2.1.0");
      switch (scenarioName)
      {
        case "selection":
          var selected = IpcLoader.GetRuntime(root);
          Check(selected.Info.Version == "2.1.0", "load order chose older runtime");
          Assembly copy = Assembly.Load(File.ReadAllBytes(typeof(IpcLoader).Assembly.Location));
          object shared = copy.GetType(typeof(IpcLoader).FullName).GetMethod("GetRuntime").Invoke(null, new object[] { root, new RuntimeRequirements() });
          Check(ReferenceEquals(shared, selected), "separate loader copy started another runtime");
          break;
        case "frozen":
          var first = IpcLoader.GetRuntime(root);
          Bundle(root, "z-new", fixtureRoot, "2.1.0");
          Check(ReferenceEquals(first, IpcLoader.GetRuntime(root)), "runtime replaced after startup");
          Refuses(() => IpcLoader.GetRuntime(root, new RuntimeRequirements("2.1.0")));
          Check(ReferenceEquals(first, IpcLoader.GetRuntime(root)), "incompatible late mod poisoned host");
          break;
        case "requirements":
          Refuses(() => IpcLoader.GetRuntime(root, new RuntimeRequirements(contractMajor: 2)));
          Refuses(() => IpcLoader.GetRuntime(root, new RuntimeRequirements(wireMajor: 4)));
          Refuses(() => IpcLoader.GetRuntime(root, new RuntimeRequirements("2.2.0")));
          Check(IpcLoader.GetRuntime(root).Info.Version == "2.1.0", "rejected requirements locked startup");
          break;
        case "capabilities":
          Refuses(() => IpcLoader.GetRuntime(root, new RuntimeRequirements(requiredCapabilities: new[] { "missing" })));
          Check(IpcLoader.GetRuntime(root, new RuntimeRequirements(requiredCapabilities: new[] { "commands" })).Info.Supports("commands"), "required capability missing");
          break;
        case "manifest-mismatch":
          Manifest(root, "z-new", "2.1.0", new[] { "commands", "invented" });
          Refuses(() => IpcLoader.GetRuntime(root));
          Directory.Delete(Path.Combine(root, "z-new"), true);
          Refuses(() => IpcLoader.GetRuntime(root));
          break;
        case "invalid-candidate":
          File.WriteAllText(Path.Combine(root, "z-new", "ipc", "manifest.json"), "not JSON");
          Check(IpcLoader.GetRuntime(root).Info.Version == "2.0.0", "invalid candidate blocked valid runtime");
          break;
        case "prerelease":
          Manifest(root, "z-new", "3.0.0-beta.1", new[] { "commands" });
          Check(IpcLoader.GetRuntime(root).Info.Version == "2.0.0", "prerelease selected automatically");
          break;
        case "preloaded":
          Assembly.LoadFrom(Path.Combine(root, "a-old", "ipc", "AdofaiIpc.Runtime.dll"));
          Refuses(() => IpcLoader.GetRuntime(root));
          break;
        case "duplicate-contract":
          Assembly.Load(File.ReadAllBytes(typeof(IIpcRuntime).Assembly.Location));
          Refuses(() => IpcLoader.GetRuntime(root));
          break;
        case "parallel":
          var instances = new IIpcRuntime[16];
          Parallel.For(0, instances.Length, i => instances[i] = IpcLoader.GetRuntime(root));
          Check(instances.All(value => ReferenceEquals(value, instances[0])), "concurrent calls started different hosts");
          break;
      }
      Check(typeof(IIpcRuntime).Assembly.GetName().Version.ToString() == "1.0.0.0", "ABI identity changed with product version");
      Console.WriteLine("PASS v2 loader " + scenarioName);
      return 0;
    }
    finally { Directory.Delete(root, true); }
  }
  static void Bundle(string root, string mod, string fixtures, string version)
  {
    string directory = Path.Combine(root, mod, "ipc"); Directory.CreateDirectory(directory);
    File.Copy(Path.Combine(fixtures, version, "AdofaiIpc.Runtime.dll"), Path.Combine(directory, "AdofaiIpc.Runtime.dll"));
    Manifest(root, mod, version, new[] { "commands" });
  }
  static void Manifest(string root, string mod, string version, string[] capabilities) => File.WriteAllText(
    Path.Combine(root, mod, "ipc", "manifest.json"), JsonConvert.SerializeObject(new {
      SchemaVersion = 1, RuntimeVersion = version, ContractMajor = 1, ContractAssemblyVersion = "1.0.0.0", WireMajor = 3, Capabilities = capabilities }));
}
