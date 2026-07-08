namespace VideoInferenceDemo;

public sealed class YoloSegmentation
{
    public YoloSegmentation(
        int classId,
        string className,
        float score,
        float x1,
        float y1,
        float x2,
        float y2,
        byte[] mask,
        int maskWidth,
        int maskHeight)
    {
        ClassId = classId;
        ClassName = className ?? string.Empty;
        Score = score;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Mask = mask ?? Array.Empty<byte>();
        MaskWidth = Math.Max(0, maskWidth);
        MaskHeight = Math.Max(0, maskHeight);
    }

    public int ClassId { get; }
    public string ClassName { get; }
    public float Score { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }
    public byte[] Mask { get; }
    public int MaskWidth { get; }
    public int MaskHeight { get; }

    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
}
