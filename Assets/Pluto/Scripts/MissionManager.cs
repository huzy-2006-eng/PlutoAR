using System.Collections.Generic;
using UnityEngine;

// Three scored missions:
//   1. Takeoff & Landing          - take off, fly to the orange pad, land softly, disarm
//   2. Hover & Directional Control - hold a hover, visit 4 waypoints while holding altitude
//   3. AR Navigation Challenge     - 5 rings in order around red pillars, then land
public class MissionManager : GuidedMode
{
    public static readonly string[] Names =
    {
        "Mission 1: Takeoff & Landing",
        "Mission 2: Hover & Directional Control",
        "Mission 3: AR Navigation Challenge"
    };

    public static readonly string[] ShortNames = { "FIRST FLIGHT", "CONTROL LAB", "CHECKPOINT RUN" };

    public static readonly string[] Descriptions =
    {
        "Take off, fly to the orange pad and land softly.",
        "Hold a steady hover, then visit four waypoints at a fixed height.",
        "Fly through five rings around the pillars and return to the pad."
    };

    static readonly Color ZoneIdle = PlutoTheme.Cyan.A(0.2f);
    static readonly Color ZoneInside = PlutoTheme.Mint.A(0.25f);

    const float TargetRadius = 0.1f;

    int current = -1;
    Vector3 targetPad;
    bool lastTouchOnTarget;
    bool hardLanding;
    float landingError = -1f;
    float penaltySeconds;
    readonly List<Bounds> obstacles = new List<Bounds>();

    protected override string Label => current >= 0 ? Eyebrow(current) : "MISSION";

    public static string Eyebrow(int index) => $"MISSION {index + 1:00}  ·  {ShortNames[index]}";

    public static int Best(int index) => PlayerPrefs.GetInt("best_m" + index, 0);

    public void Begin(int index)
    {
        Stop();
        current = index;
        lastTouchOnTarget = false;
        hardLanding = false;
        landingError = -1f;
        penaltySeconds = 0f;
        Drone.Touchdown += OnTouchdown;
        Drone.Crashed += OnCrashed;
        runner.Start(index == 0 ? Mission1() : index == 1 ? Mission2() : Mission3());
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
        if (!runner.Running || !Drone.Fsm.IsAirborne || Drone.State == FlightState.Crashed) return;
        foreach (var b in obstacles)
        {
            var hit = b;
            hit.Expand(new Vector3(0.1f, 0.03f, 0.1f)); // roughly the drone's half-width
            if (hit.Contains(Drone.Center))
            {
                Drone.Crash("You hit a red pillar!");
                return;
            }
        }
    }

