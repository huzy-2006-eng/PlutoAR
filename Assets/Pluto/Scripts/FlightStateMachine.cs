using System;

public enum FlightState { Disarmed, Armed, TakingOff, Flying, Landing, Landed, Crashed }

// Disarmed -> Armed -> TakingOff -> Flying <-> Landing -> Landed -> (TakingOff | Disarmed)
// Any invalid transition is simply refused, so e.g. you cannot disarm mid-air.
public class FlightStateMachine
{
    public FlightState State { get; private set; } = FlightState.Disarmed;
    public float TimeInState { get; private set; }

    public event Action<FlightState, FlightState> Changed;

    public bool IsAirborne => State == FlightState.TakingOff || State == FlightState.Flying
        || State == FlightState.Landing || State == FlightState.Crashed;
    public bool IsOnGroundArmed => State == FlightState.Armed || State == FlightState.Landed;
    public bool IsArmed => State != FlightState.Disarmed && State != FlightState.Crashed;

    public static bool CanTransition(FlightState from, FlightState to)
    {
        switch (from)
        {
            case FlightState.Disarmed: return to == FlightState.Armed;
            case FlightState.Armed: return to == FlightState.TakingOff || to == FlightState.Disarmed;
            case FlightState.TakingOff: return to == FlightState.Flying || to == FlightState.Armed || to == FlightState.Landing || to == FlightState.Crashed;
            case FlightState.Flying: return to == FlightState.Landing || to == FlightState.Landed || to == FlightState.Crashed;
            case FlightState.Landing: return to == FlightState.Landed || to == FlightState.Flying || to == FlightState.Crashed;
            case FlightState.Landed: return to == FlightState.TakingOff || to == FlightState.Disarmed;
            case FlightState.Crashed: return to == FlightState.Disarmed;
            default: return false;
        }
    }

    public bool TryTransition(FlightState to)
    {
        if (!CanTransition(State, to)) return false;
        var from = State;
        State = to;
        TimeInState = 0f;
        Changed?.Invoke(from, to);
        return true;
    }

    public void Tick(float dt) => TimeInState += dt;

    public void ForceReset()
    {
        var from = State;
        State = FlightState.Disarmed;
        TimeInState = 0f;
        if (from != FlightState.Disarmed) Changed?.Invoke(from, FlightState.Disarmed);
    }
}
