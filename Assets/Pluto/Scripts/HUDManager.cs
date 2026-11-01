using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public struct DialogButton
{
    public string Label;
    public ButtonStyle Style;
    public Action Action;

    public DialogButton(string label, ButtonStyle style, Action action)
    {
        Label = label;
        Style = style;
        Action = action;
    }
}

public struct MissionCard
{
    public string Number, Name, Description;
    public int Best;
    public Action Fly;
}

// What a results card shows. Only real, measured values go in here.
public class ResultSpec
{
    public string Eyebrow, Title, Feedback;
    public int? Score;
    public bool Success = true;
    public readonly List<KeyValuePair<string, string>> Metrics = new List<KeyValuePair<string, string>>();

    public ResultSpec Metric(string label, string value)
    {
        Metrics.Add(new KeyValuePair<string, string>(label, value));
        return this;
    }
}

// Higher wins when several messages compete for the single warning area.
public enum NoticePriority { Info = 0, Objective = 1, Action = 2, Tracking = 3 }

// "Pluto Flight Academy" cockpit: brand + tracking pill + Help/Menu on top,
// objective card top-left, telemetry card top-right, one warning area, and the
// scanning card. The middle of the screen is left clear for the drone.
// Dialog screens (welcome, missions, results, help) are composed with the
// BeginDialog / Add... builder.
public class HUDManager : MonoBehaviour
{
    // Original status colours - still used by the drone's LEDs.
    public static readonly Color Good = new Color(0.30f, 1f, 0.48f);
    public static readonly Color Warn = new Color(1f, 0.78f, 0.2f);
    public static readonly Color Bad = new Color(1f, 0.35f, 0.3f);
    public static readonly Color Info = new Color(0.4f, 0.85f, 1f);
    public static readonly Color Neutral = new Color(0.35f, 0.38f, 0.45f);

    const float AltitudeScale = 1.6f;
    const float TelemetryInterval = 0.1f;
    const float TopRow = 28f;
    const float CardTop = 116f;
    const float ObjectiveWidth = 560f;
    const float TelemetryWidth = 292f;
    const float TelemetryHeight = 272f;

    public event Action MenuPressed, ReplacePressed, HelpPressed;

    public bool DialogOpen => dialogRoot.activeSelf;

    GameObject playingRoot, dialogRoot;

    // header
    Image trackDot, trackBorder;
    Text trackText;
    string trackLabel;

    // objective
    GameObject objective;
    Text objEyebrow, objStep, objTitle, objBody, objDetail, meterLabel;
    Image objBorder, meterFill;
    RectTransform meterFillRect;
    GameObject meterRow;
    bool objComplete;

    // telemetry
    Text altValue, stateText, vsText, spdText, timerText;
    Image statePill, stateBorder;
    RectTransform altFill, altTarget;
    float nextTelemetry;
    int shownAltCm = int.MinValue, shownVs = int.MinValue, shownSpd = int.MinValue;
    FlightState shownState = (FlightState)(-1);

    // notices
    readonly string[] noticeText = new string[4];
    readonly Color[] noticeColor = new Color[4];
    readonly float[] noticeUntil = new float[4];
    GameObject noticeRoot;
    Image noticeDot, noticeBorder;
    Text notice;
    int shownNotice = -1;
    string shownNoticeText;

    // scanning
    GameObject scanCard, aimCue;
    Text scanTitle, scanBody;

    // dialog
    RectTransform dialogCard;
    Transform dialogBorder;

    public void Build(RectTransform parent)
    {
        var screen = parent.parent;

        BuildHeader(parent);

        playingRoot = UIFactory.MakeRect("HUD", parent).Stretch().gameObject;
        var p = playingRoot.transform;
        BuildHeaderButtons(p);
        BuildObjective(p);
        BuildTelemetry(p);

        BuildScanning(parent, screen);
        BuildNotice(parent);
        BuildDialog(screen);
    }

    // ---------- build ----------

