using System;
using UnityEngine;
using UnityEngine.UI;

public enum ButtonStyle { Primary, Outline, Dark, Danger, Ghost }

// Small helpers to build the whole UI from code, so nobody has to wire up
// Canvas objects by hand in the Inspector. Visual values come from PlutoTheme.
public static class UIFactory
{
    static Font font;

    public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    public static Sprite Circle => PlutoTheme.Circle;

    public static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image MakeImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var img = MakeRect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.sprite = sprite;
        return img;
    }

    // Decorative rounded shape (no raycasts).
    public static Image MakeRounded(string name, Transform parent, Color color, float radius, bool outline = false)
    {
        var img = MakeImage(name, parent, color, outline ? PlutoTheme.RoundedOutline : PlutoTheme.RoundedFill);
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = PlutoTheme.SliceMultiplier(radius);
        img.raycastTarget = false;
        return img;
    }

    // Rounded card: translucent navy fill, thin border, soft drop shadow.
    public static Image MakePanel(string name, Transform parent, float radius = PlutoTheme.CardRadius,
        float alpha = PlutoTheme.PanelAlpha, bool raycast = false)
    {
        var panel = MakeRounded(name, parent, PlutoTheme.Surface.A(alpha), radius);
        panel.raycastTarget = raycast;
        var shadow = panel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -4f);
        AddBorder(panel.rectTransform, PlutoTheme.Border, radius);
        return panel;
    }

    public static Image AddBorder(RectTransform target, Color color, float radius)
    {
        var border = MakeRounded("Border", target, color, radius, true);
        border.rectTransform.Stretch();
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return border;
    }

    public static Text MakeText(string name, Transform parent, string text, int size, TextAnchor anchor, Color color)
    {
        var t = MakeRect(name, parent).gameObject.AddComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    public static Button MakeButton(string name, Transform parent, string label, Color background, Action onClick, int fontSize = 32)
    {
        var img = MakeImage(name, parent, background);
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(() => onClick?.Invoke());
        var text = MakeText("Label", img.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white);
        text.fontStyle = FontStyle.Bold;
        text.rectTransform.Stretch();
        return button;
    }

    public static StyledButton MakeStyledButton(string name, Transform parent, string label, ButtonStyle style, Action onClick, int fontSize = PlutoTheme.Label)
    {
        var fill = MakeImage(name, parent, Color.white, PlutoTheme.RoundedFill);
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = PlutoTheme.SliceMultiplier(PlutoTheme.ButtonRadius);
        var button = fill.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.75f, 0.8f, 0.85f, 1f);
        colors.fadeDuration = 0.06f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => onClick?.Invoke());

        var border = AddBorder(fill.rectTransform, Color.white, PlutoTheme.ButtonRadius);
        var text = MakeText("Label", fill.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white);
        text.fontStyle = FontStyle.Bold;
        text.rectTransform.Stretch(12f, 0f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var group = fill.gameObject.AddComponent<CanvasGroup>();

        var styled = new StyledButton(button, fill, border, text, group);
        styled.SetStyle(style);
        return styled;
    }

    // Grow the touch area of a small control without changing how it looks.
    public static void ExpandTouchArea(Graphic graphic, float minHeight = PlutoTheme.TouchHeight, float minWidth = 0f)
    {
        var size = graphic.rectTransform.sizeDelta;
        float padY = Mathf.Max(0f, (minHeight - size.y) / 2f);
        float padX = Mathf.Max(0f, (minWidth - size.x) / 2f);
        graphic.raycastPadding = new Vector4(-padX, -padY, -padX, -padY);
    }

    public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Stretch(this RectTransform rt, float padX = 0f, float padY = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
        return rt;
    }

    public static Text Label(this Button button) => button.GetComponentInChildren<Text>();

    public static VerticalLayoutGroup AddVerticalLayout(GameObject go, int padH, int padV, float spacing, TextAnchor align = TextAnchor.UpperLeft)
    {
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padH, padH, padV, padV);
        layout.spacing = spacing;
        layout.childAlignment = align;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    public static LayoutElement Height(this Component c, float preferred)
    {
        var le = c.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = preferred;
        le.minHeight = preferred;
        return le;
    }
}

// A themed button with style + availability states. Unavailable buttons stay
// clickable so the existing flight logic can explain why the action is refused.
public class StyledButton
{
    public readonly Button Button;
    public readonly Image Fill;
    public readonly Image Border;
    public readonly Text Text;
    readonly CanvasGroup group;

    public RectTransform Rect => (RectTransform)Button.transform;
    public ButtonStyle Style { get; private set; }

    public StyledButton(Button button, Image fill, Image border, Text text, CanvasGroup group)
    {
        Button = button;
        Fill = fill;
        Border = border;
        Text = text;
        this.group = group;
    }

    public void SetLabel(string label) => Text.text = label;

    public void SetStyle(ButtonStyle style)
    {
        Style = style;
        switch (style)
        {
            case ButtonStyle.Primary:
                Fill.color = PlutoTheme.Cyan;
                Border.color = PlutoTheme.Cyan;
                Text.color = PlutoTheme.Deep;
                break;
            case ButtonStyle.Outline:
                Fill.color = PlutoTheme.Deep.A(0.72f);
                Border.color = PlutoTheme.Cyan;
                Text.color = PlutoTheme.Cyan;
                break;
            case ButtonStyle.Danger:
                Fill.color = PlutoTheme.Deep.A(0.72f);
                Border.color = PlutoTheme.Coral;
                Text.color = PlutoTheme.Coral;
                break;
            case ButtonStyle.Ghost:
                Fill.color = PlutoTheme.Deep.A(0.4f);
                Border.color = PlutoTheme.Border;
                Text.color = PlutoTheme.TextSecondary;
                break;
            default:
                Fill.color = PlutoTheme.Surface.A(PlutoTheme.PanelAlpha);
                Border.color = PlutoTheme.Border;
                Text.color = PlutoTheme.Text;
                break;
        }
    }

    public void SetAvailable(bool available) => group.alpha = available ? 1f : 0.42f;
}

// Keeps UI out of phone notches / rounded corners.
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt;
    Rect applied;

    void Awake()
    {
        rt = (RectTransform)transform;
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != applied) Apply();
    }

    void Apply()
    {
        applied = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0) return;
        var min = applied.position;
        var max = applied.position + applied.size;
        rt.anchorMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
        rt.anchorMax = new Vector2(max.x / Screen.width, max.y / Screen.height);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
