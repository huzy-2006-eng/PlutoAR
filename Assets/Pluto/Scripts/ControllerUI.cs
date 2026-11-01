using System;
using UnityEngine;
using UnityEngine.UI;

public struct FlightInput
{
    public float throttle, yaw, pitch, roll;

    public float Magnitude => Mathf.Max(Mathf.Max(Mathf.Abs(throttle), Mathf.Abs(yaw)), Mathf.Max(Mathf.Abs(pitch), Mathf.Abs(roll)));
}

// The virtual Pluto controller (Mode 2 layout, landscape):
//   LEFT stick  : up/down = THROTTLE, left/right = YAW
//   RIGHT stick : up/down = PITCH,    left/right = ROLL
//   Buttons     : ARM/DISARM, TAKE OFF, LAND
public class ControllerUI : MonoBehaviour
{
    public VirtualJoystick Left { get; private set; }
    public VirtualJoystick Right { get; private set; }
    public Button ArmButton { get; private set; }
    public Button TakeoffButton { get; private set; }
    public Button LandButton { get; private set; }

    public event Action ArmPressed, TakeoffPressed, LandPressed;

    GameObject root;
    Component highlighted;

    public void Build(RectTransform parent)
    {
        root = UIFactory.MakeRect("Controller", parent).Stretch().gameObject;
        const float r = 165f;
        Left = MakeStick("LeftStick", new Vector2(0f, 0f), new Vector2(70f, 90f), r, "UP", "DOWN", "YAW L", "YAW R", "THROTTLE / YAW");
        Right = MakeStick("RightStick", new Vector2(1f, 0f), new Vector2(-70f, 90f), r, "FWD", "BACK", "LEFT", "RIGHT", "PITCH / ROLL");

        ArmButton = MakeButton("ARM", -250f, HUDManager.Good, () => ArmPressed?.Invoke());
        TakeoffButton = MakeButton("TAKE OFF", 0f, new Color(0.15f, 0.45f, 0.85f, 0.9f), () => TakeoffPressed?.Invoke());
        LandButton = MakeButton("LAND", 250f, new Color(0.85f, 0.55f, 0.1f, 0.9f), () => LandPressed?.Invoke());
        SetArmed(false);
    }

    public void SetVisible(bool visible) => root.SetActive(visible);

    public void SetArmed(bool armed)
    {
        ArmButton.Label().text = armed ? "DISARM" : "ARM";
        ArmButton.targetGraphic.color = armed ? new Color(0.85f, 0.2f, 0.2f, 0.9f) : new Color(0.15f, 0.65f, 0.3f, 0.9f);
    }

    // Pulses a control so the tutorial can point at it. Pass null to clear.
    public void Highlight(Component target)
    {
        if (highlighted != null) highlighted.transform.localScale = Vector3.one;
        highlighted = target;
    }

    public FlightInput Read()
    {
        InputHelper.ReadExtraSticks(out var extraLeft, out var extraRight);
        var l = Vector2.ClampMagnitude(Left.Value + extraLeft, 1f);
        var r = Vector2.ClampMagnitude(Right.Value + extraRight, 1f);
        return new FlightInput { throttle = l.y, yaw = l.x, pitch = r.y, roll = r.x };
    }

    void Update()
    {
        if (highlighted != null)
            highlighted.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f));

        if (root == null || !root.activeInHierarchy) return;
        if (InputHelper.ArmKeyDown()) ArmPressed?.Invoke();
        if (InputHelper.TakeoffKeyDown()) TakeoffPressed?.Invoke();
        if (InputHelper.LandKeyDown()) LandPressed?.Invoke();
    }

    VirtualJoystick MakeStick(string name, Vector2 corner, Vector2 offset, float r,
        string up, string down, string left, string right, string caption)
    {
        var js = VirtualJoystick.Create(name, root.transform, r);
        var rt = (RectTransform)js.transform;
        rt.Place(corner, corner, offset, Vector2.one * r * 2f);

        AddLabel(rt, up, new Vector2(0.5f, 1f), new Vector2(0f, -26f));
        AddLabel(rt, down, new Vector2(0.5f, 0f), new Vector2(0f, 26f));
        AddLabel(rt, left, new Vector2(0f, 0.5f), new Vector2(46f, 0f));
        AddLabel(rt, right, new Vector2(1f, 0.5f), new Vector2(-46f, 0f));
        var cap = UIFactory.MakeText("Caption", rt, caption, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f));
        cap.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(r * 2.4f, 36f));
        return js;
    }

    static void AddLabel(RectTransform parent, string text, Vector2 anchor, Vector2 position)
    {
        var t = UIFactory.MakeText(text, parent, text, 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
        t.rectTransform.Place(anchor, new Vector2(0.5f, 0.5f), position, new Vector2(110f, 30f));
    }

    Button MakeButton(string label, float x, Color color, Action onClick)
    {
        var b = UIFactory.MakeButton(label, root.transform, label, color, onClick, 32);
        ((RectTransform)b.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 40f), new Vector2(220f, 100f));
        return b;
    }
}