    void BuildHeader(RectTransform parent)
    {
        var brand = UIFactory.MakeRect("Brand", parent).Place(new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(PlutoTheme.Margin, -TopRow), new Vector2(460f, 64f));
        var mark = Deco("Mark", brand, PlutoTheme.Cyan, PlutoTheme.Circle);
        mark.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
        var name = UIFactory.MakeText("Name", brand,
            $"<b>PLUTO</b>  <color=#{PlutoTheme.HexOf(PlutoTheme.TextSecondary)}>FLIGHT ACADEMY</color>",
            PlutoTheme.Label + 2, TextAnchor.MiddleLeft, PlutoTheme.Text);
        name.rectTransform.Stretch();
        name.rectTransform.offsetMin = new Vector2(32f, 0f);

        var pill = UIFactory.MakePanel("TrackingPill", parent, 28f, 0.9f);
        pill.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TopRow), new Vector2(320f, 56f));
        trackBorder = pill.transform.Find("Border").GetComponent<Image>();
        trackDot = Deco("Dot", pill.transform, PlutoTheme.TextMuted, PlutoTheme.Circle);
        trackDot.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(14f, 14f));
        trackText = UIFactory.MakeText("Text", pill.transform, "", PlutoTheme.Caption, TextAnchor.MiddleCenter, PlutoTheme.Text);
        trackText.fontStyle = FontStyle.Bold;
        trackText.rectTransform.Stretch();
        trackText.rectTransform.offsetMin = new Vector2(44f, 0f);
        trackText.rectTransform.offsetMax = new Vector2(-20f, 0f);
    }

    void BuildHeaderButtons(Transform p)
    {
        float x = -PlutoTheme.Margin;
        x = HeaderButton(p, "MENU", ButtonStyle.Dark, 150f, x, () => MenuPressed?.Invoke());
        x = HeaderButton(p, "HELP", ButtonStyle.Dark, 150f, x, () => HelpPressed?.Invoke());
        HeaderButton(p, "RE-PLACE", ButtonStyle.Ghost, 196f, x, () => ReplacePressed?.Invoke());
    }

    float HeaderButton(Transform p, string label, ButtonStyle style, float width, float right, Action onClick)
    {
        var b = UIFactory.MakeStyledButton(label + "Button", p, label, style, onClick, PlutoTheme.Caption);
        b.Rect.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(right, -TopRow + 4f), new Vector2(width, 64f));
        UIFactory.ExpandTouchArea(b.Fill);
        return right - width - PlutoTheme.S2;
    }

    void BuildObjective(Transform p)
    {
        var card = UIFactory.MakePanel("Objective", p);
        card.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PlutoTheme.Margin, -CardTop), new Vector2(ObjectiveWidth, 0f));
        objBorder = card.transform.Find("Border").GetComponent<Image>();
        UIFactory.AddVerticalLayout(card.gameObject, 28, 22, PlutoTheme.S1 + 2f);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        objective = card.gameObject;

        var head = UIFactory.MakeRect("Head", card.transform);
        head.Height(28f);
        objEyebrow = UIFactory.MakeText("Eyebrow", head, "", PlutoTheme.Caption, TextAnchor.MiddleLeft, PlutoTheme.Cyan);
        objEyebrow.fontStyle = FontStyle.Bold;
        objEyebrow.rectTransform.Stretch();
        objEyebrow.rectTransform.offsetMax = new Vector2(-110f, 0f);
        objEyebrow.horizontalOverflow = HorizontalWrapMode.Overflow;
        objStep = UIFactory.MakeText("Step", head, "", PlutoTheme.Caption, TextAnchor.MiddleRight, PlutoTheme.TextMuted);
        objStep.fontStyle = FontStyle.Bold;
        objStep.rectTransform.Stretch();

        objTitle = UIFactory.MakeText("Title", card.transform, "", PlutoTheme.Title, TextAnchor.UpperLeft, PlutoTheme.Text);
        objTitle.fontStyle = FontStyle.Bold;
        objBody = UIFactory.MakeText("Body", card.transform, "", PlutoTheme.Body - 2, TextAnchor.UpperLeft, PlutoTheme.TextSecondary);
        objDetail = UIFactory.MakeText("Detail", card.transform, "", PlutoTheme.Label, TextAnchor.UpperLeft, PlutoTheme.Cyan);
        objDetail.lineSpacing = 1.1f;

        var row = UIFactory.MakeRect("Meter", card.transform);
        row.Height(30f);
        meterRow = row.gameObject;
        var track = UIFactory.MakeRounded("Track", row, PlutoTheme.Raised, 5f);
        track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        track.rectTransform.offsetMin = new Vector2(0f, -5f);
        track.rectTransform.offsetMax = new Vector2(-150f, 5f);
        meterFill = UIFactory.MakeRounded("Fill", track.transform, PlutoTheme.Cyan, 5f);
        meterFillRect = meterFill.rectTransform;
        meterFillRect.anchorMin = Vector2.zero;
        meterFillRect.anchorMax = new Vector2(0f, 1f);
        meterFillRect.offsetMin = meterFillRect.offsetMax = Vector2.zero;
        meterLabel = UIFactory.MakeText("Label", row, "", PlutoTheme.Caption, TextAnchor.MiddleRight, PlutoTheme.TextSecondary);
        meterLabel.rectTransform.Stretch();
        meterLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

        objective.SetActive(false);
    }

    void BuildTelemetry(Transform p)
    {
        var card = UIFactory.MakePanel("Telemetry", p);
        card.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-PlutoTheme.Margin, -CardTop),
            new Vector2(TelemetryWidth, TelemetryHeight));
        var c = card.transform;

        TopLeftText(c, "ALTITUDE", 20f, PlutoTheme.Caption, PlutoTheme.TextMuted, 28f).fontStyle = FontStyle.Bold;
        altValue = TopLeftText(c, "", 50f, PlutoTheme.Headline, PlutoTheme.Text, 62f);
        altValue.fontStyle = FontStyle.Bold;

        statePill = UIFactory.MakeRounded("State", c, PlutoTheme.Raised, 20f);
        statePill.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -122f), new Vector2(186f, 40f));
        stateBorder = UIFactory.AddBorder(statePill.rectTransform, PlutoTheme.Border, 20f);
        stateText = UIFactory.MakeText("Text", statePill.transform, "", PlutoTheme.Caption - 2, TextAnchor.MiddleCenter, PlutoTheme.Text);
        stateText.fontStyle = FontStyle.Bold;
        stateText.rectTransform.Stretch();

        vsText = TopLeftText(c, "", 176f, PlutoTheme.Caption, PlutoTheme.TextSecondary, 28f);
        spdText = TopLeftText(c, "", 204f, PlutoTheme.Caption, PlutoTheme.TextSecondary, 28f);
        timerText = TopLeftText(c, "", 232f, PlutoTheme.Caption, PlutoTheme.Text, 28f);
        timerText.fontStyle = FontStyle.Bold;

        var bar = UIFactory.MakeRounded("AltitudeBar", c, PlutoTheme.Raised, 5f);
        bar.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(10f, TelemetryHeight - 48f));
        altFill = UIFactory.MakeRounded("Fill", bar.transform, PlutoTheme.Cyan, 5f).rectTransform;
        altFill.anchorMin = Vector2.zero;
        altFill.anchorMax = new Vector2(1f, 0f);
        altFill.offsetMin = altFill.offsetMax = Vector2.zero;
        altTarget = Deco("Target", bar.transform, PlutoTheme.Mint).rectTransform;
        altTarget.sizeDelta = new Vector2(28f, 4f);
        altTarget.gameObject.SetActive(false);
    }

    Text TopLeftText(Transform parent, string text, float y, int size, Color color, float height)
    {
        var t = UIFactory.MakeText("Text", parent, text, size, TextAnchor.MiddleLeft, color);
        t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -y), new Vector2(TelemetryWidth - 80f, height));
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    void BuildScanning(RectTransform parent, Transform screen)
    {
        var card = UIFactory.MakePanel("ScanCard", parent);
        card.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -CardTop), new Vector2(820f, 0f));
        UIFactory.AddVerticalLayout(card.gameObject, 36, 24, PlutoTheme.S1, TextAnchor.UpperCenter);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scanTitle = UIFactory.MakeText("Title", card.transform, "", PlutoTheme.Title, TextAnchor.UpperCenter, PlutoTheme.Text);
        scanTitle.fontStyle = FontStyle.Bold;
        scanBody = UIFactory.MakeText("Body", card.transform, "", PlutoTheme.Body - 2, TextAnchor.UpperCenter, PlutoTheme.TextSecondary);
        scanCard = card.gameObject;
        scanCard.SetActive(false);

        // Amber aiming cue in the exact screen centre, where the placement ray goes.
        var cue = UIFactory.MakeRect("AimCue", screen);
        cue.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f));
        Deco("Ring", cue, PlutoTheme.Amber.A(0.9f), PlutoTheme.CircleOutline).rectTransform.Stretch();
        Deco("Dot", cue, PlutoTheme.Amber, PlutoTheme.Circle).rectTransform.sizeDelta = new Vector2(14f, 14f);
        aimCue = cue.gameObject;
        aimCue.SetActive(false);
    }

    void BuildNotice(RectTransform parent)
    {
        var pill = UIFactory.MakeRounded("Notice", parent, PlutoTheme.Deep.A(0.92f), 30f);
        // Fixed width that fits between the objective and telemetry cards; long text wraps.
        pill.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -CardTop + 8f), new Vector2(700f, 60f));
        noticeBorder = UIFactory.AddBorder(pill.rectTransform, PlutoTheme.Amber, 30f);
        var layout = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(26, 30, 14, 14);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        pill.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        noticeDot = Deco("Dot", pill.transform, PlutoTheme.Amber, PlutoTheme.Circle);
        var dotSize = noticeDot.gameObject.AddComponent<LayoutElement>();
        dotSize.preferredWidth = dotSize.preferredHeight = 16f;
        notice = UIFactory.MakeText("Text", pill.transform, "", PlutoTheme.Body - 2, TextAnchor.MiddleCenter, PlutoTheme.Text);
        notice.fontStyle = FontStyle.Bold;
        notice.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        noticeRoot = pill.gameObject;
        noticeRoot.SetActive(false);
    }

    void BuildDialog(Transform screen)
    {
        var dim = UIFactory.MakeImage("Dialog", screen, PlutoTheme.Deep.A(0.62f));
        dim.rectTransform.Stretch();
        dialogRoot = dim.gameObject;
        var card = UIFactory.MakePanel("Card", dialogRoot.transform, 28f, 0.96f, true);
        card.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 200f));
        UIFactory.AddVerticalLayout(card.gameObject, 48, 40, PlutoTheme.S2, TextAnchor.UpperCenter);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        dialogCard = card.rectTransform;
        dialogBorder = card.transform.Find("Border");
        dialogRoot.SetActive(false);
    }

    static Image Deco(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var img = UIFactory.MakeImage(name, parent, color, sprite);
        img.raycastTarget = false;
        return img;
    }

    // ---------- header / tracking ----------

    public void SetPlayingVisible(bool visible) => playingRoot.SetActive(visible);

    public void SetTracking(TrackingQuality quality, bool scanning)
    {
        string label;
        Color color;
        switch (quality)
        {
            case TrackingQuality.Editor: label = "EDITOR TEST"; color = PlutoTheme.Cyan; break;
            case TrackingQuality.Starting: label = "STARTING"; color = PlutoTheme.TextMuted; break;
            case TrackingQuality.Unsupported: label = "AR UNAVAILABLE"; color = PlutoTheme.Coral; break;
            case TrackingQuality.Limited:
                label = scanning ? "LIMITED TRACKING" : "TRACKING LOST";
                color = scanning ? PlutoTheme.Amber : PlutoTheme.Coral;
                break;
            default:
                label = scanning ? "SCANNING" : "TRACKING";
                color = scanning ? PlutoTheme.Cyan : PlutoTheme.Mint;
                break;
        }
        if (ReferenceEquals(label, trackLabel)) return;
        trackLabel = label;
        trackText.text = label;
        trackDot.color = color;
        trackBorder.color = color.A(0.55f);
    }

    // ---------- objective card ----------

    public void SetObjective(string eyebrow, string step, string title, string body)
    {
        bool show = !string.IsNullOrEmpty(title);
        objective.SetActive(show);
        if (!show) return;
        objEyebrow.text = eyebrow ?? "";
        objStep.text = step ?? "";
        objTitle.text = title;
        objBody.text = body ?? "";
        objBody.gameObject.SetActive(!string.IsNullOrEmpty(body));
        SetObjectiveDetail(null);
        SetObjectiveMeter(null, null);
        objComplete = true;
        SetObjectiveComplete(false);
    }

    public void ClearObjective() => objective.SetActive(false);

    public void SetObjectiveDetail(string text)
    {
        bool show = !string.IsNullOrEmpty(text);
        if (objDetail.gameObject.activeSelf != show) objDetail.gameObject.SetActive(show);
        if (show) objDetail.text = text;
    }

    public void SetObjectiveMeter(float? value, string label)
    {
        bool show = value.HasValue;
        if (meterRow.activeSelf != show) meterRow.SetActive(show);
        if (!show) return;
        float v = Mathf.Clamp01(value.Value);
        if (!Mathf.Approximately(meterFillRect.anchorMax.x, v))
        {
            meterFillRect.anchorMax = new Vector2(v, 1f);
            meterFillRect.offsetMax = Vector2.zero;
        }
        meterLabel.text = label ?? "";
    }

    // Mint "step done" treatment while the runner waits to advance.
    public void SetObjectiveComplete(bool complete)
    {
        if (complete == objComplete) return;
        objComplete = complete;
        objBorder.color = complete ? PlutoTheme.Mint : PlutoTheme.Border;
        objEyebrow.color = complete ? PlutoTheme.Mint : PlutoTheme.Cyan;
        meterFill.color = complete ? PlutoTheme.Mint : PlutoTheme.Cyan;
    }

    // ---------- telemetry ----------

    public void SetTimer(string text)
    {
        bool show = !string.IsNullOrEmpty(text);
        if (timerText.gameObject.activeSelf != show) timerText.gameObject.SetActive(show);
        if (show) timerText.text = text;
    }

    public void SetAltitudeTarget(float? height)
    {
        altTarget.gameObject.SetActive(height.HasValue);
        if (!height.HasValue) return;
        float t = Mathf.Clamp01(height.Value / AltitudeScale);
        altTarget.anchorMin = new Vector2(0.5f, t);
        altTarget.anchorMax = new Vector2(0.5f, t);
        altTarget.anchoredPosition = Vector2.zero;
    }

    // Called every frame; refreshes at 10 Hz and only touches text that changed.
    public void SetStatus(DroneController d)
    {
        if (Time.unscaledTime < nextTelemetry && d.State == shownState) return;
        nextTelemetry = Time.unscaledTime + TelemetryInterval;

        int alt = Mathf.RoundToInt(d.Altitude * 100f);
        if (alt != shownAltCm)
        {
            shownAltCm = alt;
            altValue.text = $"{alt / 100f:0.00}<size={PlutoTheme.Body}><color=#{PlutoTheme.HexOf(PlutoTheme.TextSecondary)}> m</color></size>";
            altFill.anchorMax = new Vector2(1f, Mathf.Clamp01(d.Altitude / AltitudeScale));
            altFill.offsetMax = Vector2.zero;
        }
        int vs = Mathf.RoundToInt(d.Velocity.y * 100f);
        if (vs != shownVs)
        {
            shownVs = vs;
            vsText.text = $"V/S   {vs / 100f:+0.00;-0.00;0.00} m/s";
        }
        int spd = Mathf.RoundToInt(new Vector2(d.Velocity.x, d.Velocity.z).magnitude * 100f);
        if (spd != shownSpd)
        {
            shownSpd = spd;
            spdText.text = $"SPD   {spd / 100f:0.00} m/s";
        }
        if (d.State != shownState)
        {
            shownState = d.State;
            var c = StateColor(d.State);
            stateText.text = StateName(d.State);
            stateText.color = c;
            stateBorder.color = c.A(0.7f);
            statePill.color = c.A(0.14f);
        }
    }

    // ---------- warning area ----------

    public void Notify(string message, NoticePriority priority, Color color, float seconds = 2.5f)
    {
        int i = (int)priority;
        noticeText[i] = message;
        noticeColor[i] = color;
        noticeUntil[i] = Time.unscaledTime + seconds;
    }

    // Persistent until cleared with null (tracking problems).
    public void SetTrackingNotice(string message)
    {
        int i = (int)NoticePriority.Tracking;
        noticeText[i] = message;
        noticeColor[i] = PlutoTheme.Coral;
        noticeUntil[i] = float.PositiveInfinity;
    }

    void UpdateNotice()
    {
        int best = -1;
        for (int i = noticeText.Length - 1; i >= 0; i--)
        {
            if (noticeText[i] == null) continue;
            if (Time.unscaledTime > noticeUntil[i]) { noticeText[i] = null; continue; }
            best = i;
            break;
        }
        if (best == shownNotice && (best < 0 || ReferenceEquals(noticeText[best], shownNoticeText))) return;
        shownNotice = best;
        noticeRoot.SetActive(best >= 0);
        if (best < 0) { shownNoticeText = null; return; }
        shownNoticeText = noticeText[best];
        notice.text = shownNoticeText;
        noticeDot.color = noticeColor[best];
        noticeBorder.color = noticeColor[best].A(0.8f);
    }

    // ---------- scanning ----------

    public void SetScanning(string title, string body, bool ready, bool showAimCue)
    {
        bool show = !string.IsNullOrEmpty(title);
        if (scanCard.activeSelf != show) scanCard.SetActive(show);
        if (aimCue.activeSelf != showAimCue) aimCue.SetActive(showAimCue);
        if (!show) return;
        scanTitle.text = title;
        scanTitle.color = ready ? PlutoTheme.Mint : PlutoTheme.Text;
        scanBody.text = body ?? "";
    }

    // ---------- dialog builder ----------

    public void BeginDialog(float width = 820f)
    {
        foreach (Transform child in dialogCard)
        {
            if (child == dialogBorder) continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        dialogCard.sizeDelta = new Vector2(width, dialogCard.sizeDelta.y);
        dialogRoot.SetActive(true);
    }

    public void HideDialog() => dialogRoot.SetActive(false);

    public Text AddEyebrow(string text, Color? color = null)
    {
        var t = UIFactory.MakeText("Eyebrow", dialogCard, text, PlutoTheme.Label, TextAnchor.MiddleCenter, color ?? PlutoTheme.Cyan);
        t.fontStyle = FontStyle.Bold;
        return t;
    }

    public Text AddTitle(string text, int size = PlutoTheme.Headline)
    {
        var t = UIFactory.MakeText("Title", dialogCard, text, size, TextAnchor.MiddleCenter, PlutoTheme.Text);
        t.fontStyle = FontStyle.Bold;
        return t;
    }

    public Text AddBody(string text, Color? color = null) =>
        UIFactory.MakeText("Body", dialogCard, text, PlutoTheme.Body, TextAnchor.MiddleCenter, color ?? PlutoTheme.TextSecondary);

    public void AddScore(int score)
    {
        var t = UIFactory.MakeText("Score", dialogCard, score.ToString(), PlutoTheme.Display, TextAnchor.MiddleCenter, PlutoTheme.Cyan);
        t.fontStyle = FontStyle.Bold;
        UIFactory.MakeText("Caption", dialogCard, "POINTS", PlutoTheme.Caption, TextAnchor.MiddleCenter, PlutoTheme.TextMuted).fontStyle = FontStyle.Bold;
    }

    public void AddMetric(string label, string value)
    {
        var row = UIFactory.MakeRect("Metric", dialogCard);
        row.Height(46f);
        var line = Deco("Line", row, PlutoTheme.Border.A(0.6f));
        line.rectTransform.anchorMin = Vector2.zero;
        line.rectTransform.anchorMax = new Vector2(1f, 0f);
        line.rectTransform.offsetMin = Vector2.zero;
        line.rectTransform.offsetMax = new Vector2(0f, 2f);
        UIFactory.MakeText("Label", row, label, PlutoTheme.Body - 2, TextAnchor.MiddleLeft, PlutoTheme.TextSecondary).rectTransform.Stretch();
        var v = UIFactory.MakeText("Value", row, value, PlutoTheme.Body - 2, TextAnchor.MiddleRight, PlutoTheme.Text);
        v.fontStyle = FontStyle.Bold;
        v.rectTransform.Stretch();
    }

    public void AddFeedback(string text, Color color)
    {
        var box = UIFactory.MakeRounded("Feedback", dialogCard, color.A(0.12f), PlutoTheme.ButtonRadius);
        UIFactory.AddBorder(box.rectTransform, color.A(0.5f), PlutoTheme.ButtonRadius);
        UIFactory.AddVerticalLayout(box.gameObject, 24, 16, 0f);
        UIFactory.MakeText("Text", box.transform, text, PlutoTheme.Body - 2, TextAnchor.MiddleCenter, PlutoTheme.Text);
    }

    public void AddSpacer(float height) => UIFactory.MakeRect("Spacer", dialogCard).Height(height);

    public void AddButtonRow(params DialogButton[] buttons) => AddButtonRow(PlutoTheme.TouchHeight - 28f, buttons);

    public void AddButtonRow(float height, params DialogButton[] buttons)
    {
        var row = UIFactory.MakeRect("Buttons", dialogCard);
        row.Height(height);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = PlutoTheme.S2;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        foreach (var b in buttons)
        {
            var sb = UIFactory.MakeStyledButton(b.Label, row, b.Label, b.Style, b.Action, PlutoTheme.Label + 2);
            float pad = Mathf.Max(0f, (PlutoTheme.TouchHeight - height) / 2f);
            sb.Fill.raycastPadding = new Vector4(0f, -pad, 0f, -pad);
        }
    }

    public void AddMissionCards(IList<MissionCard> missions)
    {
        var row = UIFactory.MakeRect("Missions", dialogCard);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = PlutoTheme.S3;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        foreach (var m in missions)
        {
            var card = UIFactory.MakeRounded(m.Name, row, PlutoTheme.Raised.A(0.85f), PlutoTheme.CardRadius);
            UIFactory.AddBorder(card.rectTransform, PlutoTheme.Border, PlutoTheme.CardRadius);
            UIFactory.AddVerticalLayout(card.gameObject, 28, 26, PlutoTheme.S1 + 2f);
            // Equal widths regardless of text length.
            var size = card.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 0f;
            size.preferredWidth = 0f;
            size.flexibleWidth = 1f;
            var c = card.transform;
            UIFactory.MakeText("Number", c, m.Number, PlutoTheme.Caption, TextAnchor.UpperLeft, PlutoTheme.Cyan).fontStyle = FontStyle.Bold;
            UIFactory.MakeText("Name", c, m.Name, PlutoTheme.Title - 4, TextAnchor.UpperLeft, PlutoTheme.Text).fontStyle = FontStyle.Bold;
            UIFactory.MakeText("Description", c, m.Description, PlutoTheme.Label, TextAnchor.UpperLeft, PlutoTheme.TextSecondary);
            UIFactory.MakeRect("Fill", c).gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UIFactory.MakeText("Best", c, m.Best > 0 ? $"Best score  <b>{m.Best}</b>" : "Not flown yet",
                PlutoTheme.Label, TextAnchor.UpperLeft, m.Best > 0 ? PlutoTheme.Mint : PlutoTheme.TextMuted);
            var fly = UIFactory.MakeStyledButton("Fly", c, "FLY MISSION", ButtonStyle.Primary, m.Fly, PlutoTheme.Label);
            fly.Fill.Height(96f);
            fly.Fill.raycastPadding = new Vector4(0f, -18f, 0f, -18f);
        }
    }

    // Full-width card for the bonus scenario, kept visually apart from Missions 01-03.
    public void AddBonusCard(MissionCard m)
    {
        var card = UIFactory.MakeRounded(m.Name, dialogCard, PlutoTheme.Raised.A(0.85f), PlutoTheme.CardRadius);
        UIFactory.AddBorder(card.rectTransform, PlutoTheme.Amber.A(0.6f), PlutoTheme.CardRadius);
        card.Height(150f);
        var c = card.transform;
        var text = UIFactory.MakeRect("Text", c);
        text.Stretch();
        text.offsetMin = new Vector2(28f, 18f);
        text.offsetMax = new Vector2(-300f, -18f);
        UIFactory.AddVerticalLayout(text.gameObject, 0, 0, PlutoTheme.S1 - 2f, TextAnchor.MiddleLeft);
        UIFactory.MakeText("Number", text, m.Number, PlutoTheme.Caption, TextAnchor.UpperLeft, PlutoTheme.Amber).fontStyle = FontStyle.Bold;
        UIFactory.MakeText("Name", text, m.Name + (m.Best > 0 ? $"   <size={PlutoTheme.Label}><color=#{PlutoTheme.HexOf(PlutoTheme.Mint)}>Best score  {m.Best}</color></size>" : ""),
            PlutoTheme.Title - 4, TextAnchor.UpperLeft, PlutoTheme.Text).fontStyle = FontStyle.Bold;
        UIFactory.MakeText("Description", text, m.Description, PlutoTheme.Label, TextAnchor.UpperLeft, PlutoTheme.TextSecondary);
        var fly = UIFactory.MakeStyledButton("Fly", c, "FLY SCENARIO", ButtonStyle.Primary, m.Fly, PlutoTheme.Label);
        fly.Rect.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(250f, 96f));
        fly.Fill.raycastPadding = new Vector4(0f, -18f, 0f, -18f);
    }

    // ---------- loop ----------

    void Update()
    {
        UpdateNotice();
        if (aimCue.activeSelf)
            aimCue.transform.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f));
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
            case FlightState.Disarmed: return PlutoTheme.TextSecondary;
            case FlightState.Armed: return PlutoTheme.Amber;
            case FlightState.Crashed: return PlutoTheme.Coral;
            case FlightState.Flying: return PlutoTheme.Mint;
            default: return PlutoTheme.Cyan;
        }
    }
}
