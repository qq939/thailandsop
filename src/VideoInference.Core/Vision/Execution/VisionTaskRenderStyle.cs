using OpenCvSharp;

namespace VideoInferenceDemo;

public readonly record struct VisionTaskRenderStyle(
    Scalar? GlobalOverride,
    Scalar?[]? OverridesByClass,
    int BoxThickness,
    double LabelFontScale);
