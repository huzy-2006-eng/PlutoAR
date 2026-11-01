using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public struct DialogButton
{
    public string Label;
    public Color Color;
    public Action Action;

    public DialogButton(string label, Color color, Action action)
    {
        Label = label;
        Color = color;
        Action = action;
    }
}

// Heads-up display: flight status, altitude bar, instructions + live feedback,
// toasts for warnings / successes, and a generic dialog used for the menu and
// results. Panels are semi-transparent and kept to the edges so the camera
// view stays clear.
public class HUDManager : MonoBehaviour
{
    public static readonly Color Good = new Color(0.30f, 1f, 0.48f);
    public static readonly Color Warn = new Color(1f, 0.78f, 0.2f);
    public static readonly Color Bad = new Color(1f, 0.35f, 0.3f);
    public static readonly Color Info = new Color(0.4f, 0.85f, 1f);
    public static readonly Color Neutral = new Color(0.35f, 0.38f, 0.45f);

    const float AltitudeScale = 1.6f;

    public event Action MenuPressed, ReplacePressed;

    public bool DialogOpen => dialogRoot.activeSelf;

    GameObject playingRoot, instructionPanel, dialogRoot;
    Text status, title, instruction, progress, timer, center, toast, dialogTitle, dialogBody;
    Image toastBg, centerBg;
    RectTransform altFill, altTarget, dialogList;
    float toastUntil;

    public void Build(RectTransform parent)
    {
        playingRoot = UIFactory.MakeRect("HUD", parent).Stretch().gameObject;
        var p = playingRoot.transform;

        var statusBg = UIFactory.MakeImage("StatusPanel", p, new Color(0f, 0f, 0f, 0.45f));
        statusBg.raycastTarget = false;
        statusBg.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(360f, 180f));
        status = UIFactory.MakeText("Status", statusBg.transform, "", 30, TextAnchor.UpperLeft, Color.white);
        status.rectTransform.Stretch(16f, 12f);

