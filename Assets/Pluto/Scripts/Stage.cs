using System;
using System.Collections.Generic;
using UnityEngine;

// One step of the tutorial or a mission. IsComplete is checked every frame and
// must be driven by what the pilot actually does; Progress returns live
// feedback / corrective prompts shown under the instruction.
public class Stage
{
    public string Title;
    public string Instruction;
    public Action OnEnter;
    public Action OnExit;
    public Func<bool> IsComplete;
    public Func<string> Progress;
}

public class StageRunner
{
    public const float AdvanceDelay = 1.2f;

    readonly List<Stage> stages = new List<Stage>();
    float advanceTimer = -1f;

    public int Index { get; private set; } = -1;
    public int Count => stages.Count;
    public Stage Current => Index >= 0 && Index < stages.Count ? stages[Index] : null;
    public bool Running { get; private set; }
    public bool IsAdvancing => advanceTimer >= 0f;
    public float StageTime { get; private set; }
    public float TotalTime { get; private set; }

    public event Action<int, Stage> StageStarted;
    public event Action<int, Stage> StageCompleted;
    public event Action AllCompleted;

    public void Start(IEnumerable<Stage> list)
    {
        Stop();
        stages.Clear();
        stages.AddRange(list);
        Running = true;
        TotalTime = 0f;
        Enter(0);
    }

    public void Stop()
    {
        if (Running) Current?.OnExit?.Invoke();
        Running = false;
        Index = -1;
        advanceTimer = -1f;
    }

    public void Tick(float dt)
    {
        if (!Running || Current == null) return;
        TotalTime += dt;
        StageTime += dt;

        if (IsAdvancing)
        {
            advanceTimer -= dt;
            if (advanceTimer >= 0f) return;
            advanceTimer = -1f;
            Current.OnExit?.Invoke();
            if (Index + 1 >= stages.Count)
            {
                Running = false;
                AllCompleted?.Invoke();
            }
            else
            {
                Enter(Index + 1);
            }
            return;
        }

        if (Current.IsComplete != null && Current.IsComplete())
        {
            StageCompleted?.Invoke(Index, Current);
            advanceTimer = AdvanceDelay;
        }
    }

    void Enter(int i)
    {
        Index = i;
        StageTime = 0f;
        Current.OnEnter?.Invoke();
        StageStarted?.Invoke(i, Current);
    }
}

public static class FlightHelpers
{
    // "ahead, to the left, higher - 0.42 m", in the drone's own frame, with the stick to use.
    public static string Direction(DroneController d, Vector3 target, bool horizontalOnly = false)
    {
        var delta = target - d.Center;
        if (horizontalOnly) delta.y = 0f;
        float f = Vector3.Dot(delta, d.HeadingForward);
        float r = Vector3.Dot(delta, d.HeadingRight);
        var parts = new List<string>();
        if (f > 0.07f) parts.Add("ahead (right stick UP)");
        else if (f < -0.07f) parts.Add("behind (right stick DOWN)");
        if (r > 0.07f) parts.Add("to the right (right stick RIGHT)");
        else if (r < -0.07f) parts.Add("to the left (right stick LEFT)");
        if (delta.y > 0.07f) parts.Add("higher (left stick UP)");
        else if (delta.y < -0.07f) parts.Add("lower (left stick DOWN)");
        return parts.Count == 0 ? "right here!" : string.Join(", ", parts) + $"  -  {delta.magnitude:0.00} m";
    }

    // Corrective prompt for staying inside an axis-aligned box.
    public static string BoxHint(DroneController d, Vector3 center, Vector3 size)
    {
        var delta = d.Center - center;
        var half = size / 2f;
        if (delta.y > half.y) return "Too high - push the LEFT stick DOWN a little.";
        if (delta.y < -half.y) return "Too low - push the LEFT stick UP a little.";
        float f = Vector3.Dot(delta, d.HeadingForward);
        float r = Vector3.Dot(delta, d.HeadingRight);
        if (r > half.x) return "Drifting RIGHT - nudge the RIGHT stick LEFT.";
        if (r < -half.x) return "Drifting LEFT - nudge the RIGHT stick RIGHT.";
        if (f > half.z) return "Drifting FORWARD - nudge the RIGHT stick DOWN.";
        if (f < -half.z) return "Drifting BACKWARD - nudge the RIGHT stick UP.";
        return "Get back inside the box.";
    }

    public static bool InBox(Vector3 point, Vector3 center, Vector3 size) => new Bounds(center, size).Contains(point);

    public static bool OnPad(Vector3 localPosition, Vector3 padPosition, float radius) =>
        Vector2.Distance(new Vector2(localPosition.x, localPosition.z), new Vector2(padPosition.x, padPosition.z)) <= radius;

    public static string Checklist(string[] names, bool[] done, int perLine = 2)
    {
        var lines = new List<string>();
        for (int i = 0; i < names.Length; i += perLine)
        {
            var row = new List<string>();
            for (int j = i; j < Mathf.Min(i + perLine, names.Length); j++)
                row.Add(done[j] ? Colored($"[OK] {names[j]}", HUDManager.Good) : $"[  ] {names[j]}");
            lines.Add(string.Join("      ", row));
        }
        return string.Join("\n", lines);
    }

    public static string FormatTime(float seconds) => $"{(int)(seconds / 60f)}:{seconds % 60f:00.0}";

    public static string Colored(string s, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";

    public static string Warn(string s) => Colored(s, HUDManager.Warn);
}

// Shared plumbing for the tutorial and the missions.
public abstract class GuidedMode : MonoBehaviour
{
    protected PlutoGame game;
    protected readonly StageRunner runner = new StageRunner();
    readonly List<GameObject> spawned = new List<GameObject>();

    protected DroneController Drone => game.Drone;
    protected HUDManager Hud => game.Hud;
    protected Transform Arena => game.Arena;
    protected abstract string Label { get; }

    public bool Active => runner.Running;

    public void Init(PlutoGame g)
    {
        game = g;
        runner.StageStarted += (i, s) =>
        {
            Hud.SetInstruction($"{Label}  -  STEP {i + 1}/{runner.Count}: {s.Title}", s.Instruction);
            Hud.SetProgress(null);
        };
        runner.StageCompleted += (i, s) => Hud.Toast($"Step {i + 1} complete!", HUDManager.Good, 1.5f);
        runner.AllCompleted += OnAllCompleted;
    }

    public virtual void Stop()
    {
        runner.Stop();
        ClearSpawned();
        if (game == null) return;
        Hud.SetAltitudeTarget(null);
        Drone.SuppressDisarmedWarning = false;
        game.Controls.Highlight(null);
    }

    protected abstract void OnAllCompleted();

    protected T Spawn<T>(T component) where T : Component
    {
        spawned.Add(component.gameObject);
        return component;
    }

    protected GameObject Spawn(GameObject go)
    {
        spawned.Add(go);
        return go;
    }

    protected void ClearSpawned()
    {
        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
    }

    protected virtual void Update()
    {
        if (!runner.Running) return;
        runner.Tick(Time.deltaTime);
        if (!runner.Running) return;
        var s = runner.Current;
        Hud.SetProgress(runner.IsAdvancing ? FlightHelpers.Colored("Well done!", HUDManager.Good) : s?.Progress?.Invoke());
        Hud.SetTimer(FlightHelpers.FormatTime(runner.TotalTime));
    }
}
