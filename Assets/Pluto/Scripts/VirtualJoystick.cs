using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One on-screen thumb stick. Value is -1..1 on both axes and springs back to
// centre when released (centre throttle = hold altitude, like the Pluto app).
// Each stick tracks its own finger, so both sticks work at the same time.
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public float deadZone = 0.08f;

    public Vector2 Value { get; private set; }
    public bool IsHeld => pointerId != NoPointer;

    const int NoPointer = int.MinValue;
    static readonly Color Idle = new Color(1f, 1f, 1f, 0.75f);
    static readonly Color Active = new Color(0.3f, 0.9f, 1f, 1f);

    RectTransform area;
    RectTransform knob;
    Image knobImage;
    float radius;
    int pointerId = NoPointer;

    public static VirtualJoystick Create(string name, Transform parent, float radius)
    {
        var bg = UIFactory.MakeImage(name, parent, new Color(0f, 0f, 0f, 0.35f), UIFactory.Circle);
        bg.rectTransform.sizeDelta = Vector2.one * radius * 2f;

        var h = UIFactory.MakeImage("AxisH", bg.transform, new Color(1f, 1f, 1f, 0.15f));
        h.rectTransform.sizeDelta = new Vector2(radius * 1.8f, 3f);
        h.raycastTarget = false;
        var v = UIFactory.MakeImage("AxisV", bg.transform, new Color(1f, 1f, 1f, 0.15f));
        v.rectTransform.sizeDelta = new Vector2(3f, radius * 1.8f);
        v.raycastTarget = false;

        var k = UIFactory.MakeImage("Knob", bg.transform, Idle, UIFactory.Circle);
        k.rectTransform.sizeDelta = Vector2.one * radius * 0.8f;
        k.raycastTarget = false;

        var js = bg.gameObject.AddComponent<VirtualJoystick>();
        js.area = bg.rectTransform;
        js.knob = k.rectTransform;
        js.knobImage = k;
        js.radius = radius;
        return js;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (IsHeld) return;
        pointerId = e.pointerId;
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
        knobImage.color = Idle;
    }

    void Move(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out var local);
        local -= area.rect.center;
        var v = Vector2.ClampMagnitude(local / radius, 1f);
        knob.anchoredPosition = v * radius;
        Value = v.magnitude < deadZone ? Vector2.zero : v;
        knobImage.color = Color.Lerp(Idle, Active, v.magnitude);
    }
}
