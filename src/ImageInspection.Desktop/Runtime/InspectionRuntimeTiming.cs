namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed record InspectionRuntimeTiming(
    double TriggerToImageMs,
    double ActionMs,
    double PostprocessMs,
    double TotalMs)
{
    public string Summary =>
        $"取图 {TriggerToImageMs:0.#} ms / Action {ActionMs:0.#} ms / 后处理 {PostprocessMs:0.#} ms / 总计 {TotalMs:0.#} ms";
}
