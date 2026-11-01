using UnityEngine;
using UnityEngine.Rendering;

// Builds the AR props (landing pads, hover zones, waypoints, pillars, rings)
// out of Unity primitives, so no extra 3D assets are needed.
public static class ArenaBuilder
{
    static Material Mat(Color c, bool transparent) => PlutoGame.Instance.MakeMaterial(c, transparent);

    public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
        Vector3 scale, Color color, bool transparent, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Mat(color, transparent);
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    public static void SetColor(GameObject root, Color color, bool transparent)
    {
        var mat = Mat(color, transparent);
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (!(r is LineRenderer)) r.sharedMaterial = mat;
    }

    public static LineRenderer Circle(Transform parent, Vector3 localPosition, Quaternion localRotation, float radius,
        Color color, float width, bool horizontal)
    {
        var go = new GameObject("Circle");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.widthMultiplier = width;
        lr.sharedMaterial = Mat(Color.white, true);
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        const int segments = 48;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float x = Mathf.Cos(a) * radius, y = Mathf.Sin(a) * radius;
            lr.SetPosition(i, horizontal ? new Vector3(x, 0f, y) : new Vector3(x, y, 0f));
        }
        return lr;
    }

    public static GameObject Pad(Transform parent, Vector3 localPosition, float radius, Color color, string name = "Pad")
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localPosition = localPosition;
        // Dark disc + coloured rim for contrast on any table colour.
        Primitive(PrimitiveType.Cylinder, "Disc", root, new Vector3(0f, 0.001f, 0f), new Vector3(radius * 2f, 0.0008f, radius * 2f),
            PlutoTheme.Deep.A(0.55f), true);
        Circle(root, new Vector3(0f, 0.003f, 0f), Quaternion.identity, radius, color, 0.006f, true);
        Circle(root, new Vector3(0f, 0.003f, 0f), Quaternion.identity, radius * 0.8f, color.A(0.6f), 0.002f, true);
        // "H" helipad marking
        Primitive(PrimitiveType.Cube, "H1", root, new Vector3(-radius * 0.35f, 0.003f, 0f), new Vector3(radius * 0.12f, 0.001f, radius * 0.9f), Color.white, false);
        Primitive(PrimitiveType.Cube, "H2", root, new Vector3(radius * 0.35f, 0.003f, 0f), new Vector3(radius * 0.12f, 0.001f, radius * 0.9f), Color.white, false);
        Primitive(PrimitiveType.Cube, "H3", root, new Vector3(0f, 0.003f, 0f), new Vector3(radius * 0.7f, 0.001f, radius * 0.12f), Color.white, false);
        return root.gameObject;
    }

    public static Renderer Zone(Transform parent, Vector3 center, Vector3 size, Color color) =>
        Primitive(PrimitiveType.Cube, "Zone", parent, center, size, color, true).GetComponent<Renderer>();

    // A floating ball with a thin post down to the surface - the post helps
    // judge depth/height in AR.
    public static GameObject Marker(Transform parent, Vector3 localPosition, float radius, Color color, string name = "Waypoint")
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localPosition = localPosition;
        Primitive(PrimitiveType.Sphere, "Ball", root, Vector3.zero, Vector3.one * radius * 2f, new Color(color.r, color.g, color.b, 0.5f), true);
        float h = localPosition.y - radius;
        if (h > 0.01f)
            Primitive(PrimitiveType.Cylinder, "Post", root, new Vector3(0f, -radius - h / 2f, 0f), new Vector3(0.004f, h / 2f, 0.004f),
                new Color(color.r, color.g, color.b, 0.6f), true);
        return root.gameObject;
    }

    public static GameObject Obstacle(Transform parent, Vector3 basePosition, Vector3 size, out Bounds bounds)
    {
        var center = basePosition + Vector3.up * size.y / 2f;
        bounds = new Bounds(center, size);
        return Primitive(PrimitiveType.Cube, "Pillar", parent, center, size, new Color(1f, 0.25f, 0.2f, 0.8f), true);
    }
}
