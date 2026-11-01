using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Entry point. Put this on one GameObject in the AR scene (Pluto > 2. Create AR
// Scene does it for you). It builds the UI, the drone and the managers at
// runtime and runs the app flow: scan -> place -> menu -> tutorial / missions.
[DefaultExecutionOrder(-100)]
public class PlutoGame : MonoBehaviour
{
    public const float PadRadius = 0.1f;
    static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };

    public static PlutoGame Instance { get; private set; }

    [Header("Drone model (optional) - drag the Pluto model here")]
    public GameObject droneModel;
    [Tooltip("Turn the model if its nose does not point forward (blue Z axis).")]
    public float droneModelYawOffset;

    [Header("Materials (assigned by Pluto > 2. Create AR Scene)")]
    public Material opaqueMaterial;
    public Material transparentMaterial;

    public ARPlacementManager Placement { get; private set; }
    public DroneController Drone { get; private set; }
    public DroneVisual DroneVisual { get; private set; }
    public ControllerUI Controls { get; private set; }
    public HUDManager Hud { get; private set; }
    public TutorialManager Tutorial { get; private set; }
    public MissionManager Missions { get; private set; }
    public RescueMission Rescue { get; private set; }
    public Transform Arena => Placement.Arena;
    public int Difficulty { get; private set; }

    readonly Dictionary<(Color, bool), Material> materials = new Dictionary<(Color, bool), Material>();
    bool trailOn;

    void Awake()
    {
        Instance = this;
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        EnsureEventSystem();
        var ui = BuildCanvas();
        Controls = gameObject.AddComponent<ControllerUI>();
        Controls.Build(ui);
        Hud = gameObject.AddComponent<HUDManager>();
        Hud.Build(ui);

        Placement = gameObject.AddComponent<ARPlacementManager>();
        ArenaBuilder.Pad(Arena, Vector3.zero, PadRadius, PlutoTheme.Cyan, "HomePad");

        var droneGo = new GameObject("PlutoDrone");
        droneGo.transform.SetParent(Arena, false);
        Drone = droneGo.AddComponent<DroneController>();
        Drone.Controls = Controls;
        DroneVisual = droneGo.AddComponent<DroneVisual>();
        DroneVisual.Build(Drone, droneModel, droneModelYawOffset);

        Tutorial = gameObject.AddComponent<TutorialManager>();
        Tutorial.Init(this);
        Missions = gameObject.AddComponent<MissionManager>();
        Missions.Init(this);
        Rescue = gameObject.AddComponent<RescueMission>();
        Rescue.Init(this);

        SetDifficulty(PlayerPrefs.GetInt("difficulty", 0));
    }

    void Start()
    {
        Controls.ArmPressed += Drone.ToggleArm;
        Controls.TakeoffPressed += Drone.Takeoff;
        Controls.LandPressed += Drone.Land;
        Drone.Warning += msg => Hud.Notify(msg, NoticePriority.Action, PlutoTheme.Amber);
        Drone.Fsm.Changed += OnFlightStateChanged;
        Hud.MenuPressed += ShowMenu;
        Hud.ReplacePressed += RequestReplace;
        Hud.HelpPressed += ShowHelp;
        Placement.Placed += OnPlaced;
        EnterScanning();
    }

    void Update()
    {
        bool scanning = Placement.Scanning;
        var tracking = Placement.Tracking;
        Hud.SetTracking(tracking, scanning);
        bool aimCue = scanning && !Placement.ReticleVisible
            && (tracking == TrackingQuality.Tracking || tracking == TrackingQuality.Limited);
        Hud.SetScanning(scanning ? Placement.ScanTitle : null, Placement.ScanBody, Placement.ReticleVisible, aimCue);
        Hud.SetTrackingNotice(Placement.TrackingNotice);
        if (Placement.IsPlaced && !scanning) Hud.SetStatus(Drone);
    }

    // ---------- flow ----------

    void EnterScanning()
    {
        StopModes();
        Hud.HideDialog();
        Hud.SetPlayingVisible(false);
        Controls.SetVisible(false);
        Drone.InputLocked = true;
        Placement.BeginScanning();
    }

    void OnPlaced()
    {
        Hud.SetPlayingVisible(true);
        Hud.Notify("Flight zone ready", NoticePriority.Objective, PlutoTheme.Mint);
        ShowMenu();
    }

    void RequestReplace()
    {
        if (Drone.Fsm.IsAirborne) { Hud.Notify("Land Pluto before re-placing it.", NoticePriority.Action, PlutoTheme.Amber); return; }
        EnterScanning();
    }

    // Welcome screen.
    public void ShowMenu()
    {
        StopModes();
        ResetDrone();
        Drone.InputLocked = true;
        Controls.SetVisible(false);

        bool tutorialDone = PlayerPrefs.GetInt("tutorial_done", 0) == 1;
        Hud.BeginDialog(860f);
        Hud.AddEyebrow("PLUTO FLIGHT ACADEMY");
        Hud.AddTitle("Your first flight starts here.");
        Hud.AddBody("Learn the controls. Build confidence. Fly safely.");
        if (tutorialDone) Hud.AddBody("Training completed", PlutoTheme.Mint).fontStyle = FontStyle.Bold;
        Hud.AddSpacer(PlutoTheme.S1);
        Hud.AddButtonRow(
            new DialogButton("Start training", ButtonStyle.Primary, StartTutorial),
            new DialogButton("Practice missions", ButtonStyle.Outline, ShowMissions));
        Hud.AddSpacer(0f);
        Hud.AddButtonRow(88f,
            new DialogButton("Free flight", ButtonStyle.Ghost, StartFreeFlight),
            new DialogButton($"Level: {DifficultyNames[Difficulty]}", ButtonStyle.Ghost, () => { SetDifficulty((Difficulty + 1) % 3); ShowMenu(); }),
            new DialogButton($"Trail: {(trailOn ? "On" : "Off")}", ButtonStyle.Ghost, () => { trailOn = !trailOn; DroneVisual.ShowTrail = trailOn; ShowMenu(); }));
    }

    public void ShowMissions()
    {
        StopModes();
        ResetDrone();
        Drone.InputLocked = true;
        Controls.SetVisible(false);

        var cards = new List<MissionCard>();
        for (int i = 0; i < MissionManager.Names.Length; i++)
        {
            int m = i;
            cards.Add(new MissionCard
            {
                Number = $"MISSION {m + 1:00}",
                Name = MissionManager.ShortNames[m],
                Description = MissionManager.Descriptions[m],
                Best = MissionManager.Best(m),
                Fly = () => StartMission(m)
            });
        }
        Hud.BeginDialog(1320f);
        Hud.AddEyebrow("PRACTICE MISSIONS");
        Hud.AddTitle("Choose your challenge", PlutoTheme.Title + 4);
        Hud.AddMissionCards(cards);
        Hud.AddBonusCard(new MissionCard
        {
            Number = "BONUS SCENARIO",
            Name = RescueMission.Name,
            Description = RescueMission.Description,
            Best = RescueMission.Best(),
            Fly = StartRescue
        });
        Hud.AddButtonRow(88f, new DialogButton("Back", ButtonStyle.Ghost, ShowMenu));
    }

    public void StartTutorial()
    {
        BeginMode();
        Tutorial.Begin();
    }

    public void StartRescue()
    {
        BeginMode();
        Rescue.Begin();
    }

    public void StartMission(int index)
    {
        BeginMode();
        Missions.Begin(index);
    }

    public void StartFreeFlight()
    {
        BeginMode();
        Hud.SetObjective("FREE FLIGHT", null, "Free flight", "Practise anything you like. Tap Menu when you are done.");
    }

    public void ShowResult(ResultSpec result, Action retry, Action next = null, string nextLabel = "Next")
    {
        Drone.InputLocked = true;
        Controls.SetVisible(false);
        Hud.ClearObjective();
        Hud.BeginDialog(780f);
        Hud.AddEyebrow(result.Eyebrow, result.Success ? PlutoTheme.Cyan : PlutoTheme.Coral);
        if (result.Score.HasValue) Hud.AddScore(result.Score.Value);
        var title = Hud.AddTitle(result.Title, PlutoTheme.Title + 6);
        if (!result.Success) title.color = PlutoTheme.Coral;
        foreach (var metric in result.Metrics) Hud.AddMetric(metric.Key, metric.Value);
        if (!string.IsNullOrEmpty(result.Feedback)) Hud.AddFeedback(result.Feedback, result.Success ? PlutoTheme.Mint : PlutoTheme.Amber);
        if (next != null) Hud.AddButtonRow(new DialogButton(nextLabel, ButtonStyle.Primary, next));
        Hud.AddButtonRow(
            new DialogButton("Retry", next == null ? ButtonStyle.Primary : ButtonStyle.Outline, retry),
            new DialogButton("Missions", ButtonStyle.Dark, ShowMissions));
    }

    // Controls reference. The simulation pauses while it is open.
    void ShowHelp()
    {
        if (Hud.DialogOpen) return;
        Time.timeScale = 0f;
        Hud.BeginDialog(1040f);
        Hud.AddEyebrow("HOW TO FLY");
        Hud.AddTitle("Controls", PlutoTheme.Title + 6);
        Hud.AddMetric("Left stick up / down", "Climb / descend");
        Hud.AddMetric("Left stick left / right", "Yaw: turn on the spot");
        Hud.AddMetric("Right stick up / down", "Fly forward / backward");
        Hud.AddMetric("Right stick left / right", "Slide left / right");
        Hud.AddMetric("ARM / DISARM", "Start or stop motors (on the ground)");
        Hud.AddMetric("TAKE OFF / LAND", "Automatic climb or descent");
        Hud.AddFeedback("Let the left stick centre to hold height. The LEDs mark Pluto's nose.", PlutoTheme.Cyan);
        Hud.AddButtonRow(new DialogButton("Back to flight", ButtonStyle.Primary, CloseHelp));
    }

    void CloseHelp()
    {
        Time.timeScale = 1f;
        Hud.HideDialog();
    }

    void BeginMode()
    {
        StopModes();
        Hud.HideDialog();
        ResetDrone();
        Controls.SetVisible(true);
        Drone.InputLocked = false;
    }

    void StopModes()
    {
        Time.timeScale = 1f;
        Tutorial.Stop();
        Missions.Stop();
        Rescue.Stop();
        Hud.ClearObjective();
        Hud.SetTimer("");
        Hud.SetAltitudeTarget(null);
    }

    public void ResetDrone()
    {
        Drone.ResetTo(Vector3.zero, 0f);
        Controls.SetFlightState(Drone.State, Drone.Fsm.IsArmed);
    }

    void OnFlightStateChanged(FlightState from, FlightState to)
    {
        Controls.SetFlightState(to, Drone.Fsm.IsArmed);
        switch (to)
        {
            case FlightState.Armed: Hud.Notify("Armed. Motors spinning.", NoticePriority.Info, PlutoTheme.Amber, 1.5f); break;
            case FlightState.Disarmed when from != FlightState.Crashed: Hud.Notify("Disarmed. Motors stopped.", NoticePriority.Info, PlutoTheme.Cyan, 1.5f); break;
            case FlightState.Landed: Hud.Notify("Touchdown", NoticePriority.Info, PlutoTheme.Cyan, 1.2f); break;
            case FlightState.Crashed: Hud.Notify("Crash", NoticePriority.Action, PlutoTheme.Coral, 2f); break;
        }
    }

    void SetDifficulty(int level)
    {
        Difficulty = Mathf.Clamp(level, 0, 2);
        PlayerPrefs.SetInt("difficulty", Difficulty);
        switch (Difficulty)
        {
            case 0: Drone.maxHorizontalSpeed = 0.5f; Drone.responsiveness = 5f; Drone.windStrength = 0f; break;
            case 1: Drone.maxHorizontalSpeed = 0.65f; Drone.responsiveness = 4f; Drone.windStrength = 0.03f; break;
            default: Drone.maxHorizontalSpeed = 0.8f; Drone.responsiveness = 3f; Drone.windStrength = 0.08f; break;
        }
    }

    // ---------- helpers ----------

    public Material MakeMaterial(Color color, bool transparent)
    {
        var key = (color, transparent);
        if (materials.TryGetValue(key, out var m)) return m;
        var template = transparent ? transparentMaterial : opaqueMaterial;
        if (template != null)
        {
            m = new Material(template);
        }
        else
        {
            var shader = Shader.Find(transparent ? "Sprites/Default" : "Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            m = new Material(shader);
        }
        m.color = color;
        materials[key] = m;
        return m;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    static RectTransform BuildCanvas()
    {
        var go = new GameObject("PlutoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var safe = UIFactory.MakeRect("SafeArea", go.transform).Stretch();
        safe.gameObject.AddComponent<SafeAreaFitter>();
        return safe;
    }
}
