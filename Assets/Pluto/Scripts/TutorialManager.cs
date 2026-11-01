using System;
using System.Collections.Generic;
using UnityEngine;

// The 7-stage guided tutorial. Every stage only completes when the pilot
// actually performs the action, and gives live corrective prompts meanwhile.
public class TutorialManager : GuidedMode
{
    protected override string Label => "TUTORIAL";

    public void Begin()
    {
        Stop();
        runner.Start(new List<Stage> { Familiarise(), Arming(), Takeoff(), Hover(), Directions(), Navigation(), LandAndDisarm() });
    }

    protected override void OnAllCompleted()
    {
        float t = runner.TotalTime;
        Stop();
        PlayerPrefs.SetInt("tutorial_done", 1);
        PlayerPrefs.Save();
        game.ShowResult("TUTORIAL COMPLETE!",
            $"You finished all 7 stages in {FlightHelpers.FormatTime(t)}.\nYou can now arm, take off, hover, move in every direction, navigate and land.\nReady for the missions?",
            game.StartTutorial, () => game.StartMission(0), "Start Mission 1");
    }

    // 1. Familiarisation: move each stick in each direction once.
    Stage Familiarise()
    {
        var names = new[] { "Left UP (throttle +)", "Left DOWN (throttle -)", "Left LEFT (yaw left)", "Left RIGHT (yaw right)",
                            "Right UP (pitch fwd)", "Right DOWN (pitch back)", "Right LEFT (roll left)", "Right RIGHT (roll right)" };
        var done = new bool[8];
        return new Stage
        {
            Title = "Meet the controller",
            Instruction = "LEFT stick: up/down = THROTTLE (height), left/right = YAW (turn on the spot).\n"
                + "RIGHT stick: up/down = PITCH (forward/back), left/right = ROLL (slide sideways).\n"
                + "Push each stick in all 4 directions. The drone is disarmed, so it will not move yet.",
            OnEnter = () => { Array.Clear(done, 0, done.Length); Drone.SuppressDisarmedWarning = true; },
            OnExit = () => Drone.SuppressDisarmedWarning = false,
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
            Progress = () => FlightHelpers.Checklist(names, done)
        };
    }

    // 2. Arming
    Stage Arming() => new Stage
    {
        Title = "Arm the motors",
        Instruction = "Press ARM. The propellers start spinning but the drone stays on the ground.\nSafety rule: only arm on a flat surface with your hands clear of the propellers.",
        OnEnter = () => game.Controls.Highlight(game.Controls.ArmButton),
        OnExit = () => game.Controls.Highlight(null),
        IsComplete = () => Drone.Fsm.IsArmed,
        Progress = () => game.Controls.Read().Magnitude > 0.5f
            ? FlightHelpers.Warn("The sticks do nothing until the drone is armed - press ARM.")
            : "Waiting for ARM..."
    };

    // 3. Takeoff: reach 0.5 m (+/-0.1) and hold it for 2 s.
    Stage Takeoff()
    {
        const float target = 0.5f;
        float held = 0f;
        LineRenderer ring = null;
        return new Stage
        {
            Title = "Take off",
            Instruction = "Gently push the LEFT stick UP to climb. Reach the green ring at 0.5 m and hold the height for 2 seconds.\n(Let the stick go back to the centre to hover.)",
            OnEnter = () =>
            {
                held = 0f;
                ring = Spawn(ArenaBuilder.Circle(Arena, new Vector3(0f, target, 0f), Quaternion.identity, 0.15f, HUDManager.Good, 0.01f, true));
                Hud.SetAltitudeTarget(target);
            },
            OnExit = () => { if (ring != null) Destroy(ring.gameObject); Hud.SetAltitudeTarget(null); },
            IsComplete = () =>
            {
                bool ok = Drone.Fsm.IsAirborne && Mathf.Abs(Drone.Altitude - target) <= 0.1f;
                held = ok ? held + Time.deltaTime : 0f;
                return held >= 2f;
            },
            Progress = () =>
            {
                if (!Drone.Fsm.IsArmed) return FlightHelpers.Warn("The drone was disarmed - press ARM again.");
                if (Drone.Altitude < target - 0.1f) return $"Altitude {Drone.Altitude:0.00} m - climb more: LEFT stick UP.";
                if (Drone.Altitude > target + 0.1f) return FlightHelpers.Warn($"Altitude {Drone.Altitude:0.00} m - too high: LEFT stick DOWN a little.");
                return FlightHelpers.Colored($"Holding... {held:0.0} / 2.0 s", HUDManager.Good);
            }
        };
    }

