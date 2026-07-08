using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoInferenceDemo;

public sealed class TcnPrediction
{
    public TcnPrediction(
        string runUuid,
        long runStartedUtcMs,
        string label,
        float score,
        int classId,
        string sourceKey,
        int frameIndex,
        long ptsMs)
    {
        RunUuid = runUuid;
        RunStartedUtcMs = runStartedUtcMs;
        Label = label;
        Score = score;
        ClassId = classId;
        SourceKey = sourceKey;
        FrameIndex = frameIndex;
        PtsMs = ptsMs;
    }

    public string RunUuid { get; }
    public long RunStartedUtcMs { get; }
    public string Label { get; }
    public float Score { get; }
    public int ClassId { get; }
    public string SourceKey { get; }
    public int FrameIndex { get; }
    public long PtsMs { get; }
}

public sealed class TcnFeatureFrame
{
    public TcnFeatureFrame(string runUuid, long runStartedUtcMs, string sourceKey, int frameIndex, long ptsMs, float[] features)
    {
        RunUuid = runUuid ?? string.Empty;
        RunStartedUtcMs = runStartedUtcMs;
        SourceKey = sourceKey ?? string.Empty;
        FrameIndex = frameIndex;
        PtsMs = ptsMs;
        Features = features ?? Array.Empty<float>();
    }

    public string RunUuid { get; }
    public long RunStartedUtcMs { get; }
    public string SourceKey { get; }
    public int FrameIndex { get; }
    public long PtsMs { get; }
    public float[] Features { get; }
}

public sealed class TcnOnnxInferenceEngine : ITcnPredictionProvider, IDisposable
{
    private readonly TcnInferenceConfig _config;
    private readonly int _featureDim;
    private readonly string[]? _classNames;
    private readonly BlockingCollection<TcnFeatureFrame> _queue;
    private readonly float[][] _window;
    private int _windowCount;
    private int _windowIndex;
    private int _strideCounter;
    private OrtModelRuntime? _runtime;
    private TcnFeatureWindowPreprocessor? _preprocessor;
    private TcnPredictionPostprocessor? _postprocessor;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private readonly object _latestLock = new();
    private TcnPrediction? _latest;

    public TcnOnnxInferenceEngine(TcnInferenceConfig config, int featureDim)
    {
        _config = config;
        _featureDim = featureDim;
        _classNames = LoadClassNames(config.ClassesPath);
        _queue = new BlockingCollection<TcnFeatureFrame>(new ConcurrentQueue<TcnFeatureFrame>(), Math.Max(8, config.QueueCapacity));
        _window = new float[_config.WindowSize][];
        for (var i = 0; i < _window.Length; i++)
        {
            _window[i] = new float[_featureDim];
        }
    }

    public event Action<TcnPrediction>? PredictionReady;

    public void Start()
    {
        if (_worker != null)
        {
            return;
        }

        _runtime = CreateRuntime();
        _preprocessor = new TcnFeatureWindowPreprocessor(_runtime.InputName, _featureDim, _config.WindowSize);
        _postprocessor = new TcnPredictionPostprocessor(_runtime.OutputName, _config.ApplySoftmax, _classNames);
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
        }

        try
        {
            _queue.CompleteAdding();
        }
        catch
        {
        }

        try
        {
            _worker?.Wait(2000);
        }
        catch
        {
        }

        _worker = null;
    }

    public void Dispose()
    {
        Stop();
        _runtime?.Dispose();
        _queue.Dispose();
    }

    public bool TryEnqueue(string runUuid, long runStartedUtcMs, string sourceKey, int frameIndex, long ptsMs, float[] features)
    {
        if (_queue.IsAddingCompleted)
        {
            return false;
        }

        if (features.Length != _featureDim)
        {
            return false;
        }

        return _queue.TryAdd(new TcnFeatureFrame(runUuid, runStartedUtcMs, sourceKey, frameIndex, ptsMs, features));
    }

    public bool TryGetCurrent(out string label, out float score)
    {
        lock (_latestLock)
        {
            if (_latest == null)
            {
                label = string.Empty;
                score = 0;
                return false;
            }

            label = _latest.Label;
            score = _latest.Score;
            return true;
        }
    }

    private void RunAsync(CancellationToken ct)
    {
        if (_runtime == null || _preprocessor == null || _postprocessor == null)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested || _queue.Count > 0)
            {
                if (!_queue.TryTake(out var item, 50, ct))
                {
                    continue;
                }

                if (item.Features.Length != _featureDim)
                {
                    continue;
                }

                PushToWindow(item.Features);
                if (_windowCount < _config.WindowSize)
                {
                    continue;
                }

                _strideCounter++;
                if (_strideCounter < _config.Stride)
                {
                    continue;
                }

                _strideCounter = 0;
                var prediction = RunInference(item);
                if (prediction == null)
                {
                    continue;
                }

                lock (_latestLock)
                {
                    _latest = prediction;
                }

                PredictionReady?.Invoke(prediction);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PushToWindow(float[] features)
    {
        var target = _window[_windowIndex];
        Array.Copy(features, target, _featureDim);
        _windowIndex = (_windowIndex + 1) % _config.WindowSize;
        if (_windowCount < _config.WindowSize)
        {
            _windowCount++;
        }
    }

    private TcnPrediction? RunInference(TcnFeatureFrame frame)
    {
        if (_runtime == null || _preprocessor == null || _postprocessor == null)
        {
            return null;
        }

        var input = _preprocessor.Preprocess(new TcnInferenceRequest(frame, _window, _windowCount, _windowIndex), out var context);
        using var output = _runtime.Run(input);
        return _postprocessor.Process(output, context);
    }

    private OrtModelRuntime CreateRuntime()
    {
        if (string.IsNullOrWhiteSpace(_config.ModelPath) || !File.Exists(_config.ModelPath))
        {
            throw new FileNotFoundException("TCN model not found.", _config.ModelPath);
        }

        var providerOrder = OrtExecutionProviderParser.ParseMany(_config.OrtProviderOrder);
        var options = new OrtSessionFactoryOptions
        {
            DeviceKind = providerOrder.Count > 0 ? InferenceDeviceKind.GpuCuda : InferenceDeviceKind.Cpu,
            ProviderOrder = providerOrder.Count > 0 ? providerOrder : new[] { OrtExecutionProviderKind.Cpu },
            NativeLibraryPath = _config.OrtNativeLibraryPath,
            DeviceId = Math.Max(0, _config.OrtDeviceId),
            TensorRtFp16 = _config.OrtTensorRtFp16,
            TensorRtEngineCache = _config.OrtTensorRtEngineCache,
            TensorRtEngineCachePath = _config.OrtTensorRtEngineCachePath,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };

        return new OrtModelRuntime(_config.ModelPath, options, _config.InputName, _config.OutputName);
    }

    private static string[]? LoadClassNames(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".json")
        {
            try
            {
                var json = File.ReadAllText(path);
                var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                return names;
            }
            catch
            {
                return null;
            }
        }

        var lines = File.ReadAllLines(path);
        return lines.Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();
    }
}
