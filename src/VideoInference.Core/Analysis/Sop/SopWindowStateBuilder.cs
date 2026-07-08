using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public static class SopWindowStateBuilder
{
    public static SopWindowState Build(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frameWindow,
        int windowMs,
        int minScoreQ1000)
    {
        var minPts = current.PtsMs - Math.Max(1, windowMs);
        var minScore = Math.Clamp(minScoreQ1000, 0, 1000) / 1000f;
        var frames = frameWindow
            .Where(frame => frame.PtsMs >= minPts && frame.PtsMs <= current.PtsMs)
            .ToList();

        if (frames.Count == 0)
        {
            frames.Add(current);
        }

        var accumulators = new Dictionary<int, SopObjectAccumulator>();
        foreach (var frame in frames)
        {
            var presentInFrame = new HashSet<int>();
            foreach (var detection in frame.Detections ?? Array.Empty<DetectionEntity>())
            {
                if (detection.Score < minScore)
                {
                    continue;
                }

                if (!accumulators.TryGetValue(detection.ClassId, out var accumulator))
                {
                    accumulator = new SopObjectAccumulator(detection.ClassId);
                    accumulators[detection.ClassId] = accumulator;
                }

                accumulator.Add(detection);
                presentInFrame.Add(detection.ClassId);
            }

            foreach (var classId in presentInFrame)
            {
                accumulators[classId].PresentFrameCount++;
            }
        }

        var objects = accumulators.Values
            .OrderBy(item => item.ClassId)
            .Select(item => item.ToState(frames.Count))
            .ToList();

        return new SopWindowState(
            current.SourceKey,
            current.TaskId,
            current.TaskKind,
            frames[0].PtsMs,
            frames[^1].PtsMs,
            objects);
    }

    private sealed class SopObjectAccumulator
    {
        private string _bestLabel = string.Empty;
        private float _bestScore = -1;
        private SopBoundingBox? _bestBox;
        private readonly List<SopObjectInstance> _instances = new();

        public SopObjectAccumulator(int classId)
        {
            ClassId = classId;
        }

        public int ClassId { get; }
        public int TotalCount { get; private set; }
        public int PresentFrameCount { get; set; }

        public void Add(DetectionEntity detection)
        {
            TotalCount++;
            var label = string.IsNullOrWhiteSpace(detection.ClassName)
                ? $"class:{detection.ClassId}"
                : detection.ClassName.Trim();
            var box = new SopBoundingBox(detection.X1, detection.Y1, detection.X2, detection.Y2);
            _instances.Add(new SopObjectInstance(detection.ClassId, label, detection.Score, box));

            if (detection.Score <= _bestScore)
            {
                return;
            }

            _bestScore = detection.Score;
            _bestLabel = label;
            _bestBox = box;
        }

        public SopObjectWindowState ToState(int windowFrameCount)
        {
            return new SopObjectWindowState(
                ClassId,
                _bestLabel,
                TotalCount,
                PresentFrameCount,
                windowFrameCount,
                Math.Max(0, _bestScore),
                _bestBox,
                _instances.OrderByDescending(item => item.Score).ToArray());
        }
    }
}
