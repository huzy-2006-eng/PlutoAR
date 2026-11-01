using System;
using System.Collections.Generic;
using UnityEngine;

// Simplified, consistent flight model. All positions are local to the Arena
// (the anchored spot the user tapped), in metres. y = height above the surface.
//
//   centred throttle = hold altitude (like Pluto's altitude-hold mode)
//   velocity eases toward the stick target every frame = gentle auto-stabilise
public class DroneController : MonoBehaviour
{
    [Header("Flight feel (metres / seconds)")]
    public float maxHorizontalSpeed = 0.6f;
    public float maxClimbSpeed = 0.5f;
    public float yawRateDeg = 100f;
    public float responsiveness = 4f;
    public float maxAltitude = 1.6f;
    public float maxRange = 2.5f;

    [Header("Automatic take-off / landing")]
    public float autoTakeoffAltitude = 0.4f;
    public float takeoffClimbSpeed = 0.3f;
    public float landingSpeed = 0.25f;
    public float hardLandingSpeed = 0.6f;

    [Header("Difficulty")]
    public float windStrength;

    public const float CenterHeight = 0.015f;

    public FlightStateMachine Fsm { get; } = new FlightStateMachine();
    public FlightState State => Fsm.State;
    public FlightInput Input { get; private set; }
    public ControllerUI Controls { get; set; }
    public Vector3 Velocity => velocity;
    public float Yaw { get; private set; }
    public float Altitude => transform.localPosition.y;
    public Vector3 LocalPosition => transform.localPosition;
    public Vector3 Center => transform.localPosition + Vector3.up * CenterHeight;
    public Vector3 HeadingForward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
    public Vector3 HeadingRight => Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
    public bool SuppressDisarmedWarning { get; set; }
    public bool InputLocked { get; set; }

    public event Action<string> Warning;
    public event Action<float, Vector3> Touchdown; // vertical speed at impact (negative), local position
    public event Action<string> Crashed;

    Vector3 velocity;
    bool autoTakeoff;
    readonly Dictionary<string, float> lastWarning = new Dictionary<string, float>();