    void OnTouchdown(float speed, Vector3 pos)
    {
        lastTouchOnTarget = FlightHelpers.OnPad(pos, targetPad, TargetRadius);
        if (lastTouchOnTarget)
            landingError = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(targetPad.x, targetPad.z));
        if (speed < -Drone.hardLandingSpeed) hardLanding = true;
    }

    void OnCrashed(string reason)
    {
        int m = current;
        Stop();
        var result = new ResultSpec
        {
            Eyebrow = Eyebrow(m),
            Title = "Mission failed",
            Success = false,
            Feedback = reason + " Fly slower and use Pluto's shadow to judge distance."
        };
        game.ShowResult(result, () => game.StartMission(m));
    }

    protected override void OnAllCompleted()
    {
        int m = current;
        float t = runner.TotalTime;
        int score;
        var result = new ResultSpec { Eyebrow = Eyebrow(m) };
        result.Metric("Time", FlightHelpers.FormatTime(t));
        switch (m)
        {
            case 0:
                score = 1000 - Mathf.RoundToInt(8f * t) - Mathf.RoundToInt(Mathf.Max(0f, landingError) * 2000f) - (hardLanding ? 200 : 0);
                result.Metric("Landing accuracy", $"{Mathf.Max(0f, landingError) * 100f:0.0} cm from centre");
                result.Feedback = hardLanding ? "Ease the left stick down more slowly for a softer touchdown."
                    : landingError > 0.05f ? "Line up over the pad centre before you descend."
                    : "Clean, accurate landing. Now try for a faster time.";
                break;
            case 1:
                score = 1000 - Mathf.RoundToInt(5f * t) - Mathf.RoundToInt(60f * penaltySeconds) - (hardLanding ? 150 : 0);
                result.Metric("Outside altitude band", $"{penaltySeconds:0.0} s");
                result.Feedback = hardLanding ? "Ease the left stick down more slowly for a softer touchdown."
                    : penaltySeconds > 0f ? "Watch the altitude bar to stay between 0.35 m and 0.65 m."
                    : "You held the altitude band the whole way. Now try for a faster time.";
                break;
            default:
                score = 1500 - Mathf.RoundToInt(8f * t) - (hardLanding ? 200 : 0);
                result.Metric("Rings cleared", "5 / 5");
                result.Metric("Pillars hit", "0");
                result.Feedback = hardLanding ? "Ease the left stick down more slowly for a softer touchdown."
                    : "Smooth run. Yaw to face each ring earlier to save time.";
                break;
        }
        result.Metric("Hard landing", hardLanding ? "Yes" : "No");
        score = Mathf.Max(100, score);
        int previousBest = Best(m);
        bool newBest = score > previousBest;
        if (newBest)
        {
            PlayerPrefs.SetInt("best_m" + m, score);
            PlayerPrefs.Save();
        }
        result.Score = score;
        result.Title = newBest ? "New best score" : "Mission complete";
        if (!newBest) result.Metric("Best score", previousBest.ToString());

        Stop();
        if (m < Names.Length - 1)
            game.ShowResult(result, () => game.StartMission(m), () => game.StartMission(m + 1), "NEXT: " + ShortNames[m + 1]);
        else
            game.ShowResult(result, () => game.StartMission(m));
    }

    // ---------- Mission 1 ----------
    List<Stage> Mission1()
    {
        targetPad = new Vector3(0.45f, 0f, 0.5f);
        Spawn(ArenaBuilder.Pad(Arena, targetPad, TargetRadius, new Color(1f, 0.6f, 0.2f), "TargetPad"));
        return new List<Stage>
        {
            TakeoffStage(0.4f),
            new Stage
            {
                Title = "Land on the orange pad",
                Instruction = "Fly over the orange pad, then land softly as close to its centre as you can.",
                OnEnter = () => lastTouchOnTarget = false,
                IsComplete = () => Drone.State == FlightState.Landed && lastTouchOnTarget,
                Progress = () => Drone.Fsm.IsOnGroundArmed && !lastTouchOnTarget
                    ? FlightHelpers.Warn("Landed off the orange pad. Take off again: left stick up.")
                    : "Orange pad: " + FlightHelpers.Direction(Drone, targetPad, true)
            },
            DisarmStage()
        };
    }

    // ---------- Mission 2 ----------
    List<Stage> Mission2()
    {
        targetPad = Vector3.zero;
        var boxCenter = new Vector3(0f, 0.5f, 0f);
        var boxSize = new Vector3(0.4f, 0.3f, 0.4f);
        float held = 0f;
        Renderer zone = null;

        var points = new[] { new Vector3(0f, 0.5f, 0.4f), new Vector3(0.4f, 0.5f, 0f), new Vector3(0f, 0.5f, -0.4f), new Vector3(-0.4f, 0.5f, 0f) };
        var pointNames = new[] { "front", "right", "back (near you)", "left" };
        var markers = new List<GameObject>();
        int next = 0;

        return new List<Stage>
        {
            new Stage
            {
                Title = "Hold a steady hover",
                Instruction = "Arm, take off and keep Pluto inside the glowing box for 5 seconds.",
                OnEnter = () =>
                {
                    held = 0f;
                    zone = Spawn(ArenaBuilder.Zone(Arena, boxCenter, boxSize, ZoneIdle));
                    Hud.SetAltitudeTarget(boxCenter.y);
                },
                OnExit = () => { if (zone != null) Destroy(zone.gameObject); },
                IsComplete = () =>
                {
                    bool inside = Drone.Fsm.IsAirborne && FlightHelpers.InBox(Drone.Center, boxCenter, boxSize);
                    held = inside ? held + Time.deltaTime : 0f;
                    zone.sharedMaterial = game.MakeMaterial(inside ? ZoneInside : ZoneIdle, true);
                    return held >= 5f;
                },
                Progress = () => !Drone.Fsm.IsAirborne ? "Tap ARM, then push the left stick up."
                    : held > 0f ? FlightHelpers.Good("Inside the box. Stay steady.")
                    : FlightHelpers.Warn(FlightHelpers.BoxHint(Drone, boxCenter, boxSize)),
                Meter = () => held / 5f,
                MeterLabel = () => $"{held:0.0} / 5.0 s"
            },
            new Stage
            {
                Title = "Visit the 4 waypoints",
                Instruction = "Visit each waypoint in order while staying between 0.35 m and 0.65 m high.",
                OnEnter = () =>
                {
                    next = 0;
                    markers.Clear();
                    for (int i = 0; i < points.Length; i++)
                        markers.Add(Spawn(ArenaBuilder.Marker(Arena, points[i], 0.05f, i == 0 ? PlutoTheme.Cyan : Color.white)));
                },
                IsComplete = () =>
                {
                    if (Drone.Fsm.IsAirborne && (Drone.Altitude < 0.35f || Drone.Altitude > 0.65f))
                    {
                        penaltySeconds += Time.deltaTime;
                        Drone.Warn("Hold your altitude: stay between 0.35 m and 0.65 m.");
                    }
                    if (next < points.Length && Vector3.Distance(Drone.Center, points[next]) < 0.1f)
                    {
                        ArenaBuilder.SetColor(markers[next], PlutoTheme.Mint.A(0.5f), true);
                        next++;
                        if (next < points.Length)
                        {
                            ArenaBuilder.SetColor(markers[next], PlutoTheme.Cyan.A(0.6f), true);
                            Hud.Notify($"Waypoint {next} reached", NoticePriority.Objective, PlutoTheme.Mint, 1.2f);
                        }
                    }
                    return next >= points.Length;
                },
                Progress = () => next >= points.Length ? "" :
                    $"Waypoint {next + 1} ({pointNames[next]}): " + FlightHelpers.Direction(Drone, points[next])
                    + $"\nOutside altitude band: {penaltySeconds:0.0} s",
                Meter = () => next / (float)points.Length,
                MeterLabel = () => $"{next} / {points.Length} waypoints"
            },
            PadLandingStage(),
            DisarmStage()
        };
    }

    // ---------- Mission 3 ----------
    List<Stage> Mission3()
    {
        targetPad = Vector3.zero;
        var course = new RingCourse();
        var rings = new[]
        {
            new Vector3(0f, 0.35f, 0.4f), new Vector3(0.35f, 0.55f, 0.8f), new Vector3(0f, 0.75f, 1.2f),
            new Vector3(-0.45f, 0.55f, 0.8f), new Vector3(-0.45f, 0.35f, 0.3f)
        };
        var pillars = new[] { new Vector3(-0.2f, 0f, 0.5f), new Vector3(0f, 0f, 0.8f), new Vector3(0.3f, 0f, 1.25f) };

        return new List<Stage>
        {
            TakeoffStage(0.3f),
            new Stage
            {
                Title = "Fly through all 5 rings",
                Instruction = "Fly through the 5 rings in order and keep clear of the red pillars.",
                OnEnter = () =>
                {
                    obstacles.Clear();
                    foreach (var p in pillars)
                    {
                        Spawn(ArenaBuilder.Obstacle(Arena, p, new Vector3(0.07f, 1f, 0.07f), out var bounds));
                        obstacles.Add(bounds);
                    }
                    course.Rings.Clear();
                    for (int i = 0; i < rings.Length; i++)
                        course.Rings.Add(Spawn(Checkpoint.Create(Arena, rings[i], 0f, 0.14f, i + 1)));
                    course.Reset(Drone.Center);
                },
                IsComplete = () =>
                {
                    if (course.Step(Drone.Center) && !course.Done)
                        Hud.Notify($"Ring {course.Next} / {rings.Length} cleared", NoticePriority.Objective, PlutoTheme.Mint, 1.2f);
                    return course.Done;
                },
                Progress = () => course.Current == null ? ""
                    : "Next ring: " + FlightHelpers.Direction(Drone, course.Current.transform.localPosition),
                Meter = () => course.Next / (float)rings.Length,
                MeterLabel = () => $"{course.Next} / {rings.Length} rings"
            },
            PadLandingStage(),
            DisarmStage()
        };
    }

    // ---------- shared stages ----------
    Stage TakeoffStage(float height) => new Stage
    {
        Title = "Take off",
        Instruction = $"Tap ARM, then TAKE OFF or push the left stick up to climb above {height:0.0} m.",
        OnEnter = () => Hud.SetAltitudeTarget(height),
        OnExit = () => Hud.SetAltitudeTarget(null),
        IsComplete = () => Drone.Fsm.IsAirborne && Drone.Altitude >= height,
        Progress = () => Drone.State == FlightState.Disarmed ? "Tap ARM first." : "Climb: left stick up or TAKE OFF.",
        Meter = () => Drone.Altitude / height,
        MeterLabel = () => $"{Drone.Altitude:0.00} / {height:0.00} m"
    };

    Stage PadLandingStage() => new Stage
    {
        Title = "Land on the blue pad",
        Instruction = "Return to the home pad and land softly with the left stick or LAND.",
        OnEnter = () => { lastTouchOnTarget = false; Hud.SetAltitudeTarget(null); },
        IsComplete = () => Drone.State == FlightState.Landed && lastTouchOnTarget,
        Progress = () => Drone.Fsm.IsOnGroundArmed && !lastTouchOnTarget
            ? FlightHelpers.Warn("Landed off the pad. Take off again: left stick up.")
            : "Home pad: " + FlightHelpers.Direction(Drone, targetPad, true)
    };

    Stage DisarmStage() => new Stage
    {
        Title = "Disarm",
        Instruction = "Tap DISARM to stop the motors.",
        OnEnter = () => game.Controls.Highlight(game.Controls.ArmButton),
        OnExit = () => game.Controls.Highlight(null),
        IsComplete = () => Drone.State == FlightState.Disarmed,
        Progress = () => Drone.Fsm.IsAirborne ? FlightHelpers.Warn("Land first, then disarm.") : "Motors are still spinning. Tap DISARM."
    };
}
