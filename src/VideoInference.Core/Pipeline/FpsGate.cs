using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed class FpsGate
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Func<double> _getFps;
    private double _frameMs;
    private long _nextMs;

    public FpsGate(Func<double> getFps)
    {
        _getFps = getFps;
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        var fps = _getFps();
        if (fps <= 0)
        {
            return;
        }

        var newFrameMs = 1000.0 / fps;
        if (Math.Abs(newFrameMs - _frameMs) > 0.01)
        {
            _frameMs = newFrameMs;
            _nextMs = 0;
        }

        var now = _sw.ElapsedMilliseconds;
        if (_nextMs == 0)
        {
            _nextMs = now + (long)_frameMs;
        }

        var delay = _nextMs - now;
        if (delay > 0)
        {
            await Task.Delay((int)delay, ct).ConfigureAwait(false);
        }

        now = _sw.ElapsedMilliseconds;
        _nextMs += (long)_frameMs;
        if (now - _nextMs > _frameMs)
        {
            _nextMs = now + (long)_frameMs;
        }
    }
}
