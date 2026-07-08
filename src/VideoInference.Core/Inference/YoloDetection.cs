namespace VideoInferenceDemo;

public sealed class YoloDetection
{
    public YoloDetection(int classId, string className, float score, float x1, float y1, float x2, float y2)
    {
        ClassId = classId;
        ClassName = className ?? string.Empty;
        Score = score;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public int ClassId { get; }
    public string ClassName { get; }
    public float Score { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }

    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
}
