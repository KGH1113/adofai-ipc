using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using AdofaiIpc.SharedUi;
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
    DependencyDialogUi.ConfigureCanvas(root.AddComponent<CanvasScaler>());
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
    section.text = IsKorean() ? "영향받는 모드" : "AFFECTED MODS";
    DependencyDialogUi.Place(section.rectTransform, 32f, 136f, 300f, 20f);

    Image modsPanel = DependencyDialogUi.Image("Affected mods", panel.transform, DependencyDialogUi.Raised, true);
    DependencyDialogUi.Place(modsPanel.rectTransform, 30f, 160f, 620f, 140f);
    _modList = new DependencyDialogModList(modsPanel.transform);

    Image guide = DependencyDialogUi.Image("Next step", panel.transform,
      new Color(.14f, .25f, .42f, .72f), true);
    DependencyDialogUi.Place(guide.rectTransform, 30f, 316f, 620f, 92f);
    Image guideAccent = DependencyDialogUi.Image("Next step accent", guide.transform,
      DependencyDialogUi.Accent, true);
    DependencyDialogUi.Place(guideAccent.rectTransform, 0f, 0f, 4f, 92f);
    _steps = new DependencyDialogSteps(guide.transform);

    Button close = DependencyDialogUi.Button(IsKorean() ? "닫기" : "Close", panel.transform, false);
    DependencyDialogUi.Place(close.GetComponent<RectTransform>(), 344f, 430f, 108f, 42f);
    Button download = DependencyDialogUi.Button(
      IsKorean() ? "AdofaiIPC 다운로드" : "Download AdofaiIPC", panel.transform, true);
    DependencyDialogUi.Place(download.GetComponent<RectTransform>(), 464f, 430f, 186f, 42f);
    download.onClick.AddListener(() => Application.OpenURL(DependencyInstaller.ReleasePageUrl));
    close.onClick.AddListener(() => { _closed = true; _canvas.enabled = false; });
    EnsureEventSystem();
    RefreshFromSharedRegistry();
  }

  public void RefreshFromSharedRegistry()
  {
    if (_modList == null) return;
    bool korean = IsKorean();
    List<Hashtable> issues = DependencyIssueRegistry.Snapshot();
    string primaryKind = PrimaryKind(issues);
    _title.text = Title(primaryKind, korean);
    _description.text = korean
      ? "필요한 AdofaiIPC를 사용할 수 없어 아래 모드를 이번 실행에서 시작하지 않았습니다."
      : "These mods were not started because the required AdofaiIPC is unavailable.";
    List<DependencyDialogModItem> rows = new();
    foreach (Hashtable issue in issues)
    {
      string name = Escape((string)issue["displayName"]);
      string minimum = Escape((string)issue["minimumVersion"]);
      string kind = (string)issue["kind"];
      string installed = (string)issue["installedVersion"];
      rows.Add(new DependencyDialogModItem
      {
        Name = name,
        Requirement = korean ? "필요 버전 " + minimum + "+" : "Requires " + minimum + "+",
        Status = ShortMessage(kind, installed, korean)
      });
    }
    _modList.SetItems(rows);
    _steps.Set(korean ? "다음 단계" : "NEXT STEPS", GuideSteps(primaryKind, HighestMinimum(issues), korean));
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
    return value ?? "0.3.0";
  }

  private void ApplyGameFont()
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
    if (!_fontRetryRegistered)
    {
      _fontRetryRegistered = true;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }
  }

  private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyGameFont();
  private void OnDestroy()
  { if (_fontRetryRegistered) SceneManager.sceneLoaded -= OnSceneLoaded; }
  private static bool IsKorean() => DependencyDialogUi.IsKorean();

  private static string ShortMessage(string kind, string installed, bool korean) => kind switch
  {
    "InstallFailure" => korean ? "자동 설치에 실패했습니다." : "Automatic installation failed.",
    "Disabled" => korean ? "AdofaiIPC가 비활성화되어 있습니다." : "AdofaiIPC is disabled.",
    "Outdated" => korean ? $"설치된 {Escape(installed)} 버전이 오래되었습니다."
      : $"Installed version {Escape(installed)} is outdated.",
    "MigrationRequired" => korean ? "최초 전환을 위해 한 번 재설치해야 합니다."
      : "A one-time reinstall is required.",
    _ => korean ? "AdofaiIPC를 불러오지 못했습니다." : "AdofaiIPC could not be loaded."
  };

  private static string PrimaryKind(IEnumerable<Hashtable> issues)
  {
    string selected = "LoadFailure";
    int selectedRank = -1;
    foreach (Hashtable issue in issues)
    {
      string kind = (string)issue["kind"];
      int rank = kind switch
      {
        "Disabled" => 5,
        "Outdated" => 4,
        "MigrationRequired" => 4,
        "InstallFailure" => 3,
        _ => 2
      };
      if (rank > selectedRank) { selected = kind; selectedRank = rank; }
    }
    return selected;
  }

  private static string Title(string kind, bool korean) => kind switch
  {
    "Disabled" => korean ? "AdofaiIPC가 꺼져 있습니다" : "AdofaiIPC is disabled",
    "Outdated" or "MigrationRequired" => korean ? "AdofaiIPC 업데이트가 필요합니다" : "AdofaiIPC update required",
    "InstallFailure" => korean ? "AdofaiIPC를 설치하지 못했습니다" : "AdofaiIPC installation failed",
    _ => korean ? "AdofaiIPC를 시작하지 못했습니다" : "AdofaiIPC could not start"
  };

  private static string[] GuideSteps(string kind, string minimum, bool korean) => kind switch
  {
    "Disabled" => korean
      ? new[] { "UMM에서 활성화", "게임 완전 종료", "다시 실행" }
      : new[] { "Enable in UMM", "Fully quit game", "Start again" },
    "Outdated" or "MigrationRequired" => korean
      ? new[] { $"{minimum}+ 재설치", "게임 완전 종료", "다시 실행" }
      : new[] { $"Reinstall {minimum}+", "Fully quit game", "Start again" },
    _ => korean
      ? new[] { "최신 버전 재설치", "게임 완전 종료", "다시 실행" }
      : new[] { "Reinstall latest", "Fully quit game", "Start again" }
  };

  private static string Escape(string value) => (value ?? string.Empty)
    .Replace("<", "‹").Replace(">", "›");

  private void EnsureEventSystem()
  {
    if (Resources.FindObjectsOfTypeAll<EventSystem>().Length != 0) return;
    gameObject.AddComponent<EventSystem>();
    gameObject.AddComponent<StandaloneInputModule>();
  }
}
