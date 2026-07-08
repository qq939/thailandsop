using System;
using System.Diagnostics;
using System.Threading;

namespace VideoInferenceDemo;

internal sealed class PipelinePlaybackState : IDisposable
{
    private readonly ManualResetEventSlim _pauseGate = new(true);
    private readonly object _clockLock = new();
    private Stopwatch? _playbackClock;
    private long _pausedPlaybackMs;
    private long _pauseStartedPlaybackMs = -1;
    private int _isPaused;
    private VideoSourceType _sourceType;

    public void Start(VideoSourceType sourceType)
    {
        _sourceType = sourceType;
        _pauseGate.Set();
        Interlocked.Exchange(ref _isPaused, 0);
        lock (_clockLock)
        {
            _playbackClock = Stopwatch.StartNew();
            _pausedPlaybackMs = 0;
            _pauseStartedPlaybackMs = -1;
        }
    }

    public void Pause()
    {
        if (_sourceType != VideoSourceType.VideoFile || Interlocked.CompareExchange(ref _isPaused, 1, 0) != 0)
        {
            return;
        }

        lock (_clockLock)
        {
            _pauseStartedPlaybackMs = _playbackClock?.ElapsedMilliseconds ?? 0;
        }

        _pauseGate.Reset();
    }

    public void Resume()
    {
        if (Interlocked.Exchange(ref _isPaused, 0) == 0)
        {
            _pauseGate.Set();
            return;
        }

        lock (_clockLock)
        {
            if (_playbackClock != null && _pauseStartedPlaybackMs >= 0)
            {
                _pausedPlaybackMs += _playbackClock.ElapsedMilliseconds - _pauseStartedPlaybackMs;
            }

            _pauseStartedPlaybackMs = -1;
        }

        _pauseGate.Set();
    }

    public void WaitIfPaused(CancellationToken cancellationToken)
    {
        _pauseGate.Wait(cancellationToken);
    }

    public void PaceToPts(double targetPtsMs, CancellationToken cancellationToken)
    {
        if (targetPtsMs <= 0)
        {
            return;
        }

        while (true)
        {
            WaitIfPaused(cancellationToken);
            var delayMs = (int)Math.Round(targetPtsMs - GetPlaybackElapsedMilliseconds());
            if (delayMs <= 0)
            {
                return;
            }

            var waitMs = Math.Min(delayMs, 50);
            if (cancellationToken.WaitHandle.WaitOne(waitMs))
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _pauseGate.Dispose();
    }

    private long GetPlaybackElapsedMilliseconds()
    {
        lock (_clockLock)
        {
            if (_playbackClock == null)
            {
                return 0;
            }

            var rawElapsed = _playbackClock.ElapsedMilliseconds;
            if (Volatile.Read(ref _isPaused) != 0 && _pauseStartedPlaybackMs >= 0)
            {
                rawElapsed = _pauseStartedPlaybackMs;
            }

            var effectiveElapsed = rawElapsed - _pausedPlaybackMs;
            return effectiveElapsed > 0 ? effectiveElapsed : 0;
        }
    }
}
