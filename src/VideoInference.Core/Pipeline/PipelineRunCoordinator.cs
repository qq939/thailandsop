using System.Collections.Generic;
using System.Threading;

namespace VideoInferenceDemo;

internal sealed class PipelineRunCoordinator : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task<bool>? _captureTask;
    private Task? _inferTask;
    private Task? _renderTask;

    public CancellationToken Start(
        string runUuid,
        Func<CancellationToken, bool> captureLoop,
        Func<CancellationToken, Task> inferLoop,
        Action<CancellationToken> renderLoop,
        Action completeFrameQueueAdding,
        Action completeRenderQueueAdding,
        Action<PipelineRunEnded> onRunEnded)
    {
        ArgumentNullException.ThrowIfNull(captureLoop);
        ArgumentNullException.ThrowIfNull(inferLoop);
        ArgumentNullException.ThrowIfNull(renderLoop);
        ArgumentNullException.ThrowIfNull(completeFrameQueueAdding);
        ArgumentNullException.ThrowIfNull(completeRenderQueueAdding);
        ArgumentNullException.ThrowIfNull(onRunEnded);

        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        var captureTask = Task.Run(() =>
        {
            try
            {
                return captureLoop(ct);
            }
            finally
            {
                completeFrameQueueAdding();
            }
        });

        var inferTask = Task.Run(async () =>
        {
            try
            {
                await inferLoop(ct).ConfigureAwait(false);
            }
            finally
            {
                completeRenderQueueAdding();
            }
        });

        var renderTask = Task.Run(() => renderLoop(ct));

        _captureTask = captureTask;
        _inferTask = inferTask;
        _renderTask = renderTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(captureTask, inferTask, renderTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                CameraDiagnostics.Error("pipeline", $"Pipeline run monitor failed: {ex.Message}");
            }

            var reason = PipelineRunEndReason.SourceError;
            if (ct.IsCancellationRequested)
            {
                reason = PipelineRunEndReason.Canceled;
            }
            else if (captureTask.Status == TaskStatus.RanToCompletion && captureTask.Result)
            {
                reason = PipelineRunEndReason.SourceEnded;
            }

            onRunEnded(new PipelineRunEnded(runUuid, reason));
        });

        return ct;
    }

    public bool Stop(Action beforeCancel, Action? beforeJoin = null)
    {
        ArgumentNullException.ThrowIfNull(beforeCancel);

        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null)
        {
            return false;
        }

        try
        {
            beforeCancel();
            cts.Cancel();
            beforeJoin?.Invoke();
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("pipeline", $"Pipeline stop cancel failed: {ex.Message}");
        }

        try
        {
            var tasks = new List<Task>(3);
            if (_captureTask != null)
            {
                tasks.Add(_captureTask);
            }

            if (_inferTask != null)
            {
                tasks.Add(_inferTask);
            }

            if (_renderTask != null)
            {
                tasks.Add(_renderTask);
            }

            if (tasks.Count > 0)
            {
                if (!Task.WaitAll(tasks.ToArray(), 2000))
                {
                    var pending = 0;
                    foreach (var t in tasks)
                    {
                        if (!t.IsCompleted) pending++;
                    }

                    CameraDiagnostics.Warn("pipeline", $"Pipeline stop timed out after 2000ms. {pending} task(s) still running.");
                }
            }
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("pipeline", $"Pipeline stop wait failed: {ex.Message}");
        }
        finally
        {
            _captureTask = null;
            _inferTask = null;
            _renderTask = null;
            cts.Dispose();
        }

        return true;
    }

    public void Dispose()
    {
        Stop(static () => { });
    }
}
