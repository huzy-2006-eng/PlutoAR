using System;
using UnityEngine;
using UnityEngine.UI;

// Small helpers to build the whole UI from code, so nobody has to wire up
// Canvas objects by hand in the Inspector.
public static class UIFactory
{
    static Font font;
    static Sprite circle;

    public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    public static Sprite Circle
    {
        get
        {
            if (circle != null) return circle;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
            return circle;
        }
    }

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
