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

    const float TargetRadius = 0.1f;

    int current = -1;
    Vector3 targetPad;
    bool lastTouchOnTarget;
    bool hardLanding;
    float landingError = -1f;
    float penaltySeconds;
    readonly List<Bounds> obstacles = new List<Bounds>();

    protected override string Label => current >= 0 ? $"MISSION {current + 1}" : "MISSION";

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
        game.ShowResult("MISSION FAILED", reason + "\nTip: fly slower and use the drone's shadow to judge distance.", () => game.StartMission(m));
    }

    protected override void OnAllCompleted()
    {
        int m = current;
        float t = runner.TotalTime;
        int score;
        string details;
        switch (m)
        {
            case 0:
                score = 1000 - Mathf.RoundToInt(8f * t) - Mathf.RoundToInt(Mathf.Max(0f, landingError) * 2000f) - (hardLanding ? 200 : 0);
                details = $"Landing accuracy: {Mathf.Max(0f, landingError) * 100f:0.0} cm from centre";
                break;
            case 1:
                score = 1000 - Mathf.RoundToInt(5f * t) - Mathf.RoundToInt(60f * penaltySeconds) - (hardLanding ? 150 : 0);
                details = $"Time outside the altitude band: {penaltySeconds:0.0} s";
                break;
            default:
                score = 1500 - Mathf.RoundToInt(8f * t) - (hardLanding ? 200 : 0);
                details = "All 5 rings cleared, no pillars hit";
                break;
        }
        score = Mathf.Max(100, score);
        bool newBest = score > Best(m);
        if (newBest)
        {
            PlayerPrefs.SetInt("best_m" + m, score);
            PlayerPrefs.Save();
        }

        Stop();
        string body = $"Time: {FlightHelpers.FormatTime(t)}\n{details}\nHard landing: {(hardLanding ? "yes (-points)" : "no")}\n\n"
            + $"SCORE: {score}" + (newBest ? "   NEW BEST!" : $"   (best {Best(m)})");
        if (m < Names.Length - 1)
            game.ShowResult($"{Names[m]} - COMPLETE!", body, () => game.StartMission(m), () => game.StartMission(m + 1), $"Start Mission {m + 2}");
        else
            game.ShowResult($"{Names[m]} - COMPLETE!", body + "\nYou finished every mission!", () => game.StartMission(m));
    }

    // ---------- Mission 1 ----------
    List<Stage> Mission1()
    {
        targetPad = new Vector3(0.45f, 0f, 0.5f);
        Spawn(ArenaBuilder.Pad(Arena, targetPad, TargetRadius, new Color(1f, 0.55f, 0.1f), "TargetPad"));
        return new List<Stage>
        {
            TakeoffStage(0.4f),
            new Stage
            {
                Title = "Land on the orange pad",
                Instruction = "Fly over the orange pad with the RIGHT stick, then pull the LEFT stick DOWN gently (or press LAND).\nLand softly, as close to the centre as you can.",
                OnEnter = () => lastTouchOnTarget = false,
                IsComplete = () => Drone.State == FlightState.Landed && lastTouchOnTarget,
                Progress = () => Drone.Fsm.IsOnGroundArmed && !lastTouchOnTarget
                    ? FlightHelpers.Warn("You landed off the orange pad - take off again (LEFT stick UP).")
                    : "Orange pad is " + FlightHelpers.Direction(Drone, targetPad, true)
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
                Instruction = "ARM, take off and keep the drone inside the blue box for 5 seconds.",
                OnEnter = () =>
                {
                    held = 0f;
                    zone = Spawn(ArenaBuilder.Zone(Arena, boxCenter, boxSize, new Color(0.3f, 0.6f, 1f, 0.22f)));
                    Hud.SetAltitudeTarget(boxCenter.y);
                },
                OnExit = () => { if (zone != null) Destroy(zone.gameObject); },
                IsComplete = () =>
                {
                    bool inside = Drone.Fsm.IsAirborne && FlightHelpers.InBox(Drone.Center, boxCenter, boxSize);
                    held = inside ? held + Time.deltaTime : 0f;
                    zone.sharedMaterial = game.MakeMaterial(inside ? new Color(0.3f, 1f, 0.5f, 0.25f) : new Color(0.3f, 0.6f, 1f, 0.22f), true);
                    return held >= 5f;
                },
                Progress = () => !Drone.Fsm.IsAirborne ? "Press ARM, then LEFT stick UP."
                    : held > 0f ? FlightHelpers.Colored($"Steady... {held:0.0} / 5.0 s", HUDManager.Good)
                    : FlightHelpers.Warn(FlightHelpers.BoxHint(Drone, boxCenter, boxSize))
            },
            new Stage
            {
                Title = "Visit the 4 waypoints",
                Instruction = "Fly to each glowing ball in order. Keep your height between 0.35 m and 0.65 m - time outside that band costs points.",
                OnEnter = () =>
                {
                    next = 0;
                    markers.Clear();
                    for (int i = 0; i < points.Length; i++)
                        markers.Add(Spawn(ArenaBuilder.Marker(Arena, points[i], 0.05f, i == 0 ? HUDManager.Warn : Color.white)));
                },
                IsComplete = () =>
                {
                    if (Drone.Fsm.IsAirborne && (Drone.Altitude < 0.35f || Drone.Altitude > 0.65f))
                    {
                        penaltySeconds += Time.deltaTime;
                        Drone.Warn("Hold your altitude: stay between 0.35 and 0.65 m!");
                    }
                    if (next < points.Length && Vector3.Distance(Drone.Center, points[next]) < 0.1f)
                    {
                        ArenaBuilder.SetColor(markers[next], new Color(0.3f, 1f, 0.5f, 0.5f), true);
                        next++;
                        if (next < points.Length)
                        {
                            ArenaBuilder.SetColor(markers[next], new Color(1f, 0.78f, 0.2f, 0.6f), true);
                            Hud.Toast($"Waypoint {next} reached!", HUDManager.Good, 1.2f);
                        }
                    }
                    return next >= points.Length;
                },
                Progress = () => next >= points.Length ? "" :
                    $"Waypoint {next + 1}/4 ({pointNames[next]}) is " + FlightHelpers.Direction(Drone, points[next])
                    + $"\nAltitude penalty: {penaltySeconds:0.0} s"
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
                Instruction = "Fly through the rings in order (flashing yellow = next). Do NOT touch the red pillars - that is a crash!",
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
                    if (course.Step(Drone.Center) && !course.Done) Hud.Toast($"Ring {course.Next} / {rings.Length}!", HUDManager.Good, 1.2f);
                    return course.Done;
                },
                Progress = () => course.Current == null ? ""
                    : $"Ring {course.Next + 1}/{rings.Length} is " + FlightHelpers.Direction(Drone, course.Current.transform.localPosition)
            },
            PadLandingStage(),
            DisarmStage()
        };
    }

    // ---------- shared stages ----------
    Stage TakeoffStage(float height) => new Stage
    {
        Title = "Take off",
        Instruction = $"Press ARM, then push the LEFT stick UP (or press TAKE OFF) and climb to at least {height:0.0} m.",
        OnEnter = () => Hud.SetAltitudeTarget(height),
        OnExit = () => Hud.SetAltitudeTarget(null),
        IsComplete = () => Drone.Fsm.IsAirborne && Drone.Altitude >= height,
        Progress = () => Drone.State == FlightState.Disarmed ? "Press ARM first." : $"Altitude {Drone.Altitude:0.00} / {height:0.00} m"
    };

    Stage PadLandingStage() => new Stage
    {
        Title = "Land on the blue pad",
        Instruction = "Fly back over the blue home pad and land softly (LEFT stick DOWN or LAND).",
        OnEnter = () => { lastTouchOnTarget = false; Hud.SetAltitudeTarget(null); },
        IsComplete = () => Drone.State == FlightState.Landed && lastTouchOnTarget,
        Progress = () => Drone.Fsm.IsOnGroundArmed && !lastTouchOnTarget
            ? FlightHelpers.Warn("You landed off the pad - take off again (LEFT stick UP).")
            : "Home pad is " + FlightHelpers.Direction(Drone, targetPad, true)
    };

    Stage DisarmStage() => new Stage
    {
        Title = "Disarm",
        Instruction = "Press DISARM to stop the motors.",
        OnEnter = () => game.Controls.Highlight(game.Controls.ArmButton),
        OnExit = () => game.Controls.Highlight(null),
        IsComplete = () => Drone.State == FlightState.Disarmed,
        Progress = () => Drone.Fsm.IsAirborne ? FlightHelpers.Warn("Land first, then disarm.") : "Motors are still spinning - press DISARM."
    };
}
