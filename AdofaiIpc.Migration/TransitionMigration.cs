using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AdofaiIpc.SharedUi;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityModManagerNet;

namespace AdofaiIpc.Migration;

public static class TransitionMigration
{
  private const string RegistryKey = "AdofaiIpc.DependencyIssues.v1";
  private const string RootName = "[AdofaiIpc] Dependency Error UI v1";
  private const string ReleaseUrl = "https://github.com/KGH1113/adofai-ipc/releases/latest";

  public static bool PrepareAndNotify(UnityModManager.ModEntry owner)
    => PrepareAndNotify(owner, null);

  public static bool PrepareAndNotify(UnityModManager.ModEntry owner, string sourceDirectory)
  {
    if (!Prepare(owner, sourceDirectory, out string version)) return false;
    string installed = UnityModManager.modEntries
      .FirstOrDefault(entry => entry.Info.Id == "AdofaiIpc")?.Info.Version ?? string.Empty;
    Report(owner, version, installed);
    Present(owner.Logger);
    owner.Info.DisplayName = owner.Info.Id + " <color=yellow>[Restart required]</color>";
    owner.Logger.Warning(RequiresIpcReinstall(installed, version)
      ? "AdofaiIpc must be reinstalled once. This mod will start after the game is restarted."
      : "The dependency migration is ready. This mod will start after the game is restarted.");
    return true;
  }

  public static bool Prepare(UnityModManager.ModEntry owner) => Prepare(owner, null, out _);
  public static bool Prepare(UnityModManager.ModEntry owner, string sourceDirectory)
    => Prepare(owner, sourceDirectory, out _);

  private static bool Prepare(UnityModManager.ModEntry owner, string sourceDirectory, out string version)
  {
    version = null;
    string infoPath = Path.Combine(owner.Path, "Info.json");
    JObject info = JObject.Parse(File.ReadAllText(infoPath));
    if ((string)info["EntryMethod"] == "AdofaiIpc.DependencyShim.DependencyShim.Load" &&
        IsPrepared(owner.Path))
    {
      if ((string)info["AssemblyName"] != "AdofaiIpc.DependencyShim.dll")
      {
        info["AssemblyName"] = "AdofaiIpc.DependencyShim.dll";
        WriteInfo(infoPath, info);
      }
      return false;
    }

    if (string.IsNullOrWhiteSpace(sourceDirectory))
      sourceDirectory = Path.GetDirectoryName(typeof(TransitionMigration).Assembly.Location);
    if (string.IsNullOrWhiteSpace(sourceDirectory))
      throw new InvalidOperationException("AdofaiIpc migration source directory is unavailable.");
    string shimPath = Path.Combine(sourceDirectory, "AdofaiIpc.DependencyShim.dll");
    string bootstrapPath = Path.Combine(sourceDirectory, "AdofaiIpc.Bootstrap.dll");
    string manifestPath = Path.Combine(sourceDirectory, "AdofaiIpcBootstrap.json");
    if (!File.Exists(shimPath) || !File.Exists(bootstrapPath) || !File.Exists(manifestPath))
      throw new FileNotFoundException("AdofaiIpc migration payload is incomplete.");

    Assembly shim = Assembly.LoadFrom(shimPath);
    Type type = shim.GetType("AdofaiIpc.DependencyShim.DependencyShim", true);
    MethodInfo seed = type.GetMethod("Seed", BindingFlags.Public | BindingFlags.Static, null,
      new[] { typeof(UnityModManager.ModEntry), typeof(string), typeof(string) }, null) ??
      throw new MissingMethodException(type.FullName, "Seed");
    version = (string)seed.Invoke(null, new object[] { owner, bootstrapPath, manifestPath });
    return true;
  }

  private static bool IsPrepared(string modRoot)
  {
    try
    {
      if (!File.Exists(Path.Combine(modRoot, "AdofaiIpc.DependencyShim.dll"))) return false;
      JObject state = JObject.Parse(File.ReadAllText(Path.Combine(modRoot, "DependencyBootstrap", "state.json")));
      string current = (string)state["Current"];
      return (int?)state["SchemaVersion"] == 1 && !string.IsNullOrWhiteSpace(current) &&
             File.Exists(Path.Combine(modRoot, "DependencyBootstrap", "versions", current,
               "AdofaiIpc.Bootstrap.dll"));
    }
    catch { return false; }
  }

  private static void WriteInfo(string path, JObject info)
  {
    string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(temporary, info.ToString());
    if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
    else File.Move(temporary, path);
  }

  private static bool RequiresIpcReinstall(string installedValue, string minimumValue)
  {
    if (string.IsNullOrWhiteSpace(installedValue) || installedValue.Contains("-")) return true;
    return !Version.TryParse(installedValue, out Version installed) ||
           !Version.TryParse(minimumValue, out Version minimum) || installed < minimum;
  }

