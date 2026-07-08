namespace VideoInferenceDemo;

public enum SessionStartPrecheckState
{
    Ok,
    NoModel,
    Error
}

public sealed record SessionStartPrecheckResult(
    SessionStartPrecheckState State,
    string? Message = null)
{
    public static SessionStartPrecheckResult Success { get; } = new(SessionStartPrecheckState.Ok);

    public bool IsSuccess => State == SessionStartPrecheckState.Ok;

    public SessionStatusSnapshot ToStatusSnapshot(bool isVideoSource = false)
    {
        var runState = State switch
        {
            SessionStartPrecheckState.NoModel => SessionRunState.NoModel,
            SessionStartPrecheckState.Error => SessionRunState.Error,
            _ => SessionRunState.Idle
        };

        return new SessionStatusSnapshot(
            runState,
            isVideoSource,
            SessionTransitionState.Unknown,
            Message);
    }
}
