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
        ArenaBuilder.Pad(Arena, Vector3.zero, PadRadius, new Color(0.3f, 0.8f, 1f), "HomePad");

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

        SetDifficulty(PlayerPrefs.GetInt("difficulty", 0));
    }

    void Start()
    {
        Controls.ArmPressed += Drone.ToggleArm;
        Controls.TakeoffPressed += Drone.Takeoff;
        Controls.LandPressed += Drone.Land;
        Drone.Warning += msg => Hud.Toast(msg, HUDManager.Warn);
        Drone.Fsm.Changed += OnFlightStateChanged;
        Hud.MenuPressed += ShowMenu;
        Hud.ReplacePressed += RequestReplace;
        Placement.Placed += OnPlaced;
        EnterScanning();
    }

    void Update()
    {
        Hud.SetCenterMessage(Placement.StatusMessage);
        if (Placement.IsPlaced && !Placement.Scanning) Hud.SetStatus(Drone);
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
        ShowMenu();
    }

    void RequestReplace()
    {
        if (Drone.Fsm.IsAirborne) { Hud.Toast("Land the drone before re-placing it.", HUDManager.Warn); return; }
        EnterScanning();
    }

    public void ShowMenu()
    {
        StopModes();
        ResetDrone();
        Drone.InputLocked = true;
        Controls.SetVisible(false);

        bool tutorialDone = PlayerPrefs.GetInt("tutorial_done", 0) == 1;
        var items = new List<DialogButton>
        {
            new DialogButton("Guided Tutorial (7 stages)" + (tutorialDone ? "   - completed" : ""), new Color(0.15f, 0.6f, 0.3f), StartTutorial)
        };
        for (int i = 0; i < MissionManager.Names.Length; i++)
        {
            int m = i;
            int best = MissionManager.Best(m);
            items.Add(new DialogButton(MissionManager.Names[m] + (best > 0 ? $"   (best {best})" : ""), new Color(0.15f, 0.4f, 0.75f), () => StartMission(m)));
        }
        items.Add(new DialogButton("Free Flight", new Color(0.15f, 0.4f, 0.75f), StartFreeFlight));
        items.Add(new DialogButton($"Difficulty: {DifficultyNames[Difficulty]}", HUDManager.Neutral, () => { SetDifficulty((Difficulty + 1) % 3); ShowMenu(); }));
        items.Add(new DialogButton($"Flight path trail: {(trailOn ? "ON" : "OFF")}", HUDManager.Neutral, () => { trailOn = !trailOn; DroneVisual.ShowTrail = trailOn; ShowMenu(); }));

        Hud.ShowDialog("PLUTO AR FLIGHT SCHOOL",
            "Learn to fly the Pluto drone in your own room - zero crashes, zero broken propellers.\nNew pilot? Start with the Guided Tutorial.",
            items);
    }

    public void StartTutorial()
    {
        BeginMode();
        Tutorial.Begin();
    }

    public void StartMission(int index)
    {
        BeginMode();
        Missions.Begin(index);
    }

    public void StartFreeFlight()
    {
        BeginMode();
        Hud.SetInstruction("FREE FLIGHT", "Practise anything you like. Press MENU when you are done.");
    }

    public void ShowResult(string heading, string body, Action retry, Action next = null, string nextLabel = "Next")
    {
        Drone.InputLocked = true;
        Controls.SetVisible(false);
        var items = new List<DialogButton>();
        if (next != null) items.Add(new DialogButton(nextLabel, new Color(0.15f, 0.6f, 0.3f), next));
        items.Add(new DialogButton("Try again", new Color(0.15f, 0.4f, 0.75f), retry));
        items.Add(new DialogButton("Main menu", HUDManager.Neutral, ShowMenu));
        Hud.ShowDialog(heading, body, items);
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
        Tutorial.Stop();
        Missions.Stop();
        Hud.SetInstruction(null, null);
        Hud.SetTimer("");
        Hud.SetAltitudeTarget(null);
    }

    public void ResetDrone() => Drone.ResetTo(Vector3.zero, 0f);

    void OnFlightStateChanged(FlightState from, FlightState to)
    {
        Controls.SetArmed(Drone.Fsm.IsArmed);
        switch (to)
        {
            case FlightState.Armed: Hud.Toast("ARMED - motors spinning", HUDManager.Good, 1.5f); break;
            case FlightState.Disarmed when from != FlightState.Crashed: Hud.Toast("DISARMED - motors stopped", HUDManager.Info, 1.5f); break;
            case FlightState.Landed: Hud.Toast("Touchdown", HUDManager.Info, 1.2f); break;
            case FlightState.Crashed: Hud.Toast("CRASH!", HUDManager.Bad, 2f); break;
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
