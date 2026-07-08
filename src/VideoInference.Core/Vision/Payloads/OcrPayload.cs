namespace VideoInferenceDemo;

public sealed class OcrPayload : VisionTaskPayload
{
    public string Text { get; }

    public IReadOnlyList<OcrTextBlock> Blocks { get; }

    public OcrPayload(string text)
        : this(text, Array.Empty<OcrTextBlock>())
    {
    }

    public OcrPayload(string text, IReadOnlyList<OcrTextBlock>? blocks)
    {
        Text = text ?? string.Empty;
        Blocks = blocks ?? Array.Empty<OcrTextBlock>();
    }
}

public sealed record OcrTextBlock(
    string Text,
    float Score,
    IReadOnlyList<OcrPoint> BoxPoints);

public sealed record OcrPoint(int X, int Y);