        timer = UIFactory.MakeText("Timer", p, "", 40, TextAnchor.UpperLeft, Color.white);
        timer.fontStyle = FontStyle.Bold;
        timer.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -214f), new Vector2(360f, 60f));

        var menu = UIFactory.MakeButton("MenuButton", p, "MENU", new Color(0f, 0f, 0f, 0.6f), () => MenuPressed?.Invoke(), 30);
        ((RectTransform)menu.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(170f, 80f));
        var replace = UIFactory.MakeButton("ReplaceButton", p, "RE-PLACE", new Color(0f, 0f, 0f, 0.6f), () => ReplacePressed?.Invoke(), 30);
        ((RectTransform)replace.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-210f, -24f), new Vector2(210f, 80f));

        // Instruction panel (auto-sizes to its text)
        var panel = UIFactory.MakeImage("Instruction", p, new Color(0f, 0f, 0f, 0.5f));
        panel.raycastTarget = false;
        panel.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(900f, 100f));
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 14, 14);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        title = UIFactory.MakeText("Title", panel.transform, "", 30, TextAnchor.UpperCenter, Warn);
        title.fontStyle = FontStyle.Bold;
        instruction = UIFactory.MakeText("Body", panel.transform, "", 26, TextAnchor.UpperCenter, Color.white);
        progress = UIFactory.MakeText("Progress", panel.transform, "", 26, TextAnchor.UpperCenter, Info);
        instructionPanel = panel.gameObject;
        instructionPanel.SetActive(false);

        // Altitude bar on the right edge
        var bar = UIFactory.MakeImage("AltitudeBar", p, new Color(0f, 0f, 0f, 0.45f));
        bar.raycastTarget = false;
        bar.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 60f), new Vector2(34f, 380f));
        altFill = UIFactory.MakeImage("Fill", bar.transform, Info).rectTransform;
        altFill.anchorMin = Vector2.zero;
        altFill.anchorMax = new Vector2(1f, 0f);
        altFill.offsetMin = altFill.offsetMax = Vector2.zero;
        altTarget = UIFactory.MakeImage("Target", bar.transform, Good).rectTransform;
        altTarget.sizeDelta = new Vector2(24f, 6f);
        altTarget.gameObject.SetActive(false);
        var altLabel = UIFactory.MakeText("AltLabel", bar.transform, "ALT\n1.6m", 20, TextAnchor.LowerCenter, Color.white);
        altLabel.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(90f, 50f));

        // Centre message (scanning / tracking) and toasts live outside playingRoot
        // so they show even while the HUD is hidden.
        centerBg = UIFactory.MakeImage("CenterMessage", parent, new Color(0f, 0f, 0f, 0.6f));
        centerBg.raycastTarget = false;
        centerBg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1100f, 150f));
        center = UIFactory.MakeText("Text", centerBg.transform, "", 38, TextAnchor.MiddleCenter, Color.white);
        center.rectTransform.Stretch(20f, 10f);
        centerBg.gameObject.SetActive(false);

        toastBg = UIFactory.MakeImage("Toast", parent, new Color(0f, 0f, 0f, 0.8f));
        toastBg.raycastTarget = false;
        toastBg.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 175f), new Vector2(1000f, 76f));
        toast = UIFactory.MakeText("Text", toastBg.transform, "", 32, TextAnchor.MiddleCenter, Color.white);
        toast.fontStyle = FontStyle.Bold;
        toast.rectTransform.Stretch(16f, 4f);
        toastBg.gameObject.SetActive(false);

        // Dialog (menu / results) - full-screen dimmer blocks touches underneath.
        var dim = UIFactory.MakeImage("Dialog", parent, new Color(0f, 0f, 0f, 0.55f));
        dim.rectTransform.Stretch();
        dialogRoot = dim.gameObject;
        var box = UIFactory.MakeImage("Box", dialogRoot.transform, new Color(0.07f, 0.09f, 0.13f, 0.95f));
        box.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 200f));
        var boxLayout = box.gameObject.AddComponent<VerticalLayoutGroup>();
        boxLayout.padding = new RectOffset(36, 36, 28, 32);
        boxLayout.spacing = 14f;
        boxLayout.childControlWidth = true;
        boxLayout.childControlHeight = true;
        boxLayout.childForceExpandWidth = true;
        boxLayout.childForceExpandHeight = false;
        box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        dialogTitle = UIFactory.MakeText("Title", box.transform, "", 42, TextAnchor.MiddleCenter, Info);
        dialogTitle.fontStyle = FontStyle.Bold;
        dialogBody = UIFactory.MakeText("Body", box.transform, "", 27, TextAnchor.MiddleCenter, Color.white);
        dialogList = UIFactory.MakeRect("Buttons", box.transform);
        var listLayout = dialogList.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 10f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
        dialogRoot.SetActive(false);
    }

    public void SetPlayingVisible(bool visible) => playingRoot.SetActive(visible);

    public void SetInstruction(string heading, string body)
    {
        bool show = !string.IsNullOrEmpty(heading);
        instructionPanel.SetActive(show);
        title.text = heading ?? "";
        instruction.text = body ?? "";
        SetProgress(null);
    }

    public void SetProgress(string text)
    {
        progress.text = text ?? "";
        progress.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void SetTimer(string text) => timer.text = text ?? "";

    public void SetCenterMessage(string text)
    {
        bool show = !string.IsNullOrEmpty(text);
        if (centerBg.gameObject.activeSelf != show) centerBg.gameObject.SetActive(show);
        if (show) center.text = text;
    }

    public void Toast(string message, Color color, float seconds = 2.5f)
    {
        toast.text = message;
        toast.color = color;
        toastBg.color = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.88f);
        toastBg.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + seconds;
    }

    public void SetAltitudeTarget(float? height)
    {
        altTarget.gameObject.SetActive(height.HasValue);
        if (!height.HasValue) return;
        float t = Mathf.Clamp01(height.Value / AltitudeScale);
        altTarget.anchorMin = new Vector2(0f, t);
        altTarget.anchorMax = new Vector2(1f, t);
        altTarget.anchoredPosition = Vector2.zero;
    }

    public void SetStatus(DroneController d)
    {
        var flatSpeed = new Vector2(d.Velocity.x, d.Velocity.z).magnitude;
        status.text = FlightHelpers.Colored($"<b>{StateName(d.State)}</b>", StateColor(d.State))
            + $"\nALT   {d.Altitude:0.00} m"
            + $"\nV/S   {d.Velocity.y:+0.00;-0.00} m/s"
            + $"\nSPD   {flatSpeed:0.00} m/s";
        altFill.anchorMax = new Vector2(1f, Mathf.Clamp01(d.Altitude / AltitudeScale));
        altFill.offsetMax = Vector2.zero;
    }

    public void ShowDialog(string heading, string body, IList<DialogButton> buttons)
    {
        dialogTitle.text = heading;
        dialogBody.text = body;
        dialogBody.gameObject.SetActive(!string.IsNullOrEmpty(body));
        foreach (Transform child in dialogList)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        foreach (var b in buttons)
        {
            var action = b.Action;
            var button = UIFactory.MakeButton(b.Label, dialogList, b.Label, b.Color, action, 30);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
        }
        dialogRoot.SetActive(true);
    }

    public void HideDialog() => dialogRoot.SetActive(false);

    void Update()
    {
        if (toastBg.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBg.gameObject.SetActive(false);
    }

    public static string StateName(FlightState s)
    {
        switch (s)
        {
            case FlightState.Disarmed: return "DISARMED";
            case FlightState.Armed: return "ARMED";
            case FlightState.TakingOff: return "TAKING OFF";
            case FlightState.Flying: return "FLYING";
            case FlightState.Landing: return "LANDING";
            case FlightState.Landed: return "LANDED";
            case FlightState.Crashed: return "CRASHED";
            default: return s.ToString();
        }
    }

    public static Color StateColor(FlightState s)
    {
        switch (s)
        {
            case FlightState.Disarmed: return Bad;
            case FlightState.Crashed: return Bad;
            case FlightState.Landing: return Warn;
            case FlightState.Armed:
            case FlightState.Landed: return Info;
            default: return Good;
        }
    }
}
