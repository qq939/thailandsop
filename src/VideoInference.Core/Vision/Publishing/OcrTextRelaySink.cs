namespace VideoInferenceDemo;

public sealed class OcrTextRelaySink : IVisionResultSink
{
    private readonly Action<string> _onText;

    public OcrTextRelaySink(Action<string> onText)
    {
        _onText = onText ?? throw new ArgumentNullException(nameof(onText));
    }

    public bool TryPublish(VisionFrameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Payload is OcrPayload ocrPayload)
        {
            _onText(ocrPayload.Text);
            return true;
        }

        return false;
    }
}
