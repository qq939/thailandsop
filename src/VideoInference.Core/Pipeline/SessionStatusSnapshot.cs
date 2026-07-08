namespace VideoInferenceDemo;

public enum SessionRunState
{
    Idle,
    Disabled,
    Starting,
    Running,
    Paused,
    Stopped,
    Completed,
    Blocked,
    Error,
    NoModel,
    NoSource,
    ModelSelected
}

public enum SessionTransitionState
{
    Unknown,
    Normal,
    Abnormal
}

public enum SessionBadgeState
{
    Idle,
    Pending,
    Running,
    Paused,
    Disabled,
    Error
}

public readonly record struct SessionStatusSnapshot(
    SessionRunState RunState,
    bool IsVideoSource,
    SessionTransitionState TransitionState,
    string? ErrorMessage)
{
    public static SessionStatusSnapshot Empty { get; } = new(SessionRunState.Idle, false, SessionTransitionState.Unknown, null);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage) || RunState == SessionRunState.Error;

    public SessionBadgeState BadgeState => HasError
        ? SessionBadgeState.Error
        : RunState switch
        {
            SessionRunState.Starting or SessionRunState.ModelSelected => SessionBadgeState.Pending,
            SessionRunState.Running => SessionBadgeState.Running,
            SessionRunState.Paused => SessionBadgeState.Paused,
            SessionRunState.Disabled => SessionBadgeState.Disabled,
            _ => SessionBadgeState.Idle
        };
}
