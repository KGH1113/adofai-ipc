using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace AdofaiIpc.SharedUi;

internal static class DependencyDialogUi
{
  internal static readonly Color Backdrop = new(.015f, .025f, .055f, .82f);
  internal static readonly Color Shadow = new(0f, 0f, 0f, .34f);
  internal static readonly Color Border = new(.22f, .27f, .38f, .9f);
  internal static readonly Color Surface = new(.065f, .078f, .12f, .995f);
  internal static readonly Color Raised = new(.095f, .112f, .165f, 1f);
  internal static readonly Color Accent = new(.36f, .62f, 1f, 1f);
  internal static readonly Color Warning = new(1f, .69f, .25f, 1f);
  internal static readonly Color PrimaryText = new(.965f, .975f, 1f, 1f);
  internal static readonly Color SecondaryText = new(.68f, .73f, .82f, 1f);
  internal static readonly Color MutedText = new(.48f, .54f, .65f, 1f);

  private static Sprite _roundedSprite;

  internal static void ConfigureCanvas(CanvasScaler scaler)
  {
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1600f, 900f);
    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
    scaler.matchWidthOrHeight = .5f;
  }

  internal static Image Image(string name, Transform parent, Color color, bool rounded = false)
  {
    GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    gameObject.transform.SetParent(parent, false);
    Image image = gameObject.GetComponent<Image>();
    image.color = color;
    if (rounded)
    {
      image.sprite = RoundedSprite();
      image.type = UnityEngine.UI.Image.Type.Sliced;
    }
    return image;
  }

  internal static Text Text(
    string name,
    Transform parent,
    int size,
    FontStyle style,
    TextAnchor anchor,
    Color color)
  {
    GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
    gameObject.transform.SetParent(parent, false);
    Text text = gameObject.GetComponent<Text>();
    text.fontSize = size;
    text.fontStyle = style;
    text.alignment = anchor;
    text.color = color;
    text.supportRichText = true;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Truncate;
    return text;
  }

  internal static Button Button(string label, Transform parent, bool primary)
  {
    Color normal = primary ? new Color(.22f, .48f, .92f, 1f) : Raised;
    Image background = Image(label, parent, normal, true);
    Button button = background.gameObject.AddComponent<Button>();
    ColorBlock colors = button.colors;
    colors.normalColor = normal;
    colors.highlightedColor = primary ? new Color(.29f, .56f, 1f, 1f) : new Color(.14f, .17f, .24f, 1f);
    colors.pressedColor = primary ? new Color(.16f, .38f, .76f, 1f) : new Color(.07f, .085f, .13f, 1f);
    colors.disabledColor = new Color(normal.r, normal.g, normal.b, .45f);
    colors.colorMultiplier = 1f;
    colors.fadeDuration = .08f;
    button.colors = colors;
    Text text = Text("Label", background.transform, 16, FontStyle.Bold, TextAnchor.MiddleCenter, PrimaryText);
    text.text = label;
    Stretch(text.rectTransform);
    return button;
  }

  internal static void Place(RectTransform rect, float x, float y, float width, float height)
  {
    rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
    rect.pivot = new Vector2(0f, 1f);
    rect.anchoredPosition = new Vector2(x, -y);
    rect.sizeDelta = new Vector2(width, height);
  }

  internal static void Center(RectTransform rect, float width, float height, float y = 0f)
  {
    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
    rect.pivot = new Vector2(.5f, .5f);
    rect.anchoredPosition = new Vector2(0f, y);
    rect.sizeDelta = new Vector2(width, height);
  }

  internal static void Stretch(RectTransform rect)
  {
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
  }

  internal static bool ApplyFonts(Transform root, params Text[] displayTexts)
  {
    Text[] texts = root.GetComponentsInChildren<Text>(true);
    Font fallback = Resources.GetBuiltinResource<Font>("Arial.ttf");
    Font localized = TryGetLocalizedFont(out bool useLocalizedBody);
    Font body = useLocalizedBody ? localized : fallback;
    if (body == null) body = localized;
    if (body == null) return false;
    foreach (Text text in texts)
    {
      text.font = body;
      text.lineSpacing = useLocalizedBody ? .9f : 1f;
    }
    Font display = localized ?? body;
    foreach (Text text in displayTexts)
      if (text != null) text.font = display;
    return localized != null;
  }

  internal static bool IsKorean()
  {
    try
    {
      Type rd = Type.GetType("RDString, Assembly-CSharp");
      rd?.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
      object language = rd?.GetField("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                        rd?.GetProperty("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
      return language != null &&
             language.ToString().IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    catch { return false; }
  }

  private static Font TryGetLocalizedFont(out bool useLocalizedBody)
  {
    useLocalizedBody = false;
    try
    {
      Type rd = Type.GetType("RDString, Assembly-CSharp");
      rd?.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
      object language = rd?.GetField("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                        rd?.GetProperty("language", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
      string languageName = language?.ToString() ?? string.Empty;
      useLocalizedBody = languageName.IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         languageName.IndexOf("japanese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         languageName.IndexOf("chinese", StringComparison.OrdinalIgnoreCase) >= 0;
      object fontData = rd?.GetProperty("fontData", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                        rd?.GetField("fontData", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
      if (fontData == null) return null;
      FieldInfo field = fontData.GetType().GetField("font",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      if (field?.GetValue(fontData) is Font fieldFont) return fieldFont;
      PropertyInfo property = fontData.GetType().GetProperty("font",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      return property?.GetValue(fontData) as Font;
    }
    catch { return null; }
  }

  private static Sprite RoundedSprite()
  {
    if (_roundedSprite != null) return _roundedSprite;
    const int size = 40;
    const float radius = 10f;
    Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
    {
      name = "[AdofaiIpc] Rounded UI",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.HideAndDontSave
    };
    Color[] pixels = new Color[size * size];
    for (int y = 0; y < size; y++)
    for (int x = 0; x < size; x++)
    {
      float dx = Math.Max(radius - x - .5f, x + .5f - (size - radius));
      float dy = Math.Max(radius - y - .5f, y + .5f - (size - radius));
      float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                 Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
      float alpha = 1f - Mathf.Clamp01(outside - radius + 1f);
      pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
    }
    texture.SetPixels(pixels);
    texture.Apply(false, true);
    _roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f),
      100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    _roundedSprite.name = "[AdofaiIpc] Rounded UI";
    _roundedSprite.hideFlags = HideFlags.HideAndDontSave;
    return _roundedSprite;
  }
}
