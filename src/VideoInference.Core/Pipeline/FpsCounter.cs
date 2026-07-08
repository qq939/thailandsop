namespace VideoInferenceDemo;

public sealed class FpsCounter
{
    private long _lastMs;
    private double _ema;

    public double Update(long nowMs)
    {
        if (_lastMs == 0)
        {
            _lastMs = nowMs;
            return _ema;
        }

        var delta = nowMs - _lastMs;
        _lastMs = nowMs;
        if (delta <= 0)
        {
            return _ema;
        }

        var fps = 1000.0 / delta;
        _ema = _ema == 0 ? fps : (_ema * 0.9 + fps * 0.1);
        return _ema;
    }
}