    // 4. Hover: stay inside a box for 5 s.
    Stage Hover()
    {
        var center = new Vector3(0f, 0.5f, 0f);
        var size = new Vector3(0.4f, 0.3f, 0.4f);
        float held = 0f;
        Renderer zone = null;
        return new Stage
        {
            Title = "Hover",
            Instruction = "Keep the drone inside the blue box for 5 seconds.\nUse tiny stick movements - a real drone drifts, so keep correcting.",
            OnEnter = () =>
            {
                held = 0f;
                zone = Spawn(ArenaBuilder.Zone(Arena, center, size, new Color(0.3f, 0.6f, 1f, 0.22f)));
                Hud.SetAltitudeTarget(center.y);
            },
            OnExit = () => { if (zone != null) Destroy(zone.gameObject); Hud.SetAltitudeTarget(null); },
            IsComplete = () =>
            {
                bool inside = Drone.Fsm.IsAirborne && FlightHelpers.InBox(Drone.Center, center, size);
                held = inside ? held + Time.deltaTime : 0f;
                zone.sharedMaterial = game.MakeMaterial(inside ? new Color(0.3f, 1f, 0.5f, 0.25f) : new Color(0.3f, 0.6f, 1f, 0.22f), true);
                return held >= 5f;
            },
            Progress = () =>
            {
                if (!Drone.Fsm.IsAirborne) return FlightHelpers.Warn("Take off first: ARM, then LEFT stick UP.");
                return held > 0f
                    ? FlightHelpers.Colored($"Steady... {held:0.0} / 5.0 s", HUDManager.Good)
                    : FlightHelpers.Warn(FlightHelpers.BoxHint(Drone, center, size));
            }
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
            Instruction = "With the RIGHT stick, fly about 30 cm FORWARD, BACK, LEFT and RIGHT.\nForward/back is PITCH, left/right is ROLL - both relative to where the drone's nose (red LEDs) points.",
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
                for (int k = 0; k < 4; k++) labels[k] = $"{names[k]} {Mathf.Min(distance[k], goal) * 100f:0}/{goal * 100f:0} cm";
                string list = FlightHelpers.Checklist(labels, done, 4);
                if (!Drone.Fsm.IsAirborne) return FlightHelpers.Warn("Take off first.") + "\n" + list;
                if (Drone.Altitude < 0.15f) return FlightHelpers.Warn("Very low - climb a little so you don't clip the table.") + "\n" + list;
                return list;
            }
        };
    }

    // 6. Navigation: fly through 3 rings in order.
    Stage Navigation()
    {
        var course = new RingCourse();
        return new Stage
        {
            Title = "Navigate the rings",
            Instruction = "Fly through the 3 rings in order. The flashing yellow ring is the next one.\nTip: use YAW (left stick left/right) to point the nose at the ring, then PITCH forward.",
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
                if (course.Step(Drone.Center) && !course.Done) Hud.Toast($"Ring {course.Next} passed!", HUDManager.Good, 1.2f);
                return course.Done;
            },
            Progress = () =>
            {
                if (!Drone.Fsm.IsAirborne) return FlightHelpers.Warn("Take off first.");
                var ring = course.Current;
                return ring == null ? "" : $"Ring {course.Next + 1}/3 is " + FlightHelpers.Direction(Drone, ring.transform.localPosition);
            }
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
            Instruction = "Fly back over the blue landing pad. Pull the LEFT stick DOWN gently (or press LAND) to touch down softly on it.\nThen press DISARM to stop the motors.",
            OnEnter = () => { touched = onPad = soft = false; Drone.Touchdown += onTouchdown; },
            OnExit = () => { Drone.Touchdown -= onTouchdown; game.Controls.Highlight(null); },
            IsComplete = () => touched && onPad && soft && Drone.State == FlightState.Disarmed,
            Progress = () =>
            {
                if (Drone.Fsm.IsAirborne)
                {
                    game.Controls.Highlight(null);
                    return "Landing pad is " + FlightHelpers.Direction(Drone, Vector3.zero, true)
                        + (FlightHelpers.OnPad(Drone.LocalPosition, Vector3.zero, PlutoGame.PadRadius) ? "\n" + FlightHelpers.Colored("Over the pad - now descend slowly.", HUDManager.Good) : "");
                }
                if (touched && !onPad) return FlightHelpers.Warn("You landed off the pad. Take off again (LEFT stick UP) and land on the blue pad.");
                if (touched && !soft) return FlightHelpers.Warn("That landing was too hard. Take off and come down more slowly.");
                if (touched && Drone.Fsm.IsOnGroundArmed)
                {
                    game.Controls.Highlight(game.Controls.ArmButton);
                    return FlightHelpers.Colored("Perfect landing! Now press DISARM.", HUDManager.Good);
                }
                if (Drone.State == FlightState.Disarmed) return FlightHelpers.Warn("Press ARM and take off, then land on the pad.");
                return "Take off, then land on the pad.";
            }
        };
    }
}
