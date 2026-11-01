using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Draws the drone: either the organisers' Pluto model (auto-scaled to palm size)
// or a built-in placeholder quad. Also spins propellers, tilts the body in the
// direction of travel, colours the status LEDs, and draws a ground shadow and
// an optional flight-path trail.
public class DroneVisual : MonoBehaviour
{
    public float targetWidth = 0.15f;

    public bool ShowTrail { get; set; }

    DroneController drone;
    Transform tilt;
    Transform shadow;
    TrailRenderer trail;
    readonly List<Transform> props = new List<Transform>();
    readonly List<Quaternion> propBase = new List<Quaternion>();
    readonly List<Renderer> leds = new List<Renderer>();
    float propAngle;
    float propSpeed;

    public void Build(DroneController d, GameObject customModel, float yawOffset)
    {
        drone = d;
        tilt = new GameObject("Tilt").transform;
        tilt.SetParent(transform, false);

        if (customModel != null) BuildCustom(customModel, yawOffset);
        else BuildPlaceholder();

        shadow = ArenaBuilder.Primitive(PrimitiveType.Cylinder, "Shadow", transform, Vector3.zero,
            new Vector3(0.11f, 0.0005f, 0.11f), new Color(0f, 0f, 0f, 0.35f), true).transform;

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 6f;
        trail.widthMultiplier = 0.006f;
        trail.minVertexDistance = 0.01f;
        trail.sharedMaterial = PlutoGame.Instance.MakeMaterial(Color.white, true);
        trail.startColor = new Color(0.3f, 0.9f, 1f, 0.9f);
        trail.endColor = new Color(0.3f, 0.9f, 1f, 0f);
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.emitting = false;
    }

    void BuildPlaceholder()
    {
        var dark = new Color(0.15f, 0.16f, 0.18f);
        var blue = new Color(0.1f, 0.45f, 0.95f);
        var grey = new Color(0.55f, 0.57f, 0.6f);
        var up = Vector3.up;

        Part(PrimitiveType.Cube, "Body", up * 0.016f, new Vector3(0.045f, 0.014f, 0.065f), dark);
        Part(PrimitiveType.Cube, "TopPlate", up * 0.025f, new Vector3(0.04f, 0.004f, 0.05f), blue);
        Part(PrimitiveType.Cube, "Battery", new Vector3(0f, 0.03f, -0.004f), new Vector3(0.025f, 0.008f, 0.035f), grey);

        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            var arm = Part(PrimitiveType.Cube, "Arm", dir * 0.033f + up * 0.016f, new Vector3(0.008f, 0.006f, 0.066f), dark);
            arm.localRotation = Quaternion.Euler(0f, angle, 0f);

            var motor = dir * 0.066f;
            Part(PrimitiveType.Cylinder, "Motor", motor + up * 0.02f, new Vector3(0.014f, 0.007f, 0.014f), grey);
            Part(PrimitiveType.Cube, "Leg", motor + up * 0.006f, new Vector3(0.006f, 0.012f, 0.006f), dark);
            var prop = Part(PrimitiveType.Cube, "Prop", motor + up * 0.029f, new Vector3(0.05f, 0.0015f, 0.007f), new Color(0.9f, 0.9f, 0.95f));
            props.Add(prop);
            propBase.Add(prop.localRotation);
        }

        // Two LEDs on the nose show which way is "forward".
        for (int s = -1; s <= 1; s += 2)
            leds.Add(Part(PrimitiveType.Sphere, "LED", new Vector3(0.012f * s, 0.016f, 0.034f), Vector3.one * 0.008f, Color.red).GetComponent<Renderer>());
    }

    void BuildCustom(GameObject prefab, float yawOffset)
    {
        var holder = new GameObject("Model").transform;
        holder.SetParent(tilt, false);
        holder.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        var model = Instantiate(prefab, holder);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (var col in model.GetComponentsInChildren<Collider>()) Destroy(col);

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var b = WorldBounds(renderers);
        float width = Mathf.Max(b.size.x, b.size.z);
        if (width > 1e-5f) model.transform.localScale *= targetWidth / width;

        // Put the bottom-centre of the model on the drone's origin.
        b = WorldBounds(renderers);
        model.transform.position += holder.position - new Vector3(b.center.x, b.min.y, b.center.z);

        foreach (var t in model.GetComponentsInChildren<Transform>())
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("prop") || n.Contains("rotor") || n.Contains("blade"))
            {
                props.Add(t);
                propBase.Add(t.localRotation);
            }
        }

        // Small status LED on the nose so pilots can always see "forward".
        leds.Add(Part(PrimitiveType.Sphere, "LED", new Vector3(0f, b.size.y * 0.5f, targetWidth * 0.3f), Vector3.one * 0.01f, Color.red).GetComponent<Renderer>());
    }

    static Bounds WorldBounds(Renderer[] renderers)
    {
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    Transform Part(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color) =>
        ArenaBuilder.Primitive(type, name, tilt, position, scale, color, false).transform;

    void LateUpdate()
    {
        if (drone == null) return;
        var s = drone.State;
        float dt = Time.deltaTime;

        float targetSpeed;
        switch (s)
        {
            case FlightState.Disarmed:
            case FlightState.Crashed: targetSpeed = 0f; break;
            case FlightState.Armed:
            case FlightState.Landed: targetSpeed = 900f; break;
            default: targetSpeed = 1800f + 900f * Mathf.Max(0f, drone.Input.throttle); break;
        }
        propSpeed = Mathf.Lerp(propSpeed, targetSpeed, 1f - Mathf.Exp(-3f * dt));
        propAngle = (propAngle + propSpeed * dt) % 360f;
        for (int i = 0; i < props.Count; i++)
            if (props[i] != null)
                props[i].localRotation = propBase[i] * Quaternion.Euler(0f, i % 2 == 0 ? propAngle : -propAngle, 0f);

        Quaternion goal;
        if (s == FlightState.Crashed)
        {
            goal = Quaternion.Euler(35f, 0f, 25f);
        }
        else
        {
            float speed = Mathf.Max(0.01f, drone.maxHorizontalSpeed);
            float f = Mathf.Clamp(Vector3.Dot(drone.Velocity, drone.HeadingForward) / speed, -1f, 1f);
            float r = Mathf.Clamp(Vector3.Dot(drone.Velocity, drone.HeadingRight) / speed, -1f, 1f);
            goal = Quaternion.Euler(f * 18f, 0f, -r * 18f);
        }
        tilt.localRotation = Quaternion.Slerp(tilt.localRotation, goal, 1f - Mathf.Exp(-8f * dt));

        float alt = drone.Altitude;
        float k = Mathf.Clamp01(alt / 1.5f);
        float size = Mathf.Lerp(0.11f, 0.05f, k);
        shadow.localPosition = new Vector3(0f, -alt + 0.0015f, 0f);
        shadow.localScale = new Vector3(size, 0.0005f, size);

        bool blink = Mathf.Repeat(Time.time, 0.6f) < 0.3f;
        Color led;
        switch (s)
        {
            case FlightState.Disarmed: led = HUDManager.Bad; break;
            case FlightState.Armed:
            case FlightState.Landed: led = blink ? HUDManager.Good : Color.black; break;
            case FlightState.Landing: led = HUDManager.Warn; break;
            case FlightState.Crashed: led = blink ? HUDManager.Bad : Color.black; break;
            default: led = HUDManager.Good; break;
        }
        foreach (var r in leds) r.material.color = led;

        trail.emitting = ShowTrail && drone.Fsm.IsAirborne;
    }
}
