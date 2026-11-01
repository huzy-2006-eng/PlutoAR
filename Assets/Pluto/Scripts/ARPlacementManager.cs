using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Plane detection + tap-to-place. Everything in the game lives under "Arena",
// which follows an ARAnchor at the tapped spot so it stays fixed on the real
// table while the phone moves.
//
// In the Unity Editor (no AR camera) it falls back to a grey floor you click on,
// so the flight / tutorial code can be tested with Play mode on a laptop.
[DefaultExecutionOrder(-50)]
public class ARPlacementManager : MonoBehaviour
{
    public Transform Arena { get; private set; }
    public bool IsPlaced { get; private set; }
    public bool Scanning { get; private set; }
    public string StatusMessage { get; private set; }
    public bool UseEditorFallback => Application.isEditor;

    public event Action Placed;

    const TrackableType PlaneHits = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.PlaneEstimated;

    static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    ARRaycastManager raycaster;
    ARPlaneManager planes;
    ARAnchorManager anchorManager;
    Camera cam;
    Transform anchor;
    Transform reticle;
    bool reticleValid;
    Pose reticlePose;

    void Awake()
    {
        Arena = new GameObject("Arena").transform;
        cam = Camera.main;

        var origin = FindAnyObjectByType<XROrigin>();
        if (origin != null)
        {
            if (cam == null) cam = origin.Camera;
            raycaster = origin.GetComponent<ARRaycastManager>();
            if (raycaster == null) raycaster = origin.gameObject.AddComponent<ARRaycastManager>();
            anchorManager = origin.GetComponent<ARAnchorManager>();
            if (anchorManager == null) anchorManager = origin.gameObject.AddComponent<ARAnchorManager>();
            planes = origin.GetComponent<ARPlaneManager>();
        }
        else
        {
            Debug.LogWarning("Pluto: no XR Origin in the scene. Run Pluto > 2. Create AR Scene.");
        }

        if (UseEditorFallback) SetupEditorFallback();
    }

    void SetupEditorFallback()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "EditorFloor (editor only)";
        ground.transform.localScale = Vector3.one * 0.5f;
        ground.GetComponent<Renderer>().sharedMaterial = PlutoGame.Instance.MakeMaterial(new Color(0.42f, 0.44f, 0.47f), false);

