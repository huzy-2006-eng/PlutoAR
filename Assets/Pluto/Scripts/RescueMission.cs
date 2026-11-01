using System.Collections.Generic;
using UnityEngine;

// Bonus scenario "Pluto Rescue - Flood Response Training".
// A tabletop-scale flooded block: home pad A, three buildings standing in
// floodwater, and rescue rooftop B with a survivor marker. Every objective is
// completed from real flight telemetry; nothing flies or moves the drone.
//
//   Arm -> Take off -> Navigate to B -> Hold over B -> Return to A and land -> Disarm
public class RescueMission : GuidedMode
{
    public const string Name = "PLUTO RESCUE";
    public const string Description = "Flood response drill: fly to the stranded survivor's rooftop, hold position over it, then return home.";
    const string BestKey = "best_rescue";

    // Layout (arena-local metres). Building B is the rescue rooftop.
    public static readonly Vector3 BuildingB = new Vector3(0.35f, 0f, 1.2f);
    public static readonly Vector3 BuildingBSize = new Vector3(0.36f, 0.30f, 0.36f);
    static readonly Vector3[] OtherBuildings = { new Vector3(-0.15f, 0f, 0.65f), new Vector3(-0.5f, 0f, 1.25f) };
    static readonly Vector3[] OtherSizes = { new Vector3(0.22f, 0.45f, 0.22f), new Vector3(0.26f, 0.36f, 0.26f) };

    // Hover region above rooftop B (drone centre must be inside it).
    public static readonly Vector3 HoldCenter = BuildingB + new Vector3(0f, BuildingBSize.y + 0.25f, 0f);
    public static readonly Vector3 HoldSize = new Vector3(0.3f, 0.2f, 0.3f);
    const float HoldSeconds = 3f;
    const float TakeoffHeight = 0.4f;

    static readonly Color Concrete = new Color(0.42f, 0.47f, 0.55f);
    static readonly Color RoofGrey = new Color(0.58f, 0.62f, 0.69f);
    static readonly Color Water = new Color(0.16f, 0.45f, 0.72f, 0.55f);
    static readonly Color Survivor = new Color(1f, 0.55f, 0.15f);
    static readonly Color BaseGround = new Color(0.24f, 0.27f, 0.31f);

    readonly List<Bounds> obstacles = new List<Bounds>();
    bool hardLanding, lastTouchOnPad;

    protected override string Label => "BONUS  ·  " + Name;

    // Freeze the timer, holds and collision checks while AR tracking is lost.
    protected override bool Paused => game.Placement.Tracking == TrackingQuality.Limited && !game.Placement.Scanning;

    public static int Best() => PlayerPrefs.GetInt(BestKey, 0);

    public void Begin()
    {
        Stop();
        hardLanding = false;
        lastTouchOnPad = false;
        BuildEnvironment();
        Drone.Touchdown += OnTouchdown;
        Drone.Crashed += OnCrashed;
        runner.Start(new List<Stage> { ArmStage(), TakeoffStage(), NavigateStage(), HoldStage(), ReturnStage(), DisarmStage() });
    }

    public override void Stop()
    {
        if (game != null)
        {
            Drone.Touchdown -= OnTouchdown;
            Drone.Crashed -= OnCrashed;
        }
        obstacles.Clear();
        base.Stop();
    }

    protected override void Update()
    {
        base.Update();
        if (!runner.Running || Paused || !Drone.Fsm.IsAirborne || Drone.State == FlightState.Crashed) return;
        foreach (var b in obstacles)
        {
            var hit = b;
            hit.Expand(new Vector3(0.1f, 0.03f, 0.1f)); // same drone margin as the pillar mission
            if (hit.Contains(Drone.Center))
            {
                Drone.Crash("You flew into a building.");
                return;
            }
        }
    }

    void OnTouchdown(float speed, Vector3 pos)
    {
        lastTouchOnPad = FlightHelpers.OnPad(pos, Vector3.zero, PlutoGame.PadRadius);
        if (speed < -Drone.hardLandingSpeed) hardLanding = true;
    }

    void OnCrashed(string reason)
    {
        Stop();
        game.ShowResult(new ResultSpec
        {
            Eyebrow = Label,
            Title = "Mission failed",
            Success = false,
            Feedback = reason + " Climb above the rooftops before crossing the flooded block."
        }, game.StartRescue);
    }

    protected override void OnAllCompleted()
    {
        float t = runner.TotalTime;
        int score = Mathf.Max(100, 1500 - Mathf.RoundToInt(8f * t) - (hardLanding ? 200 : 0));
        int previousBest = Best();
        bool newBest = score > previousBest;
        if (newBest)
        {
            PlayerPrefs.SetInt(BestKey, score);
            PlayerPrefs.Save();
        }
        var result = new ResultSpec
        {
            Eyebrow = Label,
            Title = newBest ? "New best score" : "Rescue complete",
            Score = score,
            Feedback = hardLanding ? "Ease the left stick down more slowly for a softer touchdown."
                : "Survivor located and Pluto home safely. Now try for a faster time."
        }
            .Metric("Time", FlightHelpers.FormatTime(t))
            .Metric("Rooftop hold", $"{HoldSeconds:0} s completed")
            .Metric("Buildings hit", "0")
            .Metric("Hard landing", hardLanding ? "Yes" : "No");
        if (!newBest) result.Metric("Best score", previousBest.ToString());
        Stop();
        game.ShowResult(result, game.StartRescue);
    }

