using System.Collections.Concurrent;
using System.Threading;

namespace VideoInferenceDemo;

internal sealed class PipelinePacketQueueCoordinator
{
    private BlockingCollection<FramePacket>? _frameQueue;
    private BlockingCollection<RenderPacket>? _renderQueue;

    public int FrameQueueCount => _frameQueue?.Count ?? 0;

    public int RenderQueueCount => _renderQueue?.Count ?? 0;

    public void Start()
    {
        Stop();

        _frameQueue = new BlockingCollection<FramePacket>(new ConcurrentQueue<FramePacket>(), 3);
        _renderQueue = new BlockingCollection<RenderPacket>(new ConcurrentQueue<RenderPacket>(), 3);
    }

    public void Stop()
    {
        DrainQueue(_frameQueue);
        DrainQueue(_renderQueue);

        _frameQueue?.Dispose();
        _renderQueue?.Dispose();
        _frameQueue = null;
        _renderQueue = null;
    }

    public void CompleteFrameAdding()
    {
        TryCompleteAdding(_frameQueue);
    }

    public void CompleteRenderAdding()
    {
        TryCompleteAdding(_renderQueue);
    }

    public bool TryEnqueueCapturedFrameOrdered(FramePacket packet, CancellationToken ct)
    {
        return TryEnqueueOrdered(_frameQueue, packet, ct);
    }

    public void EnqueueCapturedFrameDropOldest(FramePacket packet, Action onDrop)
    {
        ArgumentNullException.ThrowIfNull(onDrop);
        EnqueueDropOldest(_frameQueue, packet, static item => item.Dispose(), onDrop);
    }

    public bool TryTakeFrame(bool useLossyQueue, out FramePacket? packet, Action onDrop, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onDrop);
        return useLossyQueue
            ? TryTakeLatest(_frameQueue, out packet, static item => item.Dispose(), onDrop, ct)
            : TryTakeOrdered(_frameQueue, out packet, ct);
    }

    public bool IsFrameQueueCompleted => _frameQueue?.IsCompleted ?? true;

    public bool TryEnqueueRender(bool useLossyQueue, RenderPacket packet, Action onDrop, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onDrop);
        if (useLossyQueue)
        {
            EnqueueDropOldest(_renderQueue, packet, static item => item.Dispose(), onDrop);
            return true;
        }

        return TryEnqueueOrdered(_renderQueue, packet, ct);
    }

    public bool TryTakeRender(bool useLossyQueue, out RenderPacket? packet, Action onDrop, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onDrop);
        return useLossyQueue
            ? TryTakeLatest(_renderQueue, out packet, static item => item.Dispose(), onDrop, ct)
            : TryTakeOrdered(_renderQueue, out packet, ct);
    }

    public bool IsRenderQueueCompleted => _renderQueue?.IsCompleted ?? true;

    private static void DrainQueue<T>(BlockingCollection<T>? queue) where T : IDisposable
    {
        if (queue == null)
        {
            return;
        }

        while (queue.TryTake(out var item))
        {
            item.Dispose();
        }
    }

    private static void TryCompleteAdding<T>(BlockingCollection<T>? queue)
    {
        if (queue == null)
        {
            return;
        }

        try
        {
            queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool TryTakeOrdered<T>(BlockingCollection<T>? queue, out T? item, CancellationToken ct, int waitMs = 20)
    {
        item = default;
        return queue != null && queue.TryTake(out item, waitMs, ct);
    }

    private static bool TryDrainLatest<T>(
        BlockingCollection<T> queue,
        out T latest,
        Action<T> dispose,
        Action onDrop)
    {
        latest = default!;
        var found = false;

        while (queue.TryTake(out var item))
        {
            if (found)
            {
                dispose(latest);
                onDrop();
            }

            latest = item;
            found = true;
        }

        return found;
    }

    private static bool TryTakeLatest<T>(
        BlockingCollection<T>? queue,
        out T? latest,
        Action<T> dispose,
        Action onDrop,
        CancellationToken ct,
        int waitMs = 20)
    {
        latest = default;
        if (queue == null || !queue.TryTake(out var first, waitMs, ct))
        {
            return false;
        }

        latest = first;
        while (TryDrainLatest(queue, out var newer, dispose, onDrop))
        {
            dispose(latest);
            onDrop();
            latest = newer;
        }

        return true;
    }

    private static bool TryEnqueueOrdered<T>(BlockingCollection<T>? queue, T item, CancellationToken ct, int waitMs = 20)
    {
        if (queue == null)
        {
            return false;
        }

        while (!ct.IsCancellationRequested)
        {
            if (queue.IsAddingCompleted)
            {
                return false;
            }

            try
            {
                if (queue.TryAdd(item, waitMs, ct))
                {
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static void EnqueueDropOldest<T>(BlockingCollection<T>? queue, T item, Action<T> disposeOldest, Action onDrop)
    {
        if (queue == null)
        {
            return;
        }

        if (queue.TryAdd(item))
        {
            return;
        }

        if (queue.TryTake(out var oldest))
        {
            disposeOldest(oldest);
            onDrop();
        }

        queue.TryAdd(item);
    }
}
