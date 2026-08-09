using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityModManagerNet;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyIssuePresenter
{
  private const string RootName = "[AdofaiIpc] Dependency Error UI v1";

  public static void RequestRefresh(SynchronizationContext mainThread, UnityModManager.ModEntry.ModLogger logger)
  {
    void Refresh()
    {
      try
      {
        GameObject root = FindRoot();
        if (root == null) root = CreateRoot();
        else root.SendMessage("RefreshFromSharedRegistry", SendMessageOptions.DontRequireReceiver);
      }
      catch (Exception exception) { logger.Error("Could not display AdofaiIpc dependency UI: " + exception); }
    }

    if (mainThread != null && SynchronizationContext.Current != mainThread)
      mainThread.Post(_ => Refresh(), null);
    else Refresh();
  }

  private static GameObject FindRoot()
  {
    lock (AppDomain.CurrentDomain)
    {
      Hashtable registry = DependencyIssueRegistry.GetRoot();
      if (registry[DependencyIssueRegistry.RootKey] is WeakReference reference &&
          reference.Target is GameObject target && target != null) return target;
      GameObject found = GameObject.Find(RootName);
      if (found != null) registry[DependencyIssueRegistry.RootKey] = new WeakReference(found);
      return found;
    }
  }

  private static GameObject CreateRoot()
  {
    GameObject root = new(RootName);
    UnityEngine.Object.DontDestroyOnLoad(root);
    Canvas canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = short.MaxValue;
    root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    root.AddComponent<GraphicRaycaster>();
    DependencyIssueView view = root.AddComponent<DependencyIssueView>();
    lock (AppDomain.CurrentDomain)
      DependencyIssueRegistry.GetRoot()[DependencyIssueRegistry.RootKey] = new WeakReference(root);
    view.Build();
    return root;
  }
}

internal sealed class DependencyIssueView : MonoBehaviour
{
  private Text _body;
  private Canvas _canvas;
  private bool _closed;
  private bool _fontRetryRegistered;

