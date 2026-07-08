using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NewLife.Log;
using OpenCvSharp;

namespace VideoInferenceDemo;

/// <summary>
/// PP-OCRv4 英文识别模型封装。
/// 输入：ROI 区域（BGR 图像），输出：CTC 解码后的英文字母数字字符串。
/// 预处理将 ROI resize 到高=48，等比例缩放宽度后右 padding 到固定宽度。
/// 推理走 ONNX Runtime，与现有 YOLO 共享 OrtSessionFactory 和 TensorRT EP 配置，
/// 不依赖 PaddlePaddle。
/// </summary>
public sealed class OcrTextRecognizer : IDisposable
{
    private readonly OrtModelRuntime _runtime;
    private readonly string[] _chars;
    private readonly int _fixedWidth;
    private readonly int _targetHeight;
    private const int NumClasses = 97;

    /// <param name="modelPath">ONNX 模型文件路径</param>
    /// <param name="dictPath">字符集字典（一行一个字符，不含 CTC blank）</param>
    /// <param name="deviceKind">推理设备</param>
    /// <param name="fixedWidth">固定宽度，输入不足时右 padding</param>
    public string? ActiveDeviceLabel => _runtime.ActiveDeviceLabel;

    public OcrTextRecognizer(
        string modelPath,
        string dictPath,
        InferenceDeviceKind deviceKind,
        int fixedWidth,
        int fixedHeight)
    {
        var options = new OrtSessionFactoryOptions
        {
            DeviceKind = deviceKind,
            TensorRtFp16 = true,
            TensorRtEngineCache = true,
            TensorRtEngineCachePath = Path.Combine(
                AppContext.BaseDirectory, "trt-cache", "en-ppocrv4-rec")
        };

        _runtime = new OrtModelRuntime(modelPath, options, "x", "softmax_2.tmp_0");
        _fixedWidth = fixedWidth;
        _targetHeight = fixedHeight;

        var lines = File.ReadAllLines(dictPath);
        _chars = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            _chars[i] = lines[i].Trim();
    }

    /// <summary>对图像 ROI 区域执行文字识别。</summary>
    public string Recognize(Mat image, int roiX, int roiY, int roiW, int roiH)
    {
        if (image == null || image.Empty())
            return string.Empty;

        // 1. 裁切 ROI（边界保护）
        roiX = Math.Max(0, roiX);
        roiY = Math.Max(0, roiY);
        roiW = Math.Min(roiW, image.Width - roiX);
        roiH = Math.Min(roiH, image.Height - roiY);
        if (roiW <= 0 || roiH <= 0)
            return string.Empty;

        using var roi = image[new Rect(roiX, roiY, roiW, roiH)];

        // 2. resize 到固定高度，等比例缩放宽度（保持宽高比）
        var scale = (double)_targetHeight / roi.Height;
        var targetW = Math.Max(4, (int)(roi.Width * scale));
        using var resized = roi.Resize(new Size(targetW, _targetHeight), 0, 0, InterpolationFlags.Linear);

        // 3. 统一到固定宽度，超出时 left-crop，不足时右 padding
        Mat padded;
        if (targetW > _fixedWidth)
        {
            // 等比例缩放后宽度超出模型输入，取左侧区域
            padded = resized[new Rect(0, 0, _fixedWidth, _targetHeight)].Clone();
        }
        else if (targetW < _fixedWidth)
        {
            padded = new Mat(_targetHeight, _fixedWidth, MatType.CV_8UC3, Scalar.All(0));
            resized.CopyTo(padded[new Rect(0, 0, targetW, _targetHeight)]);
        }
        else
        {
            padded = resized.Clone();
        }

        // 4. HWC → NCHW float32 [0,1]
        using var floatImg = new Mat();
        padded.ConvertTo(floatImg, MatType.CV_32FC3, 1.0 / 255.0);
        var tensor = HwcToNchw(floatImg);

        // 5. 推理
        XTrace.WriteLine("[OCR] Infer tensor: [{0},{1},{2},{3}] NCHW, image {4}x{5}, roi ({6},{7},{8},{9})",
            tensor.Dimensions[0], tensor.Dimensions[1], tensor.Dimensions[2], tensor.Dimensions[3],
            image.Width, image.Height, roiX, roiY, roiW, roiH);
        var input = new ModelInput(new[] { NamedOnnxValue.CreateFromTensor(_runtime.InputName, tensor) });
        using var output = _runtime.Run(input);
        var probs = output.Outputs[0].AsTensor<float>();

        // 6. CTC decode
        return CtcDecode(probs);
    }

    private static Tensor<float> HwcToNchw(Mat floatImg)
    {
        var h = floatImg.Rows;
        var w = floatImg.Width;
        var data = new float[3 * h * w];
        unsafe
        {
            var ptr = (float*)floatImg.Data;
            for (var row = 0; row < h; row++)
            {
                var rowStart = ptr + row * w * 3;
                for (var col = 0; col < w; col++)
                {
                    data[0 * h * w + row * w + col] = rowStart[col * 3];     // B
                    data[1 * h * w + row * w + col] = rowStart[col * 3 + 1]; // G
                    data[2 * h * w + row * w + col] = rowStart[col * 3 + 2]; // R
                }
            }
        }

        return new DenseTensor<float>(data, new[] { 1, 3, h, w });
    }

    private string CtcDecode(Tensor<float> probs)
    {
        // probs shape: [1, seq_len, 97]
        var seqLen = (int)(probs.Length / NumClasses);
        var result = new List<char>(seqLen);
        var prev = 0; // blank label
        for (var t = 0; t < seqLen; t++)
        {
            var maxIdx = 0;
            var maxVal = float.MinValue;
            for (var c = 0; c < NumClasses; c++)
            {
                var val = probs[0, t, c];
                if (val > maxVal)
                {
                    maxVal = val;
                    maxIdx = c;
                }
            }

            // CTC collapse: skip blank(0) and consecutive repeats
            if (maxIdx != 0 && maxIdx != prev)
            {
                var charIdx = maxIdx - 1;
                if (charIdx < _chars.Length)
                    result.Add(_chars[charIdx][0]);
            }

            prev = maxIdx;
        }

        return new string(result.ToArray());
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }
}
