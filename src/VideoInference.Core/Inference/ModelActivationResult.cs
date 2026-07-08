namespace VideoInferenceDemo;

public enum ModelActivationState
{
    ModelSelected,
    NoModel,
    Error
}

public sealed record ModelActivationResult(
    PrimaryVisionTaskBinding Binding);

public sealed record ModelActivationAttemptResult(
    bool Success,
    ModelActivationResult? Activation,
    ModelActivationState State,
    string? Message = null,
    Exception? Exception = null)
{
    public SessionStatusSnapshot ToStatusSnapshot(bool isVideoSource = false)
    {
        var runState = State switch
        {
            ModelActivationState.ModelSelected => SessionRunState.ModelSelected,
            ModelActivationState.NoModel => SessionRunState.NoModel,
            _ => SessionRunState.Error
        };

        return new SessionStatusSnapshot(
            runState,
            isVideoSource,
            SessionTransitionState.Unknown,
            Message);
    }
}
