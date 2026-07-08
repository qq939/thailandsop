namespace VideoInferenceDemo;

public sealed class CompositeVisionResultSink : IVisionResultSink
{
    private readonly IReadOnlyList<IVisionResultSink> _sinks;

    public CompositeVisionResultSink(IReadOnlyList<IVisionResultSink> sinks)
    {
        _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
    }

    public bool TryPublish(VisionFrameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var ok = true;
        foreach (var sink in _sinks)
        {
            ok = sink.TryPublish(result) && ok;
        }

        return ok;
    }
}
