using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed class CompositeLegacyDetectionResultSink : ILegacyDetectionResultSink
{
    private readonly IReadOnlyList<ILegacyDetectionResultSink> _sinks;

    public CompositeLegacyDetectionResultSink(IReadOnlyList<ILegacyDetectionResultSink> sinks)
    {
        _sinks = sinks;
    }

    public bool TryEnqueue(FrameDetections batch)
    {
        var ok = true;
        foreach (var sink in _sinks)
        {
            ok = sink.TryEnqueue(batch) && ok;
        }

        return ok;
    }
}

public interface ITcnFeatureConsumer
{
    bool TryConsume(FrameEntity frame, float[] features);
}

public sealed class TcnFeatureFanoutLegacyDetectionSink : ILegacyDetectionResultSink
{
    private readonly ITcnFeaturePreprocessor _preprocessor;
    private readonly IReadOnlyList<ITcnFeatureConsumer> _consumers;

    public TcnFeatureFanoutLegacyDetectionSink(ITcnFeaturePreprocessor preprocessor, IReadOnlyList<ITcnFeatureConsumer> consumers)
    {
        _preprocessor = preprocessor;
        _consumers = consumers;
    }

    public bool TryEnqueue(FrameDetections batch)
    {
        if (!_preprocessor.TryBuild(batch, out var features))
        {
            return true;
        }

        var ok = true;
        foreach (var consumer in _consumers)
        {
            ok = consumer.TryConsume(batch.Frame, features) && ok;
        }

        return ok;
    }
}

public sealed class TcnFeatureWriterConsumer : ITcnFeatureConsumer
{
    private readonly TcnFeatureWriter _writer;
    private readonly TcnFeatureVersion _version;

    public TcnFeatureWriterConsumer(TcnFeatureWriter writer, TcnFeatureVersion version)
    {
        _writer = writer;
        _version = version;
    }

    public bool TryConsume(FrameEntity frame, float[] features)
    {
        var entry = new TcnFeatureEntry(
            frame.RunUuid,
            frame.RunStartedUtcMs,
            frame.SourceId,
            frame.FrameIndex,
            frame.TimestampMs,
            _version,
            features);

        return _writer.TryEnqueue(entry);
    }
}

public sealed class TcnInferenceConsumer : ITcnFeatureConsumer
{
    private readonly TcnOnnxInferenceEngine _engine;

    public TcnInferenceConsumer(TcnOnnxInferenceEngine engine)
    {
        _engine = engine;
    }

    public bool TryConsume(FrameEntity frame, float[] features)
    {
        return _engine.TryEnqueue(
            frame.RunUuid,
            frame.RunStartedUtcMs,
            frame.SourceId,
            frame.FrameIndex,
            frame.TimestampMs,
            features);
    }
}