    // ---------- environment ----------

    void BuildEnvironment()
    {
        obstacles.Clear();
        var root = Spawn(new GameObject("RescueScenario")).transform;
        root.SetParent(Arena, false);

        // Dry rescue base under home pad A, meeting the water's edge so pad and map
        // read as one surface. Kept below the pad disc (bottom at 0.6 mm) to avoid z-fighting.
        ArenaBuilder.Primitive(PrimitiveType.Cube, "RescueBase", root, new Vector3(0f, 0.0002f, 0.025f),
            new Vector3(1.6f, 0.0004f, 0.65f), BaseGround, false);
        ArenaBuilder.Primitive(PrimitiveType.Cube, "Shoreline", root, new Vector3(0f, 0.0015f, 0.35f),
            new Vector3(1.6f, 0.003f, 0.012f), RoofGrey, false);

        // Floodwater: one thin translucent slab plus a few ripple rings (cheap).
        ArenaBuilder.Primitive(PrimitiveType.Cube, "Floodwater", root, new Vector3(0f, 0.003f, 1.0f),
            new Vector3(1.6f, 0.004f, 1.3f), Water, true);
        ArenaBuilder.Circle(root, new Vector3(0.05f, 0.007f, 0.95f), Quaternion.identity, 0.12f, Color.white.A(0.25f), 0.003f, true);
        ArenaBuilder.Circle(root, new Vector3(-0.3f, 0.007f, 0.45f), Quaternion.identity, 0.08f, Color.white.A(0.2f), 0.003f, true);
        ArenaBuilder.Circle(root, new Vector3(0.55f, 0.007f, 0.7f), Quaternion.identity, 0.1f, Color.white.A(0.2f), 0.003f, true);

        AddBuilding(root, "BuildingB", BuildingB, BuildingBSize);
        for (int i = 0; i < OtherBuildings.Length; i++) AddBuilding(root, "Building" + (i + 1), OtherBuildings[i], OtherSizes[i]);

        // Rooftop B: cyan landing ring, survivor marker and a beam to spot it from the pad.
        float roof = BuildingBSize.y;
        ArenaBuilder.Circle(root, BuildingB + new Vector3(0f, roof + 0.003f, 0f), Quaternion.identity, 0.14f, PlutoTheme.Cyan, 0.006f, true);
        var survivor = new GameObject("Survivor").transform;
        survivor.SetParent(root, false);
        survivor.localPosition = BuildingB + new Vector3(0.09f, roof, 0.09f);
        ArenaBuilder.Primitive(PrimitiveType.Capsule, "Body", survivor, new Vector3(0f, 0.03f, 0f), new Vector3(0.03f, 0.03f, 0.03f), Survivor, false, true);
        ArenaBuilder.Primitive(PrimitiveType.Sphere, "Head", survivor, new Vector3(0f, 0.072f, 0f), Vector3.one * 0.026f, new Color(1f, 0.85f, 0.7f), false);
        ArenaBuilder.Circle(survivor, new Vector3(0f, 0.004f, 0f), Quaternion.identity, 0.04f, PlutoTheme.Amber, 0.004f, true);
        Beam(root, BuildingB + new Vector3(0f, roof, 0f), 0.75f, PlutoTheme.Cyan);
    }

    void AddBuilding(Transform root, string name, Vector3 basePos, Vector3 size)
    {
        var center = basePos + Vector3.up * size.y / 2f;
        ArenaBuilder.Primitive(PrimitiveType.Cube, name, root, center, size, Concrete, false, true);
        // Roof cap and a dark window band so the blocks read as buildings.
        ArenaBuilder.Primitive(PrimitiveType.Cube, name + "Roof", root, basePos + Vector3.up * (size.y + 0.004f),
            new Vector3(size.x + 0.01f, 0.008f, size.z + 0.01f), RoofGrey, false);
        ArenaBuilder.Primitive(PrimitiveType.Cube, name + "Windows", root, basePos + Vector3.up * (size.y * 0.62f),
            new Vector3(size.x + 0.004f, size.y * 0.12f, size.z + 0.004f), new Color(0.12f, 0.16f, 0.22f), false);
        obstacles.Add(new Bounds(center, size));
    }

    static GameObject Beam(Transform root, Vector3 from, float height, Color color) =>
        ArenaBuilder.Primitive(PrimitiveType.Cylinder, "Beam", root, from + Vector3.up * height / 2f,
            new Vector3(0.008f, height / 2f, 0.008f), color.A(0.45f), true);

    // ---------- stages ----------

    Stage ArmStage() => new Stage
    {
        Title = "Ready on pad A",
        Instruction = "A survivor is stranded on a flooded rooftop. Tap ARM on the home pad to start.",
        OnEnter = () => game.Controls.Highlight(game.Controls.ArmButton),
        OnExit = () => game.Controls.Highlight(null),
        IsComplete = () => Drone.Fsm.IsArmed,
        Progress = () => "Waiting for ARM."
    };

