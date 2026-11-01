using System;
using System.Collections.Generic;
using UnityEngine;

// The 7-stage guided tutorial. Every stage only completes when the pilot
// actually performs the action, and gives live corrective prompts meanwhile.
public class TutorialManager : GuidedMode
{
    static readonly Color ZoneIdle = PlutoTheme.Cyan.A(0.2f);
    static readonly Color ZoneInside = PlutoTheme.Mint.A(0.25f);

    protected override string Label => "TRAINING";

    public void Begin()
    {
        Stop();
        runner.Start(new List<Stage> { Familiarise(), Arming(), Takeoff(), Hover(), Directions(), Navigation(), LandAndDisarm() });
    }

    protected override void OnAllCompleted()
    {
        float t = runner.TotalTime;
        int count = runner.Count;
        Stop();
        PlayerPrefs.SetInt("tutorial_done", 1);
        PlayerPrefs.Save();
        var result = new ResultSpec
        {
            Eyebrow = "TRAINING",
            Title = "Training complete",
            Feedback = "You can now arm, take off, hover, move in every direction, navigate and land."
        }
            .Metric("Time", FlightHelpers.FormatTime(t))
            .Metric("Stages completed", $"{count} / {count}");
        game.ShowResult(result, game.StartTutorial, () => game.StartMission(0), "START " + MissionManager.ShortNames[0]);
    }

    static int Count(bool[] done)
    {
        int n = 0;
        foreach (var d in done) if (d) n++;
        return n;
    }

    // 1. Familiarisation: move each stick in each direction once.
    Stage Familiarise()
    {
        var done = new bool[8];
        string Arrow(int i, string glyph) => done[i] ? FlightHelpers.Good(glyph) : FlightHelpers.Colored(glyph, PlutoTheme.TextMuted);
        return new Stage
        {
            Title = "Meet the controller",
            Instruction = "Push each stick in all four directions. Pluto stays still until it is armed.",
            OnEnter = () =>
            {
                Array.Clear(done, 0, done.Length);
                Drone.SuppressDisarmedWarning = true;
                game.Controls.SetLabelsProminent(true);
            },
            OnExit = () =>
            {
                Drone.SuppressDisarmedWarning = false;
                game.Controls.SetLabelsProminent(false);
            },
            IsComplete = () =>
            {
                var i = game.Controls.Read();
                const float t = 0.6f;
                done[0] |= i.throttle > t; done[1] |= i.throttle < -t;
                done[2] |= i.yaw < -t; done[3] |= i.yaw > t;
                done[4] |= i.pitch > t; done[5] |= i.pitch < -t;
                done[6] |= i.roll < -t; done[7] |= i.roll > t;
                return Array.TrueForAll(done, d => d);
            },
            Progress = () => $"Left   {Arrow(0, "↑")} {Arrow(1, "↓")} {Arrow(2, "←")} {Arrow(3, "→")}"
                + $"      Right   {Arrow(4, "↑")} {Arrow(5, "↓")} {Arrow(6, "←")} {Arrow(7, "→")}",
            Meter = () => Count(done) / 8f,
            MeterLabel = () => $"{Count(done)} / 8 moves"
        };
    }

    // 2. Arming
    Stage Arming() => new Stage
    {
        Title = "Arm the motors",
        Instruction = "Tap ARM to start the propellers. Keep your hands clear of a real drone when you do this.",
        OnEnter = () => game.Controls.Highlight(game.Controls.ArmButton),
        OnExit = () => game.Controls.Highlight(null),
        IsComplete = () => Drone.Fsm.IsArmed,
        Progress = () => game.Controls.Read().Magnitude > 0.5f
            ? FlightHelpers.Warn("The sticks do nothing until Pluto is armed. Tap ARM.")
            : "Waiting for ARM."
    };