        if (cam == null) return;
        var background = cam.GetComponent<ARCameraBackground>();
        if (background != null) background.enabled = false;
        foreach (var b in cam.GetComponents<Behaviour>())
            if (b != null && b.GetType().Name == "TrackedPoseDriver") b.enabled = false;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.transform.SetPositionAndRotation(new Vector3(0f, 0.75f, -1.1f), Quaternion.LookRotation(new Vector3(0f, -0.55f, 1.1f)));
    }

    public void BeginScanning()
    {
        Scanning = true;
        Arena.gameObject.SetActive(false);
    }

    void Update()
    {
        if (anchor != null) Arena.SetPositionAndRotation(anchor.position, anchor.rotation);
        SetPlanesVisible(Scanning);
        UpdateReticle();
        StatusMessage = ComputeStatus();

        if (!Scanning) return;
        if (!InputHelper.TryGetTapDown(out var screenPos) || IsOverUI(screenPos)) return;

        // Tap point first; if it misses, use the ring in the middle of the screen.
        if (TryHitSurface(screenPos, out var pose)) PlaceAt(pose);
        else if (reticleValid) PlaceAt(reticlePose);
    }

    void UpdateReticle()
    {
        if (reticle == null)
        {
            reticle = new GameObject("PlacementReticle").transform;
            ArenaBuilder.Circle(reticle, new Vector3(0f, 0.002f, 0f), Quaternion.identity, 0.1f, new Color(0.3f, 0.9f, 1f, 0.9f), 0.006f, true);
            ArenaBuilder.Circle(reticle, new Vector3(0f, 0.002f, 0f), Quaternion.identity, 0.015f, new Color(1f, 1f, 1f, 0.9f), 0.006f, true);
        }
        reticleValid = Scanning && cam != null
            && TryHitSurface(new Vector2(Screen.width / 2f, Screen.height / 2f), out reticlePose);
        reticle.gameObject.SetActive(reticleValid);
        if (reticleValid) reticle.SetPositionAndRotation(reticlePose.position, Quaternion.identity);
    }

    bool TryHitSurface(Vector2 screenPos, out Pose pose)
    {
        pose = default;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(screenPos);

        if (UseEditorFallback)
        {
            if (!Physics.Raycast(ray, out var hit, 20f)) return false;
            pose = new Pose(hit.point, Quaternion.identity);
            return true;
        }

        if (raycaster != null && raycaster.Raycast(screenPos, Hits, PlaneHits))
        {
            pose = Hits[0].pose;
            return true;
        }

        // Fallback: intersect with the infinite plane of the biggest detected surface.
        if (planes == null) return false;
        ARPlane best = null;
        foreach (var p in planes.trackables)
            if (p.alignment != PlaneAlignment.Vertical && (best == null || p.size.sqrMagnitude > best.size.sqrMagnitude))
                best = p;
        if (best == null) return false;
        var surface = new Plane(Vector3.up, best.center);
        if (!surface.Raycast(ray, out float distance) || distance > 5f) return false;
        pose = new Pose(ray.GetPoint(distance), Quaternion.identity);
        return true;
    }

    void PlaceAt(Pose pose)
    {
        // Face the arena away from the camera so "forward" on the sticks = away from the pilot.
        var forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        if (anchor != null) Destroy(anchor.gameObject);
        var go = new GameObject("ArenaAnchor");
        go.transform.SetPositionAndRotation(pose.position, rotation);
        if (!UseEditorFallback && anchorManager != null) go.AddComponent<ARAnchor>();
        anchor = go.transform;

        Arena.SetPositionAndRotation(pose.position, rotation);
        Arena.gameObject.SetActive(true);
        Scanning = false;
        IsPlaced = true;
        Placed?.Invoke();
    }

    bool IsOverUI(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        uiHits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = screenPos }, uiHits);
        return uiHits.Count > 0;
    }

    void SetPlanesVisible(bool visible)
    {
        if (planes == null) return;
        foreach (var plane in planes.trackables)
        {
            var viz = plane.GetComponent<ARPlaneMeshVisualizer>();
            if (viz != null && viz.enabled != visible) viz.enabled = visible;
            foreach (var r in plane.GetComponentsInChildren<Renderer>())
                if (r.enabled != visible) r.enabled = visible;
        }
    }

    string ComputeStatus()
    {
        if (UseEditorFallback)
            return Scanning ? "EDITOR TEST MODE: click the grey floor to place the drone" : null;

        switch (ARSession.state)
        {
            case ARSessionState.Unsupported:
                return "This phone does not support ARCore. Use a phone from Google's ARCore supported devices list.";
            case ARSessionState.NeedsInstall:
            case ARSessionState.Installing:
                return "Installing Google Play Services for AR...";
            case ARSessionState.None:
            case ARSessionState.CheckingAvailability:
            case ARSessionState.Ready:
                return "Starting the camera...";
            case ARSessionState.SessionInitializing:
                return (IsPlaced ? "Tracking lost: " : "") + TrackingAdvice();
        }

        if (!Scanning) return null;
        if (reticleValid) return "Aim the blue ring where you want the drone, then tap the screen";
        return planes != null && planes.trackables.count > 0
            ? "Surface found - point the phone at it until the blue ring appears"
            : "Slowly move your phone over a table or the floor to find a surface";
    }

    static string TrackingAdvice()
    {
        switch (ARSession.notTrackingReason)
        {
            case NotTrackingReason.InsufficientLight: return "too dark - add more light.";
            case NotTrackingReason.InsufficientFeatures: return "point at a textured surface (not plain white or glossy).";
            case NotTrackingReason.ExcessiveMotion: return "move the phone more slowly.";
            case NotTrackingReason.Relocalizing: return "hold still, finding the table again...";
            default: return "slowly move your phone around.";
        }
    }
}
