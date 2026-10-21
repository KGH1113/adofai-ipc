using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

// Run in standalone Unity Mono, in one AppDomain with the published old DLLs.
internal static class AssemblyUpgrade
{
  private static readonly BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
  private static int Main(string[] args)
  {
    string temporary = Path.Combine(Path.GetTempPath(), "adofai-ipc-mono-" + Guid.NewGuid().ToString("N"));
    try
    {
      string oldRoot = Path.GetFullPath(args[0]);
      string oldVersion = args[1];
      string zip = Path.GetFullPath(args[2]);
      string managed = Path.GetFullPath(args[3]);
      string version = args[4];
      AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) {
        string name = new AssemblyName(request.Name).Name + ".dll";
        string file = Path.Combine(managed, name);
        if (!File.Exists(file)) file = Path.Combine(managed, "UnityModManager", name);
        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
      };
      Directory.CreateDirectory(temporary);
      Assembly oldLauncher = Assembly.LoadFrom(Path.Combine(oldRoot, "Launcher", "versions", oldVersion, "AdofaiIpc.Launcher.dll"));
      Assembly oldRuntime = Assembly.LoadFrom(Path.Combine(oldRoot, "Runtime", "versions", oldVersion, "AdofaiIpc.dll"));
      Type manifestType = oldLauncher.GetType("AdofaiIpc.Launcher.UpdateManifest", true);
      object manifest = Activator.CreateInstance(manifestType, true);
      Set(manifest, "SchemaVersion", 1); Set(manifest, "Version", version);
      Set(manifest, "PackageSize", new FileInfo(zip).Length);
      using (SHA256 hash = SHA256.Create())
      using (FileStream input = File.OpenRead(zip))
        Set(manifest, "Sha256", string.Concat(hash.ComputeHash(input).Select(b => b.ToString("x2"))));
      Set(manifest, "PackageUrl", "https://github.com/KGH1113/adofai-ipc/releases/download/v" + version + "/AdofaiIpc.zip");
      Type installer = oldLauncher.GetType("AdofaiIpc.Launcher.PackageInstaller", true);
      Call(installer, "VerifyPackage", zip, manifest);
      string extracted = (string)Call(installer, "ExtractAndValidate", zip, Path.Combine(temporary, "package"), manifest);
      string installed = Path.Combine(temporary, "Mods", "AdofaiIpc");
      Directory.CreateDirectory(Path.Combine(installed, "Update"));
      File.Copy(Path.Combine(oldRoot, "Info.json"), Path.Combine(installed, "Info.json"));
      File.WriteAllText(Path.Combine(installed, "Update", "state.json"),
        "{\"SchemaVersion\":1,\"Current\":\"" + oldVersion + "\",\"Previous\":null,\"Trial\":null}");
      Call(installer, "InstallCandidate", installed, extracted, manifest);

      string launcherPath = Path.Combine(installed, "Launcher", "versions", version, "AdofaiIpc.Launcher.dll");
      Assembly launcher = Assembly.LoadFrom(launcherPath);
      Assert(!object.ReferenceEquals(oldLauncher, launcher), "Mono reused the published old launcher.");
      Assert(launcher.GetName().Version.ToString() == version + ".0", "The wrong launcher version was selected.");
      Assert(launcher.GetName().GetPublicKeyToken().Length != 0, "The new launcher has no distinct assembly identity.");
      Assert((string)Call(launcher.GetType("AdofaiIpc.Launcher.EntryPoint", true), "ResolveVersion") == version,
        "The actual candidate launcher's code did not execute.");
      Type identity = launcher.GetType("AdofaiIpc.Launcher.AssemblyIdentity", true);
      string runtimePath = Path.Combine(installed, "Runtime", "versions", version, "AdofaiIpc.dll");
      Assembly runtime = (Assembly)Call(identity, "LoadExact", runtimePath);
      Assert(!object.ReferenceEquals(oldRuntime, runtime), "Mono reused the published old runtime.");
      Assert(runtime.GetName().Version.ToString() == version + ".0", "The wrong runtime version was selected.");
      Console.WriteLine("PASS Mono " + oldVersion + " -> " + version + " selects and executes the real new assemblies");

      Type json = Assembly.LoadFrom(Path.Combine(managed, "Newtonsoft.Json.dll")).GetType("Newtonsoft.Json.JsonConvert", true);
      Assembly umm = Assembly.LoadFrom(Path.Combine(managed, "UnityModManager", "UnityModManager.dll"));
      Type infoType = umm.GetType("UnityModManagerNet.UnityModManager+ModInfo", true);
      Type entryType = umm.GetType("UnityModManagerNet.UnityModManager+ModEntry", true);
      object info = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
        .Invoke(null, new object[] { File.ReadAllText(Path.Combine(installed, "Info.json")), infoType });
      object owner = Activator.CreateInstance(entryType, new object[] { info, installed + Path.DirectorySeparatorChar });
      Call(oldLauncher.GetType("AdofaiIpc.Launcher.UpdateCoordinator", true), "Commit", owner, version);
      Assembly shim = Assembly.LoadFrom(Path.Combine(oldRoot, "AdofaiIpc.Shim.dll"));
      Type storeType = shim.GetType("AdofaiIpc.Shim.StateStore", true);
      object store = Activator.CreateInstance(storeType, new object[] { installed });
      object state = storeType.GetMethod("Load").Invoke(store, null);
      Call(shim.GetType("AdofaiIpc.Shim.Shim", true), "Promote", store, state, version);
      Assert(File.ReadAllText(Path.Combine(installed, "Update", "state.json")).Contains("\"Current\": \"" + version + "\""),
        "The published launcher/shim did not commit the new state.");
      Call(launcher.GetType("AdofaiIpc.Launcher.InstalledMetadata", true), "Promote", installed, version);
      Assert(File.ReadAllText(Path.Combine(installed, "Info.json")).Contains("\"Version\": \"" + version + "\""),
        "Info.json was not synchronized.");
      Console.WriteLine("PASS published updater/shim state commit and Info.json promotion");
      Type retirement = runtime.GetType("AdofaiIpc.Retirement.LegacyRetirement", true);
      Call(retirement, "Delete", installed);
      Assert(!Directory.Exists(installed), "The new runtime did not remove the standalone folder.");
      Assert((string)Call(launcher.GetType("AdofaiIpc.Launcher.EntryPoint", true), "ResolveVersion") == version,
        "Loaded candidate code stopped working after deletion.");
      Console.WriteLine("PASS actual new runtime removal after old Shim commit; loaded code continues executing");
      return 0;
    }
    catch (Exception error)
    {
      while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
      Console.Error.WriteLine(error); return 1;
    }
    finally { try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); } catch { } }
  }
  private static void Set(object target, string name, object value) { target.GetType().GetProperty(name).SetValue(target, value, null); }
  private static object Call(Type type, string name, params object[] arguments) { return type.GetMethod(name, AllStatic).Invoke(null, arguments); }
  private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
