using System.Collections.Concurrent;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class MatPool
{
    private readonly ConcurrentQueue<Mat> _pool = new();
    private readonly int _max;
    private int _count;

    public MatPool(int max)
    {
        _max = Math.Max(1, max);
    }

    public Mat Acquire()
    {
        if (_pool.TryDequeue(out var mat))
        {
            Interlocked.Decrement(ref _count);
            return mat;
        }

        return new Mat();
    }

    public void Release(Mat mat)
    {
        if (mat == null)
        {
            return;
        }

        if (Interlocked.Increment(ref _count) <= _max)
        {
            _pool.Enqueue(mat);
            return;
        }

        Interlocked.Decrement(ref _count);
        mat.Dispose();
    }

    public void Clear()
    {
        while (_pool.TryDequeue(out var mat))
        {
            mat.Dispose();
        }

        Interlocked.Exchange(ref _count, 0);
    }
}
