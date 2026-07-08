using System;
using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed class PipelineDrawStyleState
{
    private readonly object _gate = new();
    private Scalar? _globalOverride;
    private Scalar?[]? _overridesByClass;
    private int _boxThickness = 2;
    private double _labelFontScale = 0.55;

    public void Update(string? boxColor, string[]? boxColors, int? boxThickness, double? labelFontScale)
    {
        var parsedColor = PipelineFrameAnnotator.TryParseColor(boxColor, out var color) ? color : (Scalar?)null;
        var parsedColorOverrides = PipelineFrameAnnotator.ParseColorOverrides(boxColors);
        var thickness = PipelineFrameAnnotator.ResolveBoxThickness(boxThickness);
        var fontScale = PipelineFrameAnnotator.ResolveLabelFontScale(labelFontScale);

        lock (_gate)
        {
            _globalOverride = parsedColor;
            _overridesByClass = parsedColorOverrides;
            _boxThickness = thickness;
            _labelFontScale = fontScale;
        }
    }

    public PipelineDrawStyle GetSnapshot()
    {
        lock (_gate)
        {
            return new PipelineDrawStyle(
                _globalOverride,
                _overridesByClass,
                _boxThickness,
                _labelFontScale);
        }
    }
}