  private static void Report(UnityModManager.ModEntry owner, string minimum, string installed)
  {
    lock (AppDomain.CurrentDomain)
    {
      Hashtable root = AppDomain.CurrentDomain.GetData(RegistryKey) as Hashtable;
      if (root == null)
      {
        root = new Hashtable(StringComparer.Ordinal) { ["issues"] = new Hashtable(StringComparer.Ordinal) };
        AppDomain.CurrentDomain.SetData(RegistryKey, root);
      }
      Hashtable issues = (Hashtable)root["issues"];
      issues[owner.Info.Id] = new Hashtable(StringComparer.Ordinal)
      {
        ["kind"] = "MigrationRequired", ["modId"] = owner.Info.Id,
        ["displayName"] = string.IsNullOrWhiteSpace(owner.Info.DisplayName) ? owner.Info.Id : owner.Info.DisplayName,
        ["minimumVersion"] = minimum, ["installedVersion"] = installed, ["detail"] = "One-time reinstall required"
      };
    }
  }

  private static void Present(UnityModManager.ModEntry.ModLogger logger)
  {
    try
    {
      GameObject root = GameObject.Find(RootName);
      if (root != null) { root.SendMessage("RefreshFromSharedRegistry", SendMessageOptions.DontRequireReceiver); return; }
      root = new GameObject(RootName);
      UnityEngine.Object.DontDestroyOnLoad(root);
      Canvas canvas = root.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = short.MaxValue;
      DependencyDialogUi.ConfigureCanvas(root.AddComponent<CanvasScaler>());
      root.AddComponent<GraphicRaycaster>();
      root.AddComponent<MigrationView>().Build();
      lock (AppDomain.CurrentDomain)
      {
        Hashtable registry = (Hashtable)AppDomain.CurrentDomain.GetData(RegistryKey);
        registry["uiRoot"] = new WeakReference(root);
      }
    }
    catch (Exception exception) { logger.Error("Could not display the AdofaiIpc migration UI: " + exception); }
  }

  private sealed class MigrationView : MonoBehaviour
  {
    private Text _badge;
    private Text _description;
    private Text _title;
    private DependencyDialogModList _modList;
    private DependencyDialogSteps _steps;
    private Canvas _canvas;
    private bool _closed;
    private bool _fontRetryRegistered;

    public void Build()
    {
      _canvas = GetComponent<Canvas>();
      Image blocker = DependencyDialogUi.Image("Input blocker", transform, DependencyDialogUi.Backdrop);
      DependencyDialogUi.Stretch(blocker.rectTransform);
      Image shadow = DependencyDialogUi.Image("Shadow", blocker.transform, DependencyDialogUi.Shadow, true);
      DependencyDialogUi.Center(shadow.rectTransform, 692f, 508f, -9f);
      Image border = DependencyDialogUi.Image("Border", blocker.transform, DependencyDialogUi.Border, true);
      DependencyDialogUi.Center(border.rectTransform, 684f, 500f);
      Image panel = DependencyDialogUi.Image("Panel", border.transform, DependencyDialogUi.Surface, true);
      DependencyDialogUi.Stretch(panel.rectTransform);
      panel.rectTransform.offsetMin = new Vector2(2f, 2f);
      panel.rectTransform.offsetMax = new Vector2(-2f, -2f);
      Image accent = DependencyDialogUi.Image("Accent", panel.transform, DependencyDialogUi.Warning, true);
      DependencyDialogUi.Place(accent.rectTransform, 0f, 0f, 680f, 4f);
      Image badge = DependencyDialogUi.Image("Warning badge", panel.transform,
        new Color(1f, .69f, .25f, .14f), true);
      DependencyDialogUi.Place(badge.rectTransform, 30f, 28f, 46f, 46f);
      _badge = DependencyDialogUi.Text("Warning mark", badge.transform, 25, FontStyle.Bold,
        TextAnchor.MiddleCenter, DependencyDialogUi.Warning);
      _badge.text = "!";
      DependencyDialogUi.Stretch(_badge.rectTransform);
      _title = DependencyDialogUi.Text("Title", panel.transform, 27, FontStyle.Bold,
        TextAnchor.MiddleLeft, DependencyDialogUi.PrimaryText);
      DependencyDialogUi.Place(_title.rectTransform, 92f, 25f, 540f, 36f);
      _description = DependencyDialogUi.Text("Description", panel.transform, 15, FontStyle.Normal,
        TextAnchor.UpperLeft, DependencyDialogUi.SecondaryText);
      DependencyDialogUi.Place(_description.rectTransform, 92f, 62f, 540f, 46f);
      Image divider = DependencyDialogUi.Image("Divider", panel.transform, new Color(.24f, .28f, .36f, .72f));
      DependencyDialogUi.Place(divider.rectTransform, 30f, 120f, 620f, 1f);
      Text section = DependencyDialogUi.Text("Affected mods label", panel.transform, 12, FontStyle.Bold,
        TextAnchor.MiddleLeft, DependencyDialogUi.MutedText);
      section.text = Korean() ? "영향받는 모드" : "AFFECTED MODS";
      DependencyDialogUi.Place(section.rectTransform, 32f, 136f, 300f, 20f);
      Image modsPanel = DependencyDialogUi.Image("Affected mods", panel.transform,
        DependencyDialogUi.Raised, true);
      DependencyDialogUi.Place(modsPanel.rectTransform, 30f, 160f, 620f, 140f);
      _modList = new DependencyDialogModList(modsPanel.transform);
      Image guide = DependencyDialogUi.Image("Next step", panel.transform,
        new Color(.14f, .25f, .42f, .72f), true);
      DependencyDialogUi.Place(guide.rectTransform, 30f, 316f, 620f, 92f);
      Image guideAccent = DependencyDialogUi.Image("Next step accent", guide.transform,
        DependencyDialogUi.Accent, true);
      DependencyDialogUi.Place(guideAccent.rectTransform, 0f, 0f, 4f, 92f);
      _steps = new DependencyDialogSteps(guide.transform);
      Button close = DependencyDialogUi.Button(Korean() ? "닫기" : "Close", panel.transform, false);
      DependencyDialogUi.Place(close.GetComponent<RectTransform>(), 344f, 430f, 108f, 42f);
      Button download = DependencyDialogUi.Button(
        Korean() ? "AdofaiIPC 다운로드" : "Download AdofaiIPC", panel.transform, true);
      DependencyDialogUi.Place(download.GetComponent<RectTransform>(), 464f, 430f, 186f, 42f);
      download.onClick.AddListener(() => Application.OpenURL(ReleaseUrl));
      close.onClick.AddListener(() => { _closed = true; _canvas.enabled = false; });
      if (Resources.FindObjectsOfTypeAll<EventSystem>().Length == 0)
      { gameObject.AddComponent<EventSystem>(); gameObject.AddComponent<StandaloneInputModule>(); }
      RefreshFromSharedRegistry();
    }