    Stage TakeoffStage() => new Stage
    {
        Title = "Depart pad A",
        Instruction = $"Take off and climb above {TakeoffHeight:0.0} m before heading over the water.",
        OnEnter = () => Hud.SetAltitudeTarget(TakeoffHeight),
        OnExit = () => Hud.SetAltitudeTarget(null),
        IsComplete = () => Drone.Fsm.IsAirborne && Drone.Altitude >= TakeoffHeight,
        Progress = () => Drone.State == FlightState.Disarmed ? "Tap ARM first." : "Climb: left stick up or TAKE OFF.",
        Meter = () => Drone.Altitude / TakeoffHeight,
        MeterLabel = () => $"{Drone.Altitude:0.00} / {TakeoffHeight:0.00} m"
    };

    Stage NavigateStage()
    {
        float startDistance = 1f;
        return new Stage
        {
            Title = "Navigate to rooftop B",
            Instruction = "Follow the cyan beam to the survivor's rooftop. Keep clear of the buildings.",
            OnEnter = () =>
            {
                startDistance = Mathf.Max(0.1f, Vector3.Distance(Drone.Center, HoldCenter));
                Hud.SetAltitudeTarget(HoldCenter.y);
            },
            IsComplete = () => Drone.Fsm.IsAirborne && FlightHelpers.InBox(Drone.Center, HoldCenter, HoldSize),
            Progress = () => !Drone.Fsm.IsAirborne ? FlightHelpers.Warn("Take off again: left stick up.")
                : "Rooftop B: " + FlightHelpers.Direction(Drone, HoldCenter),
            Meter = () => 1f - Vector3.Distance(Drone.Center, HoldCenter) / startDistance,
            MeterLabel = () => $"{Vector3.Distance(Drone.Center, HoldCenter):0.00} m to go"
        };
    }

    Stage HoldStage()
    {
        float held = 0f;
        Renderer zone = null;
        return new Stage
        {
            Title = "Hold over rooftop B",
            Instruction = $"Hover steadily above the survivor for {HoldSeconds:0} seconds.",
            OnEnter = () =>
            {
                held = 0f;
                zone = Spawn(ArenaBuilder.Zone(Arena, HoldCenter, HoldSize, PlutoTheme.Cyan.A(0.18f)));
                Hud.SetAltitudeTarget(HoldCenter.y);
            },
            OnExit = () => { if (zone != null) Destroy(zone.gameObject); Hud.SetAltitudeTarget(null); },
            IsComplete = () =>
            {
                bool inside = Drone.Fsm.IsAirborne && FlightHelpers.InBox(Drone.Center, HoldCenter, HoldSize);
                held = inside ? held + Time.deltaTime : 0f;
                zone.sharedMaterial = game.MakeMaterial(inside ? PlutoTheme.Mint.A(0.22f) : PlutoTheme.Cyan.A(0.18f), true);
                return held >= HoldSeconds;
            },
            Progress = () => !Drone.Fsm.IsAirborne ? FlightHelpers.Warn("Take off again: left stick up.")
                : held > 0f ? FlightHelpers.Good("Holding over the survivor. Stay steady.")
                : FlightHelpers.Warn(FlightHelpers.BoxHint(Drone, HoldCenter, HoldSize)),
            Meter = () => held / HoldSeconds,
            MeterLabel = () => $"{held:0.0} / {HoldSeconds:0.0} s"
        };
    }

    Stage ReturnStage()
    {
        GameObject beam = null;
        return new Stage
        {
            Title = "Return to pad A",
            Instruction = "Fly back to the home pad and land softly with the left stick or LAND.",
            OnEnter = () =>
            {
                lastTouchOnPad = false;
                beam = Spawn(Beam(Arena, Vector3.zero, 0.6f, PlutoTheme.Mint));
            },
            OnExit = () => { if (beam != null) Destroy(beam); },
            IsComplete = () => Drone.State == FlightState.Landed && lastTouchOnPad,
            Progress = () => Drone.Fsm.IsOnGroundArmed && !lastTouchOnPad
                ? FlightHelpers.Warn("Landed off the pad. Take off again: left stick up.")
                : FlightHelpers.OnPad(Drone.LocalPosition, Vector3.zero, PlutoGame.PadRadius)
                    ? FlightHelpers.Good("Over the pad. Descend slowly.")
                    : "Home pad: " + FlightHelpers.Direction(Drone, Vector3.zero, true)
        };
    }

    Stage DisarmStage() => new Stage
    {
        Title = "Disarm",
        Instruction = "Tap DISARM to stop the motors and finish the drill.",
        OnEnter = () => game.Controls.Highlight(game.Controls.ArmButton),
        OnExit = () => game.Controls.Highlight(null),
        IsComplete = () => Drone.State == FlightState.Disarmed,
        Progress = () => Drone.Fsm.IsAirborne ? FlightHelpers.Warn("Land first, then disarm.") : "Motors are still spinning. Tap DISARM."
    };
}
