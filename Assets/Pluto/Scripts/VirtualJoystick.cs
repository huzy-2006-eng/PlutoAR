using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One on-screen thumb stick. Value is -1..1 on both axes and springs back to
// centre when released (centre throttle = hold altitude, like the Pluto app).
// Each stick tracks its own finger, so both sticks work at the same time.
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public const float HandleSize = 104f;

    public float deadZone = 0.08f;

    public Vector2 Value { get; private set; }
    public bool IsHeld => pointerId != NoPointer;

    const int NoPointer = int.MinValue;
    static readonly Color HandleIdle = PlutoTheme.Cyan.A(0.78f);
    static readonly Color HandleActive = PlutoTheme.Cyan;
    static readonly Color RingIdle = PlutoTheme.Border;
    static readonly Color RingActive = PlutoTheme.Cyan.A(0.9f);

    RectTransform area;
    RectTransform knob;
    Image knobImage, ring;
    float radius;
    int pointerId = NoPointer;

    public static VirtualJoystick Create(string name, Transform parent, float radius)
    {
        var bg = UIFactory.MakeImage(name, parent, PlutoTheme.Deep.A(0.42f), PlutoTheme.Circle);
        bg.rectTransform.sizeDelta = Vector2.one * radius * 2f;

        var ring = Deco("Ring", bg.transform, RingIdle, PlutoTheme.CircleOutline);
        ring.rectTransform.Stretch();
        var inner = Deco("Inner", bg.transform, PlutoTheme.Border.A(0.35f), PlutoTheme.CircleOutline);
        inner.rectTransform.sizeDelta = Vector2.one * radius;

        // Direction markers just inside the rim.
        for (int i = 0; i < 4; i++)
        {
            var dir = new Vector2(i == 1 ? 1f : i == 3 ? -1f : 0f, i == 0 ? 1f : i == 2 ? -1f : 0f);
            var tick = Deco("Tick", bg.transform, PlutoTheme.TextSecondary.A(0.55f), PlutoTheme.Circle);
            tick.rectTransform.sizeDelta = new Vector2(10f, 10f);
            tick.rectTransform.anchoredPosition = dir * (radius - 22f);
        }
        Deco("CentreDot", bg.transform, PlutoTheme.TextSecondary.A(0.5f), PlutoTheme.Circle).rectTransform.sizeDelta = new Vector2(8f, 8f);

        var k = Deco("Knob", bg.transform, HandleIdle, PlutoTheme.Circle);
        k.rectTransform.sizeDelta = Vector2.one * HandleSize;
        var shadow = k.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -3f);
        Deco("Shine", k.transform, Color.white.A(0.28f), PlutoTheme.Circle).rectTransform.sizeDelta = Vector2.one * HandleSize * 0.42f;

        var js = bg.gameObject.AddComponent<VirtualJoystick>();
        js.area = bg.rectTransform;
        js.knob = k.rectTransform;
        js.knobImage = k;
        js.ring = ring;
        js.radius = radius;
        return js;
    }

    static Image Deco(string name, Transform parent, Color color, Sprite sprite)
    {
        var img = UIFactory.MakeImage(name, parent, color, sprite);
        img.raycastTarget = false;
        return img;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (IsHeld) return;
        pointerId = e.pointerId;
        ring.color = RingActive;
        Move(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId == pointerId) Move(e);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId == pointerId) Release();
    }

    void OnDisable() => Release();

    void Release()
    {
        pointerId = NoPointer;
        Value = Vector2.zero;
        if (knob == null) return;
        knob.anchoredPosition = Vector2.zero;
        knobImage.color = HandleIdle;
        ring.color = RingIdle;
    }

    void Move(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out var local);
        local -= area.rect.center;
        var v = Vector2.ClampMagnitude(local / radius, 1f);
        knob.anchoredPosition = v * radius;
        Value = v.magnitude < deadZone ? Vector2.zero : v;
        knobImage.color = Color.Lerp(HandleIdle, HandleActive, v.magnitude);
    }
}