    public void RefreshFromSharedRegistry()
    {
      if (_modList == null) return;
      bool korean = Korean();
      Hashtable root = (Hashtable)AppDomain.CurrentDomain.GetData(RegistryKey);
      Hashtable issues = (Hashtable)root["issues"];
      bool reinstall = issues.Values.Cast<Hashtable>().Any(RequiresReinstall);
      List<DependencyDialogModItem> mods = issues.Values.Cast<Hashtable>()
        .OrderBy(row => (string)row["displayName"])
        .Select(row => new DependencyDialogModItem
        {
          Name = Escape(row["displayName"] as string),
          Requirement = korean
            ? "필요 버전 " + Escape(row["minimumVersion"] as string) + "+"
            : "Requires " + Escape(row["minimumVersion"] as string) + "+",
          Status = korean ? "이번 실행에서는 시작되지 않습니다." : "Not started in this session."
        }).ToList();
      _title.text = korean
        ? reinstall ? "AdofaiIPC를 한 번 다시 설치해야 합니다" : "전환 준비가 끝났습니다"
        : reinstall ? "Reinstall AdofaiIPC once" : "Migration is ready";
      _description.text = korean
        ? "새 의존성 구조로 전환하는 동안 아래 모드는 안전하게 중단되었습니다."
        : "The mods below were safely paused while the dependency setup is migrated.";
      _modList.SetItems(mods);
      _steps.Set(korean ? "다음 단계" : "NEXT STEPS", korean
        ? reinstall
          ? new[] { "최신 버전 재설치", "게임 완전 종료", "다시 실행" }
          : new[] { "게임 완전 종료", "다시 실행" }
        : reinstall
          ? new[] { "Reinstall latest", "Fully quit game", "Start again" }
          : new[] { "Fully quit game", "Start again" });
      ApplyFont();
      if (!_closed) _canvas.enabled = true;
    }

    private static bool RequiresReinstall(Hashtable issue)
    {
      if ((string)issue["kind"] != "MigrationRequired") return true;
      string installedValue = issue["installedVersion"] as string;
      string minimumValue = issue["minimumVersion"] as string;
      if (string.IsNullOrWhiteSpace(installedValue) || installedValue.Contains("-")) return true;
      return RequiresIpcReinstall(installedValue, minimumValue);
    }

    private void ApplyFont()
    {
      if (DependencyDialogUi.ApplyFonts(transform))
      {
        if (_fontRetryRegistered)
        {
          SceneManager.sceneLoaded -= OnSceneLoaded;
          _fontRetryRegistered = false;
        }
        return;
      }
      if (!_fontRetryRegistered) { SceneManager.sceneLoaded += OnSceneLoaded; _fontRetryRegistered = true; }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyFont();
    private void OnDestroy()
    { if (_fontRetryRegistered) SceneManager.sceneLoaded -= OnSceneLoaded; }

    private static bool Korean() => DependencyDialogUi.IsKorean();
    private static string Escape(string value) => (value ?? string.Empty)
      .Replace("<", "‹").Replace(">", "›");
  }
}