    // 3. Takeoff: reach 0.5 m (+/-0.1) and hold it for 2 s.
    Stage Takeoff()
    {
        const float target = 0.5f;
        const float hold = 2f;
        float held = 0f;
        LineRenderer ring = null;
        return new Stage
        {
            Title = "Take off",
            Instruction = "Push the left stick up, then hold Pluto level with the green ring at 0.5 m for 2 seconds.",
            OnEnter = () =>
            {
                held = 0f;
                ring = Spawn(ArenaBuilder.Circle(Arena, new Vector3(0f, target, 0f), Quaternion.identity, 0.15f, PlutoTheme.Mint, 0.01f, true));
                Hud.SetAltitudeTarget(target);
            },
            OnExit = () => { if (ring != null) Destroy(ring.gameObject); Hud.SetAltitudeTarget(null); },
            IsComplete = () =>
            {
                bool ok = Drone.Fsm.IsAirborne && Mathf.Abs(Drone.Altitude - target) <= 0.1f;
                held = ok ? held + Time.deltaTime : 0f;
                return held >= hold;
            },
            Progress = () =>
            {
                if (!Drone.Fsm.IsArmed) return FlightHelpers.Warn("Pluto was disarmed. Tap ARM again.");
                if (Drone.Altitude < target - 0.1f) return "Climb more: left stick up.";
                if (Drone.Altitude > target + 0.1f) return FlightHelpers.Warn("Too high. Ease the left stick down.");
                return FlightHelpers.Good("In the band. Hold steady.");
            },
            Meter = () => held / hold,
            MeterLabel = () => $"{held:0.0} / {hold:0.0} s"
        };
    }

    // 4. Hover: stay inside a box for 5 s.
    Stage Hover()
    {
        var center = new Vector3(0f, 0.5f, 0f);
        var size = new Vector3(0.4f, 0.3f, 0.4f);
        const float hold = 5f;
        float held = 0f;
        Renderer zone = null;
        return new Stage
        {
            Title = "Hover",
            Instruction = "Keep Pluto inside the glowing box for 5 seconds using tiny stick moves.",
            OnEnter = () =>
            {
                held = 0f;
                zone = Spawn(ArenaBuilder.Zone(Arena, center, size, ZoneIdle));
                Hud.SetAltitudeTarget(center.y);
            },
            OnExit = () => { if (zone != null) Destroy(zone.gameObject); Hud.SetAltitudeTarget(null); },
            IsComplete = () =>
            {
                bool inside = Drone.Fsm.IsAirborne && FlightHelpers.InBox(Drone.Center, center, size);
                held = inside ? held + Time.deltaTime : 0f;
                zone.sharedMaterial = game.MakeMaterial(inside ? ZoneInside : ZoneIdle, true);
                return held >= hold;
            },
            Progress = () =>
            {
                if (!Drone.Fsm.IsAirborne) return FlightHelpers.Warn("Take off first: ARM, then left stick up.");
                return held > 0f ? FlightHelpers.Good("Inside the box. Stay steady.") : FlightHelpers.Warn(FlightHelpers.BoxHint(Drone, center, size));
            },
            Meter = () => held / hold,
            MeterLabel = () => $"{held:0.0} / {hold:0.0} s"
        };
    }

    // 5. Directional moves: ~0.3 m forward, back, left and right.
    Stage Directions()
    {
        var names = new[] { "Forward", "Back", "Left", "Right" };
        var distance = new float[4];
        var done = new bool[4];
        const float goal = 0.3f;
        return new Stage
        {
            Title = "Move in every direction",
            Instruction = "Use the right stick to fly about 30 cm forward, back, left and right of where the nose points.",
            OnEnter = () => { Array.Clear(distance, 0, 4); Array.Clear(done, 0, 4); },
            IsComplete = () =>
            {
                if (!Drone.Fsm.IsAirborne) return false;
                var step = Drone.Velocity * Time.deltaTime;
                float f = Vector3.Dot(step, Drone.HeadingForward);
                float r = Vector3.Dot(step, Drone.HeadingRight);
                var i = Drone.Input;
                if (i.pitch > 0.3f && f > 0f) distance[0] += f;
                if (i.pitch < -0.3f && f < 0f) distance[1] -= f;
                if (i.roll < -0.3f && r < 0f) distance[2] -= r;
                if (i.roll > 0.3f && r > 0f) distance[3] += r;
                for (int k = 0; k < 4; k++) done[k] = distance[k] >= goal;
                return Array.TrueForAll(done, d => d);
            },
            Progress = () =>
            {
                var labels = new string[4];
                for (int k = 0; k < 4; k++) labels[k] = $"{names[k]} {Mathf.Min(distance[k], goal) * 100f:0}";
                string list = FlightHelpers.Checklist(labels, done, 4);
                if (!Drone.Fsm.IsAirborne) return FlightHelpers.Warn("Take off first.") + "\n" + list;
                if (Drone.Altitude < 0.15f) return FlightHelpers.Warn("Very low. Climb a little first.") + "\n" + list;
                return list;
            },
            Meter = () =>
            {
                float sum = 0f;
                for (int k = 0; k < 4; k++) sum += Mathf.Min(distance[k], goal);
                return sum / (4f * goal);
            },
            MeterLabel = () => $"{Count(done)} / 4 directions"
        };
    }

