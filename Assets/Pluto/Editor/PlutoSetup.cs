using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Adds a "Pluto" menu to the Unity menu bar:
//   1. Configure Android Settings  - player settings needed for ARCore
//   2. Create AR Scene             - AR Session, XR Origin, plane prefab, PlutoGame
//   3. Build and Run on Phone      - builds Builds/PlutoAR.apk and installs it over USB
public static class PlutoSetup
{
    const string Root = "Assets/Pluto";
    const string ScenePath = Root + "/Scenes/PlutoAR.unity";
    const string ApkPath = "Builds/PlutoAR.apk";

    [MenuItem("Pluto/1. Configure Android Settings", priority = 1)]
    public static void ConfigureAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PlayerSettings.companyName = "TeamPluto";
        PlayerSettings.productName = "Pluto AR Flight School";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.teampluto.arflightschool");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        if ((int)PlayerSettings.Android.minSdkVersion < 24)
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24;

        // Landscape only, like the Pluto Controller app.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        EditorUserBuildSettings.buildAppBundle = false;
        AssetDatabase.SaveAssets();
        Debug.Log("Pluto: Android settings configured. Next: Edit > Project Settings > XR Plug-in Management > Android tab > tick 'Google ARCore', then run Pluto > 2. Create AR Scene.");
    }

    [MenuItem("Pluto/2. Create AR Scene", priority = 2)]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Prefabs");
        EnsureFolder(Root + "/Scenes");
        var opaque = CreateMaterial("PlutoOpaque", "Standard", Color.white);
        var transparent = CreateMaterial("PlutoTransparent", "Sprites/Default", Color.white);
        var planeMat = CreateMaterial("ARPlaneFill", "Sprites/Default", new Color(0.2f, 0.8f, 1f, 0.25f));
        var planePrefab = CreatePlanePrefab(planeMat, transparent);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
        if (Object.FindAnyObjectByType<ARSession>() == null)
        {
            var session = new GameObject("AR Session");
            session.AddComponent<ARSession>();
            session.AddComponent<ARInputManager>();
        }

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
        {
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");
            origin = Object.FindAnyObjectByType<XROrigin>();
        }
        if (origin == null) origin = BuildOriginManually();

        var planeManager = GetOrAdd<ARPlaneManager>(origin.gameObject);
        planeManager.planePrefab = planePrefab;
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        GetOrAdd<ARRaycastManager>(origin.gameObject);
        GetOrAdd<ARAnchorManager>(origin.gameObject);

        var cam = origin.Camera;
        if (cam != null)
        {
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
        }

        var game = new GameObject("PlutoGame").AddComponent<PlutoGame>();
        game.opaqueMaterial = opaque;
        game.transparentMaterial = transparent;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/PlutoX.glb");
        if (model != null) game.droneModel = model;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Selection.activeGameObject = game.gameObject;
        Debug.Log("Pluto: AR scene created at " + ScenePath + (model != null ? " (using the PlutoX model)." : " (using the placeholder drone - drag a model onto PlutoGame > Drone Model)."));
    }

    [MenuItem("Pluto/3. Build and Run on Phone", priority = 20)]
    public static void BuildAndRun() => Build(true);

    [MenuItem("Pluto/4. Build APK only", priority = 21)]
    public static void BuildApk() => Build(false);

    static void Build(bool run)
    {
        Directory.CreateDirectory("Builds");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"Pluto: build {report.summary.result} - {report.summary.totalErrors} errors, output {ApkPath}");
    }

    static XROrigin BuildOriginManually()
    {
        var originGo = new GameObject("XR Origin (Mobile AR)");
        var origin = originGo.AddComponent<XROrigin>();
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(originGo.transform, false);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(offset.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.AddComponent<ARCameraManager>();
        camGo.AddComponent<ARCameraBackground>();
#if ENABLE_INPUT_SYSTEM
        var pose = camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        pose.positionInput = new UnityEngine.InputSystem.InputActionProperty(
            new UnityEngine.InputSystem.InputAction("Position", binding: "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3"));
        pose.rotationInput = new UnityEngine.InputSystem.InputActionProperty(
            new UnityEngine.InputSystem.InputAction("Rotation", binding: "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion"));
#endif
        origin.Camera = cam;
        origin.CameraFloorOffsetObject = offset;
        return origin;
    }

    static GameObject CreatePlanePrefab(Material fill, Material line)
    {
        string path = Root + "/Prefabs/ARPlane.prefab";
        var go = new GameObject("ARPlane");
        go.AddComponent<ARPlane>();
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = fill;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.widthMultiplier = 0.005f;
        lr.sharedMaterial = line;
        lr.startColor = lr.endColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        go.AddComponent<ARPlaneMeshVisualizer>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static Material CreateMaterial(string name, string shaderName, Color color)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Pluto: shader '{shaderName}' not found.");
                shader = Shader.Find("Sprites/Default");
            }
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        EditorUtility.SetDirty(m);
        return m;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
