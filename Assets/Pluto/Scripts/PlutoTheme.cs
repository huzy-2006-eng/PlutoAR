using UnityEngine;

// "Pluto Flight Academy" design tokens: colours, spacing, type sizes and the
// generated rounded / circular sprites every UI element shares. Sprites are
// built once at runtime, so no image assets need importing.
public static class PlutoTheme
{
    // Colours
    public static readonly Color Deep = Hex(0x07111F);
    public static readonly Color Surface = Hex(0x102238);
    public static readonly Color Raised = Hex(0x18334B);
    public static readonly Color Cyan = Hex(0x41D9FF);
    public static readonly Color Mint = Hex(0x63E6B1);
    public static readonly Color Amber = Hex(0xFFC86A);
    public static readonly Color Coral = Hex(0xFF7485);
    public static readonly Color Text = Hex(0xF4F8FF);
    public static readonly Color TextSecondary = Hex(0xB3C5D9);
    public static readonly Color TextMuted = Hex(0x8198B0);
    public static readonly Color Border = Hex(0x37506B);

    public const float PanelAlpha = 0.88f;

    // Spacing (8-unit grid, 1920x1080 reference pixels)
    public const float S1 = 8f, S2 = 16f, S3 = 24f, S4 = 32f;
    public const float Margin = 40f;

    // Shape
    public const float CardRadius = 22f;
    public const float ButtonRadius = 16f;

    // Type (sized for a ~6.8" landscape phone; 1 ref px ~ 1.3 screen px on the GT 7)
    public const int Caption = 22;
    public const int Label = 24;
    public const int Body = 28;
    public const int Title = 36;
    public const int Headline = 48;
    public const int Display = 76;

    // Minimum touch height in reference px (~48 dp on a 560 dpi phone)
    public const float TouchHeight = 132f;

    const int SliceRadius = 32;

    static Sprite roundedFill, roundedOutline, circle, circleOutline;

    public static Color Hex(int rgb, float alpha = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Color A(this Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

    public static string HexOf(Color c) => ColorUtility.ToHtmlStringRGB(c);

    public static Font Font => UIFactory.Font;

    public static Sprite RoundedFill => roundedFill != null ? roundedFill : (roundedFill = MakeRounded(0f));
    public static Sprite RoundedOutline => roundedOutline != null ? roundedOutline : (roundedOutline = MakeRounded(3f));
    public static Sprite Circle => circle != null ? circle : (circle = MakeCircle(0f));
    public static Sprite CircleOutline => circleOutline != null ? circleOutline : (circleOutline = MakeCircle(4f));

    // Sliced sprites have a 32 px corner; this multiplier gives the wanted radius.
    public static float SliceMultiplier(float radius) => SliceRadius / Mathf.Max(1f, radius);

    static Sprite MakeRounded(float thickness)
    {
        const int size = 128;
        var tex = NewTexture(size);
        var px = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // signed distance to a rounded square (negative inside)
                float qx = Mathf.Abs(x + 0.5f - half) - (half - SliceRadius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - SliceRadius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - SliceRadius;
                px[y * size + x] = White(Coverage(d, thickness));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(SliceRadius, SliceRadius, SliceRadius, SliceRadius));
    }

    static Sprite MakeCircle(float thickness)
    {
        const int size = 256;
        var tex = NewTexture(size);
        var px = new Color32[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) - (r - 1f);
                px[y * size + x] = White(Coverage(d, thickness));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // Anti-aliased fill (thickness 0) or ring of the given pixel thickness.
    static float Coverage(float d, float thickness)
    {
        float fill = Mathf.Clamp01(0.5f - d);
        return thickness <= 0f ? fill : fill * Mathf.Clamp01(0.5f + d + thickness);
    }

    static Color32 White(float alpha) => new Color32(255, 255, 255, (byte)(alpha * 255f));

    static Texture2D NewTexture(int size) =>
        new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
}
