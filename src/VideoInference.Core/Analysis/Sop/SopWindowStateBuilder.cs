// 引入 System 命名空间，包含基本类型和工具函数
using System;
// 引入 System.Collections.Generic 命名空间，包含泛型集合
using System.Collections.Generic;
// 引入 System.Linq 命名空间，包含 LINQ 查询操作
using System.Linq;

// 定义命名空间
namespace VideoInferenceDemo;

// 定义一个静态类，用于构建滑动窗口的状态
// static 表示该类不能被实例化，所有成员都是静态的
public static class SopWindowStateBuilder
{
    // 构建滑动窗口状态的静态方法
    public static SopWindowState Build(
        FsmFrameMetrics current,              // 当前帧的度量数据
        IReadOnlyList<FsmFrameMetrics> frameWindow,  // 帧窗口的只读列表
        int windowMs,                         // 窗口的时间长度（毫秒）
        int minScoreQ1000)                    // 最低置信度分数（千分比）
    {
        // 计算窗口的最小时间戳
        var minPts = current.PtsMs - Math.Max(1, windowMs);
        // 将千分比分数转换为 0.0 到 1.0 范围
        var minScore = Math.Clamp(minScoreQ1000, 0, 1000) / 1000f;
        // 使用 LINQ Where 过滤出时间范围内的帧
        var frames = frameWindow
            .Where(frame => frame.PtsMs >= minPts && frame.PtsMs <= current.PtsMs)
            .ToList();  // ToList 将结果转换为 List<T> 类型

        // 如果过滤后没有帧，添加当前帧
        if (frames.Count == 0)
        {
            frames.Add(current);  // List<T>.Add 方法向列表添加元素
        }

        // 创建字典用于累积每个类别的对象信息
        // Dictionary<TKey, TValue> 是键值对集合
        var accumulators = new Dictionary<int, SopObjectAccumulator>();
        // 遍历每一帧
        foreach (var frame in frames)
        {
            // 创建 HashSet 记录当前帧出现的类别
            // HashSet<T> 是不包含重复元素的集合
            var presentInFrame = new HashSet<int>();
            // 遍历当前帧的所有检测结果
            // ?? 是空合并运算符，如果左边为 null 则使用右边
            foreach (var detection in frame.Detections ?? Array.Empty<DetectionEntity>())
            {
                // 如果检测分数低于阈值，跳过
                if (detection.Score < minScore)
                {
                    continue;  // continue 跳过本次循环剩余部分
                }

                // 尝试从字典中获取该类别的累加器
                // TryGetValue 尝试获取值，返回是否成功
                if (!accumulators.TryGetValue(detection.ClassId, out var accumulator))
                {
                    // 如果不存在，创建新的累加器
                    accumulator = new SopObjectAccumulator(detection.ClassId);
                    accumulators[detection.ClassId] = accumulator;
                }

                // 将检测结果添加到累加器
                accumulator.Add(detection);
                // 记录该类别在当前帧出现过
                presentInFrame.Add(detection.ClassId);
            }

            // 遍历当前帧出现的所有类别
            foreach (var classId in presentInFrame)
            {
                // 增加该类别的出现帧数
                accumulators[classId].PresentFrameCount++;
            }
        }

        // 使用 LINQ 将累加器转换为对象状态列表
        var objects = accumulators.Values
            .OrderBy(item => item.ClassId)  // 按类别 ID 排序
            .Select(item => item.ToState(frames.Count))  // 转换为状态对象
            .ToList();

        // 返回构建好的窗口状态
        return new SopWindowState(
            current.SourceKey,        // 数据源标识
            current.TaskId,           // 任务 ID
            current.TaskKind,         // 任务类型
            frames[0].PtsMs,          // 窗口第一帧的时间戳
            frames[^1].PtsMs,         // 窗口最后一帧的时间戳（^1 是索引器，表示倒数第一）
            objects);                 // 对象状态列表
    }

    // 私有密封类，用于累加单个类别的检测信息
    private sealed class SopObjectAccumulator
    {
        // 私有字段，存储最佳标签
        private string _bestLabel = string.Empty;
        // 私有字段，存储最佳分数
        private float _bestScore = -1;
        // 私有字段，存储最佳边界框
        private SopBoundingBox? _bestBox;
        // 私有只读字段，存储所有实例列表
        // readonly 表示字段只能在构造函数中赋值
        private readonly List<SopObjectInstance> _instances = new();

        // 构造函数，初始化累加器
        public SopObjectAccumulator(int classId)
        {
            ClassId = classId;
        }

        // 公共属性，获取类别 ID（只读）
        public int ClassId { get; }
        // 公共属性，获取总检测次数（私有 setter）
        public int TotalCount { get; private set; }
        // 公共属性，获取出现帧数（可读写）
        public int PresentFrameCount { get; set; }

        // 添加一个检测结果的方法
        public void Add(DetectionEntity detection)
        {
            TotalCount++;  // 总次数加 1
            // 获取标签名称，如果没有则使用类别 ID
            var label = string.IsNullOrWhiteSpace(detection.ClassName)
                ? $"class:{detection.ClassId}"  // 字符串插值
                : detection.ClassName.Trim();
            // 创建边界框对象
            var box = new SopBoundingBox(detection.X1, detection.Y1, detection.X2, detection.Y2);
            // 创建实例并添加到列表
            _instances.Add(new SopObjectInstance(detection.ClassId, label, detection.Score, box));

            // 如果当前分数不大于最佳分数，直接返回
            if (detection.Score <= _bestScore)
            {
                return;  // return 提前退出方法
            }

            // 更新最佳分数、标签和边界框
            _bestScore = detection.Score;
            _bestLabel = label;
            _bestBox = box;
        }

        // 转换为窗口状态对象的方法
        public SopObjectWindowState ToState(int windowFrameCount)
        {
            return new SopObjectWindowState(
                ClassId,                                  // 类别 ID
                _bestLabel,                               // 最佳标签
                TotalCount,                               // 总检测次数
                PresentFrameCount,                        // 出现帧数
                windowFrameCount,                         // 窗口总帧数
                Math.Max(0, _bestScore),                  // 最佳分数（不小于 0）
                _bestBox,                                 // 最佳边界框
                _instances.OrderByDescending(item => item.Score).ToArray());  // 按分数降序排列的实例数组
        }
    }
}
