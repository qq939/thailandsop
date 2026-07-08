namespace VideoInferenceDemo;

public sealed record ModelActivationStatusInfo(
    ModelActivationState State,
    string Title,
    string Detail,
    string DeviceText)
{
    public static ModelActivationStatusInfo FromAttempt(ModelActivationAttemptResult attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return attempt.State switch
        {
            ModelActivationState.ModelSelected => new ModelActivationStatusInfo(
                attempt.State,
                "模型已选",
                InferenceDisplayTextFormatter.GetStatusText(attempt),
                InferenceDisplayTextFormatter.GetDeviceText(attempt)),
            ModelActivationState.NoModel => new ModelActivationStatusInfo(
                attempt.State,
                "无模型",
                InferenceDisplayTextFormatter.GetStatusText(attempt),
                InferenceDisplayTextFormatter.GetDeviceText(attempt)),
            _ => new ModelActivationStatusInfo(
                attempt.State,
                "异常",
                attempt.Message ?? InferenceDisplayTextFormatter.GetStatusText(attempt),
                InferenceDisplayTextFormatter.GetDeviceText(attempt))
        };
    }
}