    public void ResetTo(Vector3 localPosition, float yaw)
    {
        Fsm.ForceReset();
        velocity = Vector3.zero;
        Yaw = yaw;
        transform.localPosition = new Vector3(localPosition.x, 0f, localPosition.z);
        transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public void ToggleArm()
    {
        if (State == FlightState.Disarmed) Fsm.TryTransition(FlightState.Armed);
        else Disarm();
    }

    public void Disarm()
    {
        if (Fsm.IsOnGroundArmed) Fsm.TryTransition(FlightState.Disarmed);
        else if (Fsm.IsAirborne) Warn("Land first. Disarming in the air would make a real drone fall.");
    }

    public void Takeoff()
    {
        if (State == FlightState.Disarmed) { Warn("Arm Pluto first: tap ARM."); return; }
        if (!Fsm.IsOnGroundArmed) return;
        autoTakeoff = true;
        Fsm.TryTransition(FlightState.TakingOff);
    }

    public void Land()
    {
        if (State == FlightState.Flying || State == FlightState.TakingOff) Fsm.TryTransition(FlightState.Landing);
        else if (!Fsm.IsAirborne) Warn("Already on the ground.");
    }

    public void Crash(string reason)
    {
        if (!Fsm.IsAirborne || State == FlightState.Crashed) return;
        Fsm.TryTransition(FlightState.Crashed);
        Crashed?.Invoke(reason);
    }

    public void Warn(string message)
    {
        if (lastWarning.TryGetValue(message, out var t) && Time.time - t < 2.5f) return;
        lastWarning[message] = Time.time;
        Warning?.Invoke(message);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Fsm.Tick(dt);
        Input = Controls != null && !InputLocked ? Controls.Read() : default;

        var p = transform.localPosition;
        var target = Vector3.zero;

        switch (State)
        {
            case FlightState.Disarmed:
                velocity = Vector3.zero;
                if (Input.Magnitude > 0.5f && !SuppressDisarmedWarning && !InputLocked)
                    Warn("Pluto is disarmed. Tap ARM to start the motors.");
                break;

            case FlightState.Armed:
            case FlightState.Landed:
                velocity = Vector3.zero;
                if (Input.throttle > 0.3f)
                {
                    autoTakeoff = false;
                    Fsm.TryTransition(FlightState.TakingOff);
                }
                break;

            case FlightState.TakingOff:
                Yaw += Input.yaw * yawRateDeg * dt;
                if (autoTakeoff)
                {
                    target = Horizontal(0.5f) + Vector3.up * takeoffClimbSpeed;
                    if (p.y >= autoTakeoffAltitude) Fsm.TryTransition(FlightState.Flying);
                }
                else
                {
                    target = Horizontal(1f) + Vector3.up * Mathf.Max(0f, Input.throttle) * maxClimbSpeed;
                    if (p.y > 0.05f) Fsm.TryTransition(FlightState.Flying);
                    else if (Input.throttle < 0.1f && p.y < 0.01f) Fsm.TryTransition(FlightState.Armed);
                }
                break;

            case FlightState.Flying:
                Yaw += Input.yaw * yawRateDeg * dt;
                target = Horizontal(1f) + Vector3.up * Input.throttle * maxClimbSpeed;
                break;

            case FlightState.Landing:
                if (Input.throttle > 0.5f) { Fsm.TryTransition(FlightState.Flying); break; }
                Yaw += Input.yaw * yawRateDeg * 0.5f * dt;
                target = Horizontal(0.4f) + Vector3.down * landingSpeed;
                break;

            case FlightState.Crashed:
                velocity.x *= 0.98f;
                velocity.z *= 0.98f;
                velocity.y -= 9.81f * dt;
                break;
        }

        if (Fsm.IsAirborne && State != FlightState.Crashed)
        {
            velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-responsiveness * dt));
            if (windStrength > 0f)
            {
                float t = Time.time * 0.25f;
                p += new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, 0f, Mathf.PerlinNoise(0f, t) - 0.5f) * (2f * windStrength * dt);
            }
        }

        p += velocity * dt;

        // Ground: the drone can never go below the surface.
        if (p.y <= 0f)
        {
            p.y = 0f;
            if (State == FlightState.Crashed)
            {
                velocity = Vector3.zero;
                if (Fsm.TimeInState > 1.5f) Fsm.TryTransition(FlightState.Disarmed);
            }
            else if ((State == FlightState.Flying || State == FlightState.Landing) && velocity.y < 0f)
            {
                float impact = velocity.y;
                velocity = Vector3.zero;
                Fsm.TryTransition(FlightState.Landed);
                if (impact < -hardLandingSpeed) Warn("Hard landing. Ease the left stick down more gently.");
                Touchdown?.Invoke(impact, p);
            }
            else if (velocity.y < 0f)
            {
                velocity.y = 0f;
            }
        }

        if (p.y > maxAltitude)
        {
            p.y = maxAltitude;
            velocity.y = Mathf.Min(0f, velocity.y);
            Warn("Maximum training altitude reached.");
        }

        var flat = new Vector2(p.x, p.z);
        if (flat.magnitude > maxRange)
        {
            flat = flat.normalized * maxRange;
            p.x = flat.x;
            p.z = flat.y;
            Warn("Too far from the home pad. Fly back.");
        }

        transform.localPosition = p;
        transform.localRotation = Quaternion.Euler(0f, Yaw, 0f);
    }

    // Pitch/roll are relative to where the drone's nose points, like a real quad.
    Vector3 Horizontal(float scale)
    {
        var dir = HeadingForward * Input.pitch + HeadingRight * Input.roll;
        return Vector3.ClampMagnitude(dir, 1f) * maxHorizontalSpeed * scale;
    }
}
