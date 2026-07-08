// 引入 System 命名空间，包含基本的类型和工具函数
using System;
// 引入 System.Collections.Generic 命名空间，包含泛型集合类型
using System.Collections.Generic;

// 定义命名空间，组织相关的代码
namespace VideoInferenceDemo;

// 定义一个只读的记录结构体，表示边界框（Bounding Box）
// record struct 是 C# 9.0 引入的值类型记录，具有值类型的特性和记录的便利性
// 包含四个 float 字段，表示矩形框的左上角 (X1,Y1) 和右下角 (X2,Y2) 坐标
public readonly record struct SopBoundingBox(float X1, float Y1, float X2, float Y2);

// 定义一个密封的记录类，表示检测到的对象实例
// sealed 表示该类不能被继承
public sealed record SopObjectInstance(
    int ClassId,          // 对象的类别 ID，用于标识是哪一类对象
    string Label,         // 对象的标签名称，如"产品"、"内盒"等
    float Score,          // 检测的置信度分数，范围通常是 0.0 到 1.0
    SopBoundingBox Box);  // 对象的边界框位置

// 定义一个密封的记录类，表示对象在滑动窗口内的状态
public sealed record SopObjectWindowState(
    int ClassId,                                    // 对象的类别 ID
    string Label,                                   // 对象的标签名称
    int TotalCount,                                 // 窗口内检测到该对象的总次数
    int PresentFrameCount,                          // 对象在多少帧中出现过
    int WindowFrameCount,                           // 滑动窗口包含的总帧数
    float BestScore,                                // 窗口内检测到的最高置信度分数
    SopBoundingBox? BestBox,                        // 窗口内检测到的最佳边界框
    IReadOnlyList<SopObjectInstance> Instances)     // 窗口内所有检测到的该对象实例的只读列表
{
    // 计算可见比例，结果乘以 1000（即 Q1000 表示千分之几）
    // => 是 C# 6.0 引入的表达式体成员语法，简洁地定义只读属性
    public int VisibleRatioQ1000 => WindowFrameCount <= 0
        ? 0                                                                 // 如果窗口没有帧，比例为 0
        : Math.Clamp(                                                      // Math.Clamp 将值限制在指定范围内
            (int)Math.Round(PresentFrameCount * 1000.0 / WindowFrameCount,  // 计算可见比例并四舍五入
                MidpointRounding.AwayFromZero),                             // 中点舍入方式：远离零
            0, 1000);                                                       // 限制在 0 到 1000 之间
}

// 定义一个密封的记录类，表示整个滑动窗口的状态
public sealed record SopWindowState(
    string SourceKey,                                  // 数据源的唯一标识
    string TaskId,                                     // 任务 ID
    VisionTaskKind TaskKind,                           // 任务类型（如目标检测、分类等）
    long StartPtsMs,                                   // 窗口的起始时间戳（毫秒）
    long EndPtsMs,                                     // 窗口的结束时间戳（毫秒）
    IReadOnlyList<SopObjectWindowState> Objects)       // 窗口内所有对象状态的只读列表
{
    // 静态方法，创建一个空的窗口状态
    // static 表示该方法属于类本身，不需要实例化即可调用
    public static SopWindowState Empty(FsmFrameMetrics current) => new(
        current.SourceKey,    // 使用当前帧的数据源标识
        current.TaskId,       // 使用当前帧的任务 ID
        current.TaskKind,     // 使用当前帧的任务类型
        current.PtsMs,        // 起始时间戳等于当前帧时间戳
        current.PtsMs,        // 结束时间戳等于当前帧时间戳
        Array.Empty<SopObjectWindowState>());  // 对象列表为空
}

// 定义一个密封的记录类，表示匹配到的 SOP 状态
public sealed record SopMatchedState(
    string StateCode,              // 状态代码，用于标识当前匹配到的状态
    string? Label,                 // 状态对应的标签，? 表示该字段可以为 null
    double? Score,                 // 状态匹配的分数
    string? Note = null,           // 附加说明，默认值为 null
    SopObjectWindowState? Object = null);  // 关联的对象状态，默认值为 null

// 定义一个密封的记录类，表示 SOP 规则评估时的上下文
public sealed record SopRuleContext(
    FsmFrameMetrics Current,       // 当前帧的度量数据
    AnalysisContext Analysis,      // 分析上下文，包含配置和状态
    SopWindowState Window);        // 滑动窗口的状态
