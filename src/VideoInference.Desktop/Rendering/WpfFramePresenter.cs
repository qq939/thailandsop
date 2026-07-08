using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoInferenceDemo;

public sealed class WpfFramePresenter
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Action<ImageSource, string?> _applyFrame;
    private WriteableBitmap? _bitmap;
    private int _renderPending;
    private long _lastFrameInfoMs;

    /// <summary>
    /// 管线来源：camera → OpenCV Mat(CV_8UC3,BGR) → FramePacket → RenderPacket(byte[] PixelBuffer)。
    /// 整条链路在推理/画框阶段直接操作 Mat 避免额外拷贝，仅在最终输出时做一次
    /// Marshal.Copy(Mat→byte[]) + WritePixels(byte[]→GPU)，是 BGR24 裸字节路径中的最优方案。
    /// 若上游传 Bitmap 反而需要推理侧 Bitmap→Mat 转回来，徒增拷贝和 GDI 线程封送开销。
    /// </summary>
    public WpfFramePresenter(IUiDispatcher uiDispatcher, Action<ImageSource, string?> applyFrame)
    {
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _applyFrame = applyFrame ?? throw new ArgumentNullException(nameof(applyFrame));
    }

    /// <summary>
    /// 提交一帧用于显示。
    /// 通过 Interlocked.Exchange 实现丢帧机制：若上一帧仍在 UI 线程排队，新帧直接丢弃，
    /// 确保渲染线程始终追赶最新帧，避免 WPF 调度队列堆积。
    /// WriteableBitmap.WritePixels 接收 Bgr24 byte[] 可直接经 DMA 上传 GPU，无需转换。
    /// </summary>
    public void Present(RenderPacket packet)
    {
        if (packet == null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        // 丢帧：_renderPending=1 表示上一帧尚未完成 UI 线程调度，丢弃当前帧
        if (Interlocked.Exchange(ref _renderPending, 1) == 1)
        {
            packet.Dispose();
            return;
        }

        _uiDispatcher.Render(() =>
        {
            try
            {
                EnsureBitmap(packet.Width, packet.Height);
                _bitmap!.WritePixels(
                    new Int32Rect(0, 0, packet.Width, packet.Height),
                    packet.PixelBuffer,
                    packet.Stride,
                    0);

                // 帧率（叠）信息显示节流：最快 200ms 更新一次，避免 OS 文本开销挤占渲染时间
                string? frameInfo = null;
                var now = Environment.TickCount64;
                if (now - _lastFrameInfoMs >= 200)
                {
                    _lastFrameInfoMs = now;
                    frameInfo = $"Frame #{packet.Sequence} @ {packet.TimelineMs} ms";
                }

                _applyFrame(_bitmap, frameInfo);
            }
            finally
            {
                packet.Dispose();
                Interlocked.Exchange(ref _renderPending, 0);
            }
        });
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap != null && _bitmap.PixelWidth == width && _bitmap.PixelHeight == height)
        {
            return;
        }

        // PixelFormats.Bgr24 直接对应管线内的 BGR24 裸字节格式，零转换开销
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
    }
}
