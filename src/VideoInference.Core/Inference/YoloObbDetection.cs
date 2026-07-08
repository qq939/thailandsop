namespace VideoInferenceDemo;

public sealed class YoloObbDetection
{
    public YoloObbDetection(
        int classId,
        string className,
        float score,
        float centerX,
        float centerY,
        float width,
        float height,
        float angleDeg)
    {
        ClassId = classId;
        ClassName = className;
        Score = score;
        CenterX = centerX;
        CenterY = centerY;
        Width = width;
        Height = height;
        AngleDeg = angleDeg;
    }

    public int ClassId { get; }
    public string ClassName { get; }
    public float Score { get; }
    public float CenterX { get; }
    public float CenterY { get; }
    public float Width { get; }
    public float Height { get; }
    public float AngleDeg { get; }

    public float X1 => CenterX - (Width / 2f);
    public float Y1 => CenterY - (Height / 2f);
    public float X2 => CenterX + (Width / 2f);
    public float Y2 => CenterY + (Height / 2f);
}
