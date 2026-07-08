using System;

namespace VideoInferenceDemo;

public readonly record struct ImageRectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

public readonly record struct ImageAdjustmentGeometry(
    int OriginalWidth,
    int OriginalHeight,
    int TargetWidth,
    int TargetHeight,
    float RatioX,
    float RatioY,
    int PadX,
    int PadY)
{
    public static ImageAdjustmentGeometry CreateLetterbox(int originalWidth, int originalHeight, int targetWidth, int targetHeight)
    {
        if (originalWidth <= 0 || originalHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalWidth), "Image dimensions must be positive.");
        }

        var scale = Math.Min(targetWidth / (float)originalWidth, targetHeight / (float)originalHeight);
        var resizedWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));
        var padX = (targetWidth - resizedWidth) / 2;
        var padY = (targetHeight - resizedHeight) / 2;

        return new ImageAdjustmentGeometry(
            originalWidth,
            originalHeight,
            targetWidth,
            targetHeight,
            resizedWidth / (float)originalWidth,
            resizedHeight / (float)originalHeight,
            padX,
            padY);
    }

    public ImageRectF AdjustRect(ImageRectF rectangle)
    {
        var x = (rectangle.X - PadX) / RatioX;
        var y = (rectangle.Y - PadY) / RatioY;
        var width = rectangle.Width / RatioX;
        var height = rectangle.Height / RatioY;

        var clampedX = Clamp(x, 0f, OriginalWidth);
        var clampedY = Clamp(y, 0f, OriginalHeight);
        var clampedRight = Clamp(x + width, 0f, OriginalWidth);
        var clampedBottom = Clamp(y + height, 0f, OriginalHeight);

        return new ImageRectF(
            clampedX,
            clampedY,
            Math.Max(0f, clampedRight - clampedX),
            Math.Max(0f, clampedBottom - clampedY));
    }

    private static float Clamp(float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
