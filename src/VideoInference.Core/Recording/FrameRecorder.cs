using System;
using OpenCvSharp;

namespace VideoInferenceDemo;

public interface IFrameRecorder : IDisposable
{
    event Action<string>? Error;

    long DroppedCount { get; }

    void Start();
    void Stop();
    bool TryEnqueue(Mat frame, long ptsMs, long captureUtcMs);
    void RequestRotate(string? reason = null);
}
