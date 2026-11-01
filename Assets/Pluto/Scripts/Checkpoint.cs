using System.Collections.Generic;
using UnityEngine;

public enum RingState { Waiting, Active, Passed }

// A floating ring the drone must fly through. Detection is purely geometric:
// the drone's path between two frames must cross the ring's plane inside its radius.
public class Checkpoint : MonoBehaviour
{
    public float Radius { get; private set; }
    public RingState State { get; private set; }

    LineRenderer line;

    public static Checkpoint Create(Transform parent, Vector3 localPosition, float yawDeg, float radius, int number)
    {
        var go = new GameObject("Ring" + number);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

        var cp = go.AddComponent<Checkpoint>();
        cp.Radius = radius;
        cp.line = ArenaBuilder.Circle(go.transform, Vector3.zero, Quaternion.identity, radius, Color.white, 0.012f, false);

        float h = localPosition.y - radius;
        if (h > 0.01f)
            ArenaBuilder.Primitive(PrimitiveType.Cylinder, "Post", go.transform, new Vector3(0f, -radius - h / 2f, 0f),
                new Vector3(0.004f, h / 2f, 0.004f), new Color(1f, 1f, 1f, 0.35f), true);

        cp.SetState(RingState.Waiting);
        return cp;
    }

    public void SetState(RingState state)
    {
        State = state;
        // Bright cyan = next, dimmed white = later, mint = cleared.
        Color c = state == RingState.Active ? PlutoTheme.Cyan
            : state == RingState.Passed ? PlutoTheme.Mint.A(0.55f)
            : new Color(1f, 1f, 1f, 0.25f);
        line.startColor = c;
        line.endColor = c;
        line.widthMultiplier = 0.012f;
    }

    void Update()
    {
        if (State == RingState.Active)
            line.widthMultiplier = 0.012f * (1.4f + 0.4f * Mathf.Sin(Time.time * 6f));
    }

    // Both points are in Arena-local coordinates (the ring's parent space).
    public bool CrossedBy(Vector3 previous, Vector3 current)
    {
        var inv = Quaternion.Inverse(transform.localRotation);
        var a = inv * (previous - transform.localPosition);
        var b = inv * (current - transform.localPosition);
        if ((a.z > 0f) == (b.z > 0f) || Mathf.Approximately(a.z, b.z)) return false;
        float t = a.z / (a.z - b.z);
        var hit = Vector3.Lerp(a, b, t);
        return new Vector2(hit.x, hit.y).magnitude <= Radius;
    }
}

// An ordered list of rings; only the active one counts.
public class RingCourse
{
    public readonly List<Checkpoint> Rings = new List<Checkpoint>();
    public int Next { get; private set; }
    public bool Done => Next >= Rings.Count;
    public Checkpoint Current => Done ? null : Rings[Next];

    Vector3 previous;

    public void Reset(Vector3 start)
    {
        Next = 0;
        previous = start;
        for (int i = 0; i < Rings.Count; i++) Rings[i].SetState(i == 0 ? RingState.Active : RingState.Waiting);
    }

    public bool Step(Vector3 position)
    {
        bool passed = false;
        if (!Done && Rings[Next].CrossedBy(previous, position))
        {
            Rings[Next].SetState(RingState.Passed);
            Next++;
            if (!Done) Rings[Next].SetState(RingState.Active);
            passed = true;
        }
        previous = position;
        return passed;
    }
}
