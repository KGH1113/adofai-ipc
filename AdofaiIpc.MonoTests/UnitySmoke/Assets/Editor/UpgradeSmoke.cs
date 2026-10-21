using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class UpgradeSmoke
{
    private static string _install, _version, _result;
    private static double _deadline;
    private static object _owner;
    private static Assembly _runtime;
    private static bool _started;

    public static void Start()
    {
        try
        {
            var oldRoot = Environment.GetEnvironmentVariable("IPC_SMOKE_OLD_ROOT");
            var oldVersion = Environment.GetEnvironmentVariable("IPC_SMOKE_OLD_VERSION");
            var candidate = Environment.GetEnvironmentVariable("IPC_SMOKE_CANDIDATE");
            var managed = Environment.GetEnvironmentVariable("IPC_SMOKE_MANAGED");
            _version = Environment.GetEnvironmentVariable("IPC_SMOKE_VERSION");
            _result = Environment.GetEnvironmentVariable("IPC_SMOKE_RESULT");
            _install = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Fixture", "Mods", "AdofaiIpc");
            var fixture = Path.GetDirectoryName(Path.GetDirectoryName(_install));
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
            Copy(oldRoot, _install);
            Copy(Path.Combine(candidate, "Launcher", "versions", _version), Path.Combine(_install, "Launcher", "versions", _version));
            Copy(Path.Combine(candidate, "Runtime", "versions", _version), Path.Combine(_install, "Runtime", "versions", _version));
            File.WriteAllText(Path.Combine(_install, "Update", "state.json"),
                "{\"SchemaVersion\":1,\"Current\":\"" + oldVersion + "\",\"Previous\":null,\"Trial\":\"" + _version + "\"}");
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                var name = new AssemblyName(request.Name).Name;
                if (name != "UnityModManager" && name != "Newtonsoft.Json") return null;
                var path = name == "UnityModManager" ? Path.Combine(managed, "UnityModManager", name + ".dll") : Path.Combine(managed, name + ".dll");
                return Assembly.LoadFrom(path);
            };
            // Reproduce the previously failing binding in the same AppDomain.
            Assembly.LoadFrom(Path.Combine(_install, "Launcher", "versions", oldVersion, "AdofaiIpc.Launcher.dll"));
            Assembly.LoadFrom(Path.Combine(_install, "Runtime", "versions", oldVersion, "AdofaiIpc.dll"));
            var umm = Assembly.LoadFrom(Path.Combine(managed, "UnityModManager", "UnityModManager.dll"));
            var json = Assembly.LoadFrom(Path.Combine(managed, "Newtonsoft.Json.dll")).GetType("Newtonsoft.Json.JsonConvert", true);
            var infoType = umm.GetType("UnityModManagerNet.UnityModManager+ModInfo", true);
            var entryType = umm.GetType("UnityModManagerNet.UnityModManager+ModEntry", true);
            var info = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
                .Invoke(null, new object[] { File.ReadAllText(Path.Combine(_install, "Info.json")), infoType });
            _owner = Activator.CreateInstance(entryType, new object[] { info, _install + Path.DirectorySeparatorChar });
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Load(); };
            _deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Load()
    {
        try
        {
            var shim = Assembly.LoadFrom(Path.Combine(_install, "AdofaiIpc.Shim.dll"));
            var loaded = (bool)shim.GetType("AdofaiIpc.Shim.Shim", true).GetMethod("Load").Invoke(null, new[] { _owner });
            if (!loaded) throw new Exception("Published Shim.Load returned false.");
            _runtime = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "AdofaiIpc" && a.GetName().Version.ToString() == _version + ".0");
            var host = GameObject.Find("AdofaiIpc Main Thread Dispatcher");
            if (host == null || !host.GetComponents<Component>().Any(c => c != null && c.GetType().Assembly == _runtime))
                throw new Exception("The new runtime did not create its actual Unity dispatcher component.");
            if (!File.ReadAllText(Path.Combine(_install, "Info.json")).Contains("\"Version\": \"" + _version + "\""))
                throw new Exception("The real runtime load did not synchronize Info.json.");
            if (!File.ReadAllText(Path.Combine(_install, "Update", "state.json")).Contains("\"Current\": \"" + _version + "\""))
                throw new Exception("The published shim did not promote the runtime state.");
            _started = true;
            _deadline = EditorApplication.timeSinceStartup + 20;
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Poll()
    {
        if (_started && !Directory.Exists(_install)) { Finish(null); return; }
        if (EditorApplication.timeSinceStartup > _deadline) { Finish(new Exception("Timed out waiting for Unity startup/removal.")); return; }
        EditorApplication.QueuePlayerLoopUpdate();
    }

    private static void Finish(Exception error)
    {
        EditorApplication.update -= Poll;
        while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
        if (error == null)
        {
            Debug.Log("PASS actual Unity Play Mode: old DLL preload -> published Shim -> real runtime " + _version + " -> Info/state promotion -> frame-driven folder removal");
            File.WriteAllText(_result, "{\"passed\":true,\"version\":\"" + _version + "\",\"unity\":\"" + Application.unityVersion + "\",\"gameE2E\":false}");
            try { _runtime.GetType("AdofaiIpc.Main", true).GetMethod("Rollback").Invoke(null, new[] { _owner }); } catch { }
        }
        else { Debug.LogException(error); if (!string.IsNullOrEmpty(_result)) File.WriteAllText(_result, "{\"passed\":false}"); }
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var path in Directory.GetFiles(source)) File.Copy(path, Path.Combine(target, Path.GetFileName(path)), true);
        foreach (var path in Directory.GetDirectories(source)) Copy(path, Path.Combine(target, Path.GetFileName(path)));
    }
}