    // 6. Navigation: fly through 3 rings in order.
    Stage Navigation()
    {
        var course = new RingCourse();
        return new Stage
        {
            Title = "Navigate the rings",
            Instruction = "Fly through the 3 rings in order. The bright ring is next; yaw to face it, then fly forward.",
            OnEnter = () =>
            {
                course.Rings.Clear();
                course.Rings.Add(Spawn(Checkpoint.Create(Arena, new Vector3(0f, 0.5f, 0.45f), 0f, 0.16f, 1)));
                course.Rings.Add(Spawn(Checkpoint.Create(Arena, new Vector3(0.3f, 0.6f, 0.85f), 0f, 0.16f, 2)));
                course.Rings.Add(Spawn(Checkpoint.Create(Arena, new Vector3(0f, 0.5f, 1.25f), 0f, 0.16f, 3)));
                course.Reset(Drone.Center);
            },
            OnExit = () => { foreach (var r in course.Rings) if (r != null) Destroy(r.gameObject); course.Rings.Clear(); },
            IsComplete = () =>
            {
                if (course.Step(Drone.Center) && !course.Done)
                    Hud.Notify($"Ring {course.Next} cleared", NoticePriority.Objective, PlutoTheme.Mint, 1.2f);
                return course.Done;
            },
            Progress = () =>
            {
                if (!Drone.Fsm.IsAirborne) return FlightHelpers.Warn("Take off first.");
                var ring = course.Current;
                return ring == null ? "" : "Next ring: " + FlightHelpers.Direction(Drone, ring.transform.localPosition);
            },
            Meter = () => course.Next / (float)Mathf.Max(1, course.Rings.Count),
            MeterLabel = () => $"{course.Next} / {course.Rings.Count} rings"
        };
    }

    // 7. Land on the pad softly, then disarm.
    Stage LandAndDisarm()
    {
        bool touched = false, onPad = false, soft = false;
        Action<float, Vector3> onTouchdown = (speed, pos) =>
        {
            touched = true;
            onPad = FlightHelpers.OnPad(pos, Vector3.zero, PlutoGame.PadRadius);
            soft = speed > -Drone.hardLandingSpeed;
        };
        return new Stage
        {
            Title = "Land and disarm",
            Instruction = "Land softly on the home pad with the left stick or LAND, then tap DISARM.",
            OnEnter = () => { touched = onPad = soft = false; Drone.Touchdown += onTouchdown; },
            OnExit = () => { Drone.Touchdown -= onTouchdown; game.Controls.Highlight(null); },
            IsComplete = () => touched && onPad && soft && Drone.State == FlightState.Disarmed,
            Progress = () =>
            {
                if (Drone.Fsm.IsAirborne)
                {
                    game.Controls.Highlight(null);
                    return FlightHelpers.OnPad(Drone.LocalPosition, Vector3.zero, PlutoGame.PadRadius)
                        ? FlightHelpers.Good("Over the pad. Descend slowly.")
                        : "Home pad: " + FlightHelpers.Direction(Drone, Vector3.zero, true);
                }
                if (touched && !onPad) return FlightHelpers.Warn("Landed off the pad. Take off again and land on the home pad.");
                if (touched && !soft) return FlightHelpers.Warn("Too hard. Take off and come down more slowly.");
                if (touched && Drone.Fsm.IsOnGroundArmed)
                {
                    game.Controls.Highlight(game.Controls.ArmButton);
                    return FlightHelpers.Good("Perfect landing. Now tap DISARM.");
                }
                if (Drone.State == FlightState.Disarmed) return FlightHelpers.Warn("Tap ARM and take off, then land on the pad.");
                return "Take off, then land on the home pad.";
            }
        };
    }
}
