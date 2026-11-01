using System;
using System.Collections.Generic;
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
    const float StickRadius = 150f;
    const float ButtonWidth = 200f;
    const float ButtonHeight = 96f;
    static readonly Color LabelBright = PlutoTheme.Text.A(0.95f);
    static readonly Color LabelDim = PlutoTheme.TextSecondary.A(0.45f);

    public VirtualJoystick Left { get; private set; }
    public VirtualJoystick Right { get; private set; }
    public Button ArmButton => arm.Button;
    public Button TakeoffButton => takeoff.Button;
    public Button LandButton => land.Button;
    public bool Visible => root.activeSelf;

    public event Action ArmPressed, TakeoffPressed, LandPressed;

    GameObject root;
    StyledButton arm, takeoff, land, context;
    Action contextAction;
    readonly List<Text> stickLabels = new List<Text>();
    Component highlighted;

    public void Build(RectTransform parent)
    {
        root = UIFactory.MakeRect("Controller", parent).Stretch().gameObject;
        Left = MakeStick("LeftStick", new Vector2(0f, 0f), new Vector2(120f, 80f), "CLIMB", "DESCEND", "YAW\nLEFT", "YAW\nRIGHT");
        Right = MakeStick("RightStick", new Vector2(1f, 0f), new Vector2(-120f, 80f), "FORWARD", "BACKWARD", "LEFT", "RIGHT");

        float step = ButtonWidth + PlutoTheme.S3;
        arm = MakeButton("ARM", -step, ButtonStyle.Outline, () => ArmPressed?.Invoke());
        takeoff = MakeButton("↑ TAKE OFF", 0f, ButtonStyle.Primary, () => TakeoffPressed?.Invoke());
        land = MakeButton("↓ LAND", step, ButtonStyle.Dark, () => LandPressed?.Invoke());
        SetFlightState(FlightState.Disarmed, false);
        SetLabelsProminent(false);

        // Mission-specific action (e.g. RELEASE PACKAGE), above the flight buttons.
        context = UIFactory.MakeStyledButton("ContextButton", root.transform, "", ButtonStyle.Primary, () => contextAction?.Invoke(), PlutoTheme.Label + 2);
        context.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f + ButtonHeight + 44f), new Vector2(380f, 88f));
        UIFactory.ExpandTouchArea(context.Fill);
        SetContextAction(null, null);
    }

    public void SetVisible(bool visible) => root.SetActive(visible);

    // Pass a null label to hide the contextual button.
    public void SetContextAction(string label, Action onClick)
    {
        contextAction = onClick;
        bool show = !string.IsNullOrEmpty(label);
        context.Rect.gameObject.SetActive(show);
        if (show) context.SetLabel(label);
    }

    public void SetContextAvailable(bool available) => context.SetAvailable(available);

    // Button labels / availability follow the real flight state. Unavailable
    // buttons stay tappable so the drone can explain why the action is refused.
    public void SetFlightState(FlightState state, bool armed)
    {
        bool airborne = state == FlightState.TakingOff || state == FlightState.Flying || state == FlightState.Landing;
        arm.SetLabel(armed ? "DISARM" : "ARM");
        arm.SetStyle(armed ? ButtonStyle.Danger : ButtonStyle.Outline);
        arm.SetAvailable(!airborne && state != FlightState.Crashed);
        takeoff.SetAvailable(armed && !airborne && state != FlightState.Crashed);
        land.SetAvailable(state == FlightState.Flying || state == FlightState.TakingOff);
    }

    // Full-strength stick labels while learning the controls, dimmed afterwards.
    public void SetLabelsProminent(bool prominent)
    {
        foreach (var t in stickLabels) t.color = prominent ? LabelBright : LabelDim;
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

    VirtualJoystick MakeStick(string name, Vector2 corner, Vector2 offset, string up, string down, string left, string right)
    {
        var js = VirtualJoystick.Create(name, root.transform, StickRadius);
        var rt = (RectTransform)js.transform;
        rt.Place(corner, corner, offset, Vector2.one * StickRadius * 2f);

        AddLabel(rt, up, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(200f, 30f), TextAnchor.LowerCenter);
        AddLabel(rt, down, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(200f, 30f), TextAnchor.UpperCenter);
        AddLabel(rt, left, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(96f, 60f), TextAnchor.MiddleRight);
        AddLabel(rt, right, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(96f, 60f), TextAnchor.MiddleLeft);
        return js;
    }

    void AddLabel(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, TextAnchor align)
    {
        var t = UIFactory.MakeText(text, parent, text, PlutoTheme.Caption, align, LabelDim);
        t.fontStyle = FontStyle.Bold;
        t.lineSpacing = 0.9f;
        t.rectTransform.Place(anchor, pivot, position, size);
        stickLabels.Add(t);
    }

    StyledButton MakeButton(string label, float x, ButtonStyle style, Action onClick)
    {
        var b = UIFactory.MakeStyledButton(label, root.transform, label, style, onClick, PlutoTheme.Label + 2);
        b.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 48f), new Vector2(ButtonWidth, ButtonHeight));
        UIFactory.ExpandTouchArea(b.Fill);
        return b;
    }
}