  public void Build()
  {
    _canvas = GetComponent<Canvas>();
    Image blocker = Ui.Image("Input blocker", transform, new Color(0f, 0f, 0f, .72f));
    Ui.Stretch(blocker.rectTransform);

    Image panel = Ui.Image("Panel", blocker.transform, new Color(.075f, .085f, .11f, .98f));
    RectTransform panelRect = panel.rectTransform;
    panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
    panelRect.pivot = new Vector2(.5f, .5f);
    panelRect.sizeDelta = new Vector2(720f, 440f);
    VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
    layout.padding = new RectOffset(36, 36, 30, 28);
    layout.spacing = 18f;
    layout.childControlHeight = true;
    layout.childControlWidth = true;
    layout.childForceExpandHeight = false;

    Text title = Ui.Text("Title", panel.transform, 30, FontStyle.Bold, TextAnchor.MiddleLeft);
    title.text = IsKorean() ? "AdofaiIPC가 필요합니다" : "AdofaiIPC is required";
    title.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
    _body = Ui.Text("Issues", panel.transform, 20, FontStyle.Normal, TextAnchor.UpperLeft);
    LayoutElement bodyLayout = _body.gameObject.AddComponent<LayoutElement>();
    bodyLayout.minHeight = 245f;
    bodyLayout.flexibleHeight = 1f;

    GameObject buttons = new("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
    buttons.transform.SetParent(panel.transform, false);
    HorizontalLayoutGroup row = buttons.GetComponent<HorizontalLayoutGroup>();
    row.spacing = 12f;
    row.childForceExpandWidth = true;
    buttons.AddComponent<LayoutElement>().preferredHeight = 52f;
    Button download = Ui.Button(IsKorean() ? "AdofaiIPC 다운로드" : "Download AdofaiIPC", buttons.transform);
    download.onClick.AddListener(() => Application.OpenURL(DependencyInstaller.ReleasePageUrl));
    Button close = Ui.Button(IsKorean() ? "닫기" : "Close", buttons.transform);
    close.onClick.AddListener(() => { _closed = true; _canvas.enabled = false; });
    EnsureEventSystem();
    RefreshFromSharedRegistry();
  }

  public void RefreshFromSharedRegistry()
  {
    if (_body == null) return;
    bool korean = IsKorean();
    List<Hashtable> issues = DependencyIssueRegistry.Snapshot();
    string intro = korean
      ? "아래 모드는 필요한 AdofaiIPC를 불러오지 못해 시작되지 않았습니다. 문제를 해결한 뒤 게임을 다시 시작하세요."
      : "The following mods were not started because their required AdofaiIPC could not be loaded. Fix the issue, then restart the game.";
    List<string> rows = new() { intro, string.Empty };
    string highestMinimum = HighestMinimum(issues);
    if (!string.IsNullOrEmpty(highestMinimum))
      rows.Add((korean ? "필요한 최고 최소 버전: " : "Highest minimum required: ") + highestMinimum);
    foreach (Hashtable issue in issues)
    {
      string name = (string)issue["displayName"];
      string minimum = (string)issue["minimumVersion"];
      string kind = (string)issue["kind"];
      string installed = (string)issue["installedVersion"];
      rows.Add("• " + name + " — " + (korean ? "최소 " : "minimum ") + minimum);
      rows.Add("  " + Message(kind, installed, korean));
    }
    _body.text = string.Join("\n", rows);
    ApplyGameFont();
    if (!_closed) _canvas.enabled = true;
  }

  private static string HighestMinimum(IEnumerable<Hashtable> issues)
  {
    Version highest = null;
    string value = null;
    foreach (Hashtable issue in issues)
    {
      string candidateValue = (string)issue["minimumVersion"];
      string numeric = candidateValue?.Split(new[] { '-', '+', ' ' }, 2)[0];
      if (!Version.TryParse(numeric, out Version candidate) || highest != null && candidate <= highest) continue;
      highest = candidate;
      value = candidateValue;
    }
    return value;
  }

  private void ApplyGameFont()
  {
    Text[] texts = GetComponentsInChildren<Text>(true);
    if (TryApplyGameFont(texts))
    {
      if (_fontRetryRegistered)
      {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _fontRetryRegistered = false;
      }
      return;
    }
    Font fallback = Resources.GetBuiltinResource<Font>("Arial.ttf");
    if (fallback != null) foreach (Text text in texts) text.font = fallback;
    if (!_fontRetryRegistered)
    {
      _fontRetryRegistered = true;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }
  }

  private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyGameFont();

  private static bool TryApplyGameFont(Text[] texts)
  {
    try
    {
      Type rdString = Type.GetType("RDString, Assembly-CSharp");
      rdString?.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
      MethodInfo setLocalizedFont = rdString?.GetMethod("SetLocalizedFont",
        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Text) }, null);
      if (setLocalizedFont != null)
      {
        foreach (Text text in texts) setLocalizedFont.Invoke(null, new object[] { text });
        if (texts.Length > 0 && Array.TrueForAll(texts, text => text.font != null)) return true;
      }
      object fontData = rdString?.GetField("fontData", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
      if (fontData == null) return false;
      foreach (FieldInfo field in fontData.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        if (typeof(Font).IsAssignableFrom(field.FieldType) && field.GetValue(fontData) is Font font)
        { foreach (Text text in texts) text.font = font; return true; }
      foreach (PropertyInfo property in fontData.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        if (typeof(Font).IsAssignableFrom(property.PropertyType) && property.GetValue(fontData) is Font font)
        { foreach (Text text in texts) text.font = font; return true; }
    }
    catch { }
    return false;
  }

  private static bool IsKorean()
  {
    try
    {
      Type rdString = Type.GetType("RDString, Assembly-CSharp");
      object language = rdString?.GetField("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                        rdString?.GetProperty("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
      return language != null && language.ToString().IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    catch { return false; }
  }

  private static string Message(string kind, string installed, bool korean)
  {
    return kind switch
    {
      "InstallFailure" => korean ? "설치되지 않았고 자동 설치에도 실패했습니다. 다운로드 후 재시작하세요."
        : "Not installed, and automatic installation failed. Download it and restart.",
      "Disabled" => korean ? "Unity Mod Manager에서 AdofaiIPC를 직접 활성화한 뒤 재시작하세요."
        : "Enable AdofaiIPC in Unity Mod Manager, then restart.",
      "Outdated" => korean ? $"설치 버전 {installed}은(는) 너무 오래되었습니다. 다시 설치하고 재시작하세요."
        : $"Installed version {installed} is outdated. Reinstall and restart.",
      "MigrationRequired" => korean ? "이번 한 번만 AdofaiIPC를 다시 설치하고 게임을 완전히 종료한 뒤 재실행하세요."
        : "Reinstall AdofaiIPC once, fully quit the game, then start it again.",
      _ => korean ? "AdofaiIPC 로드에 실패했습니다. UMM 로그를 확인하고 다시 설치하세요."
        : "AdofaiIPC failed to load. Check the UMM log and reinstall it."
    };
  }

  private void EnsureEventSystem()
  {
    if (Resources.FindObjectsOfTypeAll<EventSystem>().Length != 0) return;
    gameObject.AddComponent<EventSystem>();
    gameObject.AddComponent<StandaloneInputModule>();
  }
}

internal static class Ui
{
  public static Image Image(string name, Transform parent, Color color)
  {
    GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    gameObject.transform.SetParent(parent, false);
    Image image = gameObject.GetComponent<Image>();
    image.color = color;
    return image;
  }

  public static Text Text(string name, Transform parent, int size, FontStyle style, TextAnchor anchor)
  {
    GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
    gameObject.transform.SetParent(parent, false);
    Text text = gameObject.GetComponent<Text>();
    text.fontSize = size;
    text.fontStyle = style;
    text.alignment = anchor;
    text.color = Color.white;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    return text;
  }

  public static Button Button(string label, Transform parent)
  {
    Image background = Image(label, parent, new Color(.18f, .42f, .78f, 1f));
    Button button = background.gameObject.AddComponent<Button>();
    Text text = Text("Label", background.transform, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
    Stretch(text.rectTransform);
    return button;
  }

  public static void Stretch(RectTransform rect)
  {
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
  }
}
