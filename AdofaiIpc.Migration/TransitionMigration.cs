using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
  {
    if (!Prepare(owner, out string version)) return false;
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

  public static bool Prepare(UnityModManager.ModEntry owner) => Prepare(owner, out _);

  private static bool Prepare(UnityModManager.ModEntry owner, out string version)
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

    string sourceDirectory = Path.GetDirectoryName(typeof(TransitionMigration).Assembly.Location);
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
      root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
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
    private Text _body;
    private Text _title;
    private Canvas _canvas;
    private bool _closed;
    private bool _fontRetryRegistered;

    public void Build()
    {
      _canvas = GetComponent<Canvas>();
      Image blocker = Image("Input blocker", transform, new Color(0f, 0f, 0f, .72f));
      Stretch(blocker.rectTransform);
      Image panel = Image("Panel", blocker.transform, new Color(.075f, .085f, .11f, .98f));
      panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(.5f, .5f);
      panel.rectTransform.sizeDelta = new Vector2(760f, 470f);
      VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
      layout.padding = new RectOffset(36, 36, 30, 28); layout.spacing = 18f;
      layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
      _title = Text("Title", panel.transform, 30, FontStyle.Bold, TextAnchor.MiddleLeft);
      _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
      _body = Text("Body", panel.transform, 20, FontStyle.Normal, TextAnchor.UpperLeft);
      _body.gameObject.AddComponent<LayoutElement>().minHeight = 270f;
      GameObject buttons = new("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
      buttons.transform.SetParent(panel.transform, false);
      buttons.GetComponent<HorizontalLayoutGroup>().spacing = 12f;
      buttons.AddComponent<LayoutElement>().preferredHeight = 52f;
      Button download = Button(Korean() ? "AdofaiIPC 다운로드" : "Download AdofaiIPC", buttons.transform);
      download.onClick.AddListener(() => Application.OpenURL(ReleaseUrl));
      Button close = Button(Korean() ? "닫기" : "Close", buttons.transform);
      close.onClick.AddListener(() => { _closed = true; _canvas.enabled = false; });
      if (Resources.FindObjectsOfTypeAll<EventSystem>().Length == 0)
      { gameObject.AddComponent<EventSystem>(); gameObject.AddComponent<StandaloneInputModule>(); }
      RefreshFromSharedRegistry();
    }

    public void RefreshFromSharedRegistry()
    {
      if (_body == null) return;
      bool korean = Korean();
      Hashtable root = (Hashtable)AppDomain.CurrentDomain.GetData(RegistryKey);
      Hashtable issues = (Hashtable)root["issues"];
      bool reinstall = issues.Values.Cast<Hashtable>().Any(RequiresReinstall);
      string names = string.Join("\n", issues.Values.Cast<Hashtable>()
        .OrderBy(row => (string)row["displayName"])
        .Select(row => "• " + row["displayName"] + " (AdofaiIPC " + row["minimumVersion"] + "+)"));
      _title.text = korean
        ? reinstall ? "AdofaiIPC를 한 번 다시 설치해야 합니다" : "전환 준비가 끝났습니다"
        : reinstall ? "Reinstall AdofaiIPC once" : "Migration is ready";
      _body.text = korean
        ? "이번 실행에서는 아래 모드가 시작되지 않습니다.\n\n" + names +
          (reinstall
            ? "\n\n1. 최신 AdofaiIPC를 다운로드해 다시 설치하세요.\n2. 게임을 완전히 종료하세요.\n3. 게임을 다시 실행하세요.\n\n지금 재설치하면 게임 재시작은 한 번만 필요합니다."
            : "\n\nAdofaiIPC는 이미 최신입니다. 게임을 완전히 종료한 뒤 다시 실행하면 됩니다.")
        : "The following mods were not started in this session.\n\n" + names +
          (reinstall
            ? "\n\n1. Download and reinstall the latest AdofaiIPC.\n2. Fully quit the game.\n3. Start the game again.\n\nIf you reinstall now, only one restart is needed."
            : "\n\nAdofaiIPC is already current. Fully quit the game, then start it again.");
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
      Text[] texts = GetComponentsInChildren<Text>(true);
      try
      {
        Type rd = Type.GetType("RDString, Assembly-CSharp");
        rd?.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        MethodInfo method = rd?.GetMethod("SetLocalizedFont", BindingFlags.Public | BindingFlags.Static,
          null, new[] { typeof(Text) }, null);
        if (method != null)
        {
          foreach (Text text in texts) method.Invoke(null, new object[] { text });
          if (texts.Length > 0 && texts.All(text => text.font != null))
          {
            if (_fontRetryRegistered) { SceneManager.sceneLoaded -= OnSceneLoaded; _fontRetryRegistered = false; }
            return;
          }
        }
      }
      catch { }
      Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
      if (font != null) foreach (Text text in texts) text.font = font;
      if (!_fontRetryRegistered) { SceneManager.sceneLoaded += OnSceneLoaded; _fontRetryRegistered = true; }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyFont();
    private void OnDestroy()
    { if (_fontRetryRegistered) SceneManager.sceneLoaded -= OnSceneLoaded; }

    private static bool Korean()
    {
      try
      {
        Type rd = Type.GetType("RDString, Assembly-CSharp");
        rd?.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        object language = rd?.GetField("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                          rd?.GetProperty("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return language != null && language.ToString().IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0;
      }
      catch { return false; }
    }

    private static Image Image(string name, Transform parent, Color color)
    { GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); go.transform.SetParent(parent, false); Image image = go.GetComponent<Image>(); image.color = color; return image; }
    private static Text Text(string name, Transform parent, int size, FontStyle style, TextAnchor anchor)
    { GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)); go.transform.SetParent(parent, false); Text text = go.GetComponent<Text>(); text.fontSize = size; text.fontStyle = style; text.alignment = anchor; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; return text; }
    private static Button Button(string label, Transform parent)
    { Image image = Image(label, parent, new Color(.18f, .42f, .78f, 1f)); Button button = image.gameObject.AddComponent<Button>(); Text text = Text("Label", image.transform, 20, FontStyle.Bold, TextAnchor.MiddleCenter); text.text = label; Stretch(text.rectTransform); return button; }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
  }
}
