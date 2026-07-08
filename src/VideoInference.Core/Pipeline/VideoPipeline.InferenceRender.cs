using System;
using System.Threading;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace VideoInferenceDemo;

public sealed partial class VideoPipeline
{
    private Task InferLoop(CancellationToken ct)
    {
        return _inferenceRenderCoordinator.RunInferLoop(
            _sourceType,
            () => _targetFps,
            WaitIfPaused,
            ProcessFrameForRender,
            ct);
    }

    private void RenderLoop(CancellationToken ct)
    {
        _inferenceRenderCoordinator.RunRenderLoop(
            _sourceType,
            WaitIfPaused,
            () => FrameReady,
            () => StatsUpdated,
            ct);
    }

    private PipelineInferFrameResult ProcessFrameForRender(FramePacket packet)
    {
        var transferOwnership = false;
        if (!TryRunInference(packet))
        {
            DrawDemoBoxes(packet.Image, packet.Sequence);
        }

        return new PipelineInferFrameResult(ToRenderPacket(packet, out transferOwnership), transferOwnership);
    }

    private RenderPacket ToRenderPacket(FramePacket packet, out bool transferOwnership)
    {
        var input = packet.Image;
        Mat bgr = input;
        transferOwnership = true;
        if (input.Type() != MatType.CV_8UC3)
        {
            bgr = _matPool.Acquire();
            Cv2.CvtColor(input, bgr, ColorConversionCodes.BGRA2BGR);
            transferOwnership = false;
        }

        if (!bgr.IsContinuous())
        {
            var clone = _matPool.Acquire();
            bgr.CopyTo(clone);
            if (!ReferenceEquals(bgr, input))
            {
                _matPool.Release(bgr);
            }
            bgr = clone;
            transferOwnership = false;
        }

        if (transferOwnership && packet.TryTakeOwnership(out var releaseInput) && releaseInput != null)
        {
            return new RenderPacket(bgr, packet.TimelineMs, packet.Sequence, releaseInput);
        }

        return new RenderPacket(bgr, packet.TimelineMs, packet.Sequence, _matPool.Release);
    }

    private static void DrawDemoBoxes(Mat image, int sequence)
    {
        var width = image.Width;
        var height = image.Height;
        var boxWidth = Math.Max(40, width / 5);
        var boxHeight = Math.Max(40, height / 5);
        var x = Math.Abs(sequence * 7) % Math.Max(1, width - boxWidth);
        var y = Math.Abs(sequence * 5) % Math.Max(1, height - boxHeight);

        Cv2.Rectangle(image, new CvRect(x, y, boxWidth, boxHeight), new Scalar(0, 255, 0), 2);
        Cv2.PutText(image, $"#{sequence}", new CvPoint(x + 6, y + 24), HersheyFonts.HersheySimplex, 0.7,
            new Scalar(0, 255, 255), 2);
    }

    private bool TryRunInference(FramePacket packet)
    {
        var taskSnapshot = _taskRuntime.GetExecutionSnapshot();
        if (taskSnapshot == null)
        {
            return false;
        }

        var request = _frameExecutionRequestFactory.Create(
            packet,
            taskSnapshot,
            _sourceId,
            _sourceType,
            _runUuid,
            Interlocked.Read(ref _runStartedUtcMs));
        return _taskExecutionCoordinator.TryExecuteFrame(request);
    }

    private void EmitDeviceChanged(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        DeviceChanged?.Invoke(label);
    }

    private PipelineDrawStyle GetDrawStyleSnapshot()
    {
        return _drawStyle.GetSnapshot();
    }
}
