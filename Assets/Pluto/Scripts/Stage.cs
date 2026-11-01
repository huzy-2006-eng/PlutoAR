using System;
using System.Collections.Generic;
using UnityEngine;

// One step of the tutorial or a mission. IsComplete is checked every frame and
// must be driven by what the pilot actually does; Progress returns live
// feedback / corrective prompts shown under the instruction. Meter (0..1) and
// MeterLabel are optional and must read the same real values IsComplete uses.
public class Stage
{
    public string Title;
    public string Instruction;
    public Action OnEnter;
    public Action OnExit;
    public Func<bool> IsComplete;
    public Func<string> Progress;
    public Func<float> Meter;
    public Func<string> MeterLabel;
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
        if (f > 0.07f) parts.Add("ahead (right stick up)");
        else if (f < -0.07f) parts.Add("behind (right stick down)");
        if (r > 0.07f) parts.Add("right (right stick right)");
        else if (r < -0.07f) parts.Add("left (right stick left)");
        if (delta.y > 0.07f) parts.Add("higher (left stick up)");
        else if (delta.y < -0.07f) parts.Add("lower (left stick down)");
        return parts.Count == 0 ? "right here" : string.Join(", ", parts) + $"  ·  {delta.magnitude:0.00} m";
    }

    // Corrective prompt for staying inside an axis-aligned box.
    public static string BoxHint(DroneController d, Vector3 center, Vector3 size)
    {
        var delta = d.Center - center;
        var half = size / 2f;
        if (delta.y > half.y) return "Too high. Ease the left stick down.";
        if (delta.y < -half.y) return "Too low. Ease the left stick up.";
        float f = Vector3.Dot(delta, d.HeadingForward);
        float r = Vector3.Dot(delta, d.HeadingRight);
        if (r > half.x) return "Drifting right. Nudge the right stick left.";
        if (r < -half.x) return "Drifting left. Nudge the right stick right.";
        if (f > half.z) return "Drifting forward. Nudge the right stick down.";
        if (f < -half.z) return "Drifting back. Nudge the right stick up.";
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
                row.Add(done[j] ? Colored(names[j], PlutoTheme.Mint) : Colored(names[j], PlutoTheme.TextMuted));
            lines.Add(string.Join("      ", row));
        }
        return string.Join("\n", lines);
    }

    public static string FormatTime(float seconds) => $"{(int)(seconds / 60f)}:{seconds % 60f:00.0}";

    public static string Colored(string s, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";

    public static string Warn(string s) => Colored(s, PlutoTheme.Amber);

    public static string Good(string s) => Colored(s, PlutoTheme.Mint);
}

// Shared plumbing for the tutorial and the missions.
public abstract class GuidedMode : MonoBehaviour
{
    const float UiInterval = 0.1f;

    protected PlutoGame game;
    protected readonly StageRunner runner = new StageRunner();
    readonly List<GameObject> spawned = new List<GameObject>();
    float nextUi;

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
            Hud.SetObjective(Label, $"{i + 1:00} / {runner.Count:00}", s.Title, s.Instruction);
            nextUi = 0f;
        };
        runner.StageCompleted += (i, s) => nextUi = 0f;
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

    // Modes can freeze timers and dwell checks (e.g. during AR tracking loss).
    protected virtual bool Paused => false;

    protected virtual void Update()
    {
        if (!runner.Running) return;
        if (Paused)
        {
            Hud.SetObjectiveDetail(FlightHelpers.Warn("Paused while tracking is lost. Timer and holds resume when it returns."));
            nextUi = 0f;
            return;
        }
        runner.Tick(Time.deltaTime);
        if (!runner.Running) return;

        // Completion is checked every frame above; the card only needs ~10 Hz.
        if (Time.unscaledTime < nextUi) return;
        nextUi = Time.unscaledTime + UiInterval;
        var s = runner.Current;
        if (s == null) return;
        bool done = runner.IsAdvancing;
        Hud.SetObjectiveComplete(done);
        Hud.SetObjectiveDetail(done ? FlightHelpers.Good("Step complete. Nice flying.") : s.Progress?.Invoke());
        if (s.Meter != null) Hud.SetObjectiveMeter(done ? 1f : s.Meter(), s.MeterLabel?.Invoke());
        else Hud.SetObjectiveMeter(null, null);
        Hud.SetTimer("TIME   " + FlightHelpers.FormatTime(runner.TotalTime));
    }
}
