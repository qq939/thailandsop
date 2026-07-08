namespace VideoInferenceDemo;

public sealed record YoloInferenceTiming(
    double PreprocessMs,
    double RunMs,
    double PostprocessMs,
    double TotalMs)
{
    public static YoloInferenceTiming Empty { get; } = new(0, 0, 0, 0);
}
