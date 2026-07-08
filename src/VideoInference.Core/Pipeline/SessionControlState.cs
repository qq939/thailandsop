namespace VideoInferenceDemo;

public sealed record SessionControlState(
    SessionStatusSnapshot Status,
    string SessionName,
    string SourceLabel,
    string CurrentVideoLabel,
    bool HasVideoPath,
    bool IsProfileEnabled)
{
    public bool IsVideoSource => Status.IsVideoSource;

    public bool IsRunning => Status.RunState is SessionRunState.Starting or SessionRunState.Running or SessionRunState.Paused;

    public bool IsPaused => Status.RunState == SessionRunState.Paused;

    public bool CanStartCamera => IsProfileEnabled && !IsRunning;

    public bool CanStopCamera => IsRunning;

    public bool CanPlayVideo => HasVideoPath && (!IsRunning || IsPaused);

    public bool CanPauseVideo => IsVideoSource && IsRunning && !IsPaused;
}
