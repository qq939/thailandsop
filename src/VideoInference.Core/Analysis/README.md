# Analysis 在线分析框架说明
> **本次更新总结**：简化了 SOP1-SOP4 和 SopRuleAnalysisStrategy 的步骤检测逻辑，每一步只需检测对应目标连续出现足够帧数；修复了 IsSopCycleReset 和 IsReset 的行为，现在只清理 UI 状态，不影响 OK 计数；优化了状态重置机制，最后一步完成后显示完整状态再重置，确保 OK 计数正确 +1。

这个目录负责把视觉任务输出的检测结果转换成 SOP/FSM 状态。它的设计目标是：框架稳定，项目规则集中；以后新项目只需要改少量规则代码，不需要重新理解整条推理、UI、数据库链路。

## 主体数据流

```text
VisionFrameResult
  -> LegacyDetectionCompatibilityVisionResultSink
  -> FrameDetections
  -> AnalysisEngine
  -> IAnalysisStrategy
  -> AnalysisResult
  -> UI SOP 步骤状态 / OK-NG
```

`AnalysisEngine` 是后台在线分析服务的主体。它接收每帧检测结果，维护最近 N 帧窗口，调用策略类输出当前步骤和状态。UI 不直接写判断逻辑，只消费 `AnalysisResult`。

## 顶层文件

### `AnalysisConfig.cs`

在线分析配置模型。

常用字段：

- `EnableOnlineAnalysis`：是否启用在线分析。
- `Strategy`：策略名称，当前推荐 `sop_rules`。
- `SourceTaskId`：只消费指定 task 的结果；多 task 并行时建议配置。
- `FrameWindowSize`：引擎保留的最近帧数。
- `StateWindowSize`：保留的最近分析结果数。
- `HoldFrames`：条件短暂不满足时继续保持当前步骤的帧数。
- `SopWindowMs`：SOP 规则使用的最近时间窗口。
- `SopMinScoreQ1000`：检测置信度阈值，千分制。
- `SopMinVisibleRatioQ1000`：目标在窗口内稳定出现比例，千分制。

### `AnalysisConfigStorage.cs`

旧版独立 `analysis_config.json` 的读写工具。现在主配置已经合并到 `AppConfig`，新逻辑优先走 `AppConfigStorage`。

### `AnalysisModels.cs`

在线分析核心数据对象。

重要类型：

- `FsmFrameMetrics`：策略输入。包含 run/source/task/frame 信息、检测结果和基础特征。
- `AnalysisState`：策略跨帧状态，例如当前步骤、hold 计数。
- `AnalysisResult`：策略输出。包含当前 step、状态码、NG 原因、转移信息。
- `AnalysisContext`：策略上下文，包含配置、步骤定义、历史结果和可变状态。

### `AnalysisEngine.cs`

在线分析引擎。

职责：

- 将 `FrameDetections` 转成 `FsmFrameMetrics`。
- 按 run 自动 reset 窗口和状态。
- 维护最近帧窗口和最近结果窗口。
- 调用 `IAnalysisStrategy`。
- 统一补充步骤转移信息：
  - `IsTransition`
  - `IsReset`
  - `TransitionOk`
  - `FromStep`
  - `ToStep`

一般不要在这里写项目业务条件。项目条件应写在 `Sop/SopProjectRules.cs`。

### `AnalysisStrategyFactory.cs`

根据配置选择策略。

当前规则：

- `basic_distance` -> `BasicDistanceAnalysisStrategy`
- 其它或默认 -> `SopRuleAnalysisStrategy`

### `AnalysisStrategyNames.cs`

策略名称常量，避免字符串散落。

当前：

- `AnalysisStrategyNames.BasicDistance`
- `AnalysisStrategyNames.SopRules`

## SOP 子目录

`Sop` 子目录是当前推荐的规则式 SOP 分析实现。它被拆成“通用外壳 + 项目规则 + 条件工具”。

```text
SopRuleAnalysisStrategy.cs   通用外壳
SopProjectRules.cs           项目规则入口
SopConditionHelpers.cs       条件工具函数
SopWindowStateBuilder.cs     最近 N 秒窗口汇总
SopModels.cs                 SOP 状态模型
```

### `SopRuleAnalysisStrategy.cs`

通用 SOP 策略外壳。

它负责：

- 按 `SourceTaskId` 过滤任务结果。
- 构建最近 `SopWindowMs` 的窗口状态。
- 调用 `SopProjectRules.Match(...)` 获取当前满足的状态。
- 按 SOP 步骤顺序推进当前 step。
- 处理 hold、跳步、NG 原因。

不要把项目专用条件直接写在这里。这里应该保持通用。

### `SopProjectRules.cs`

项目规则唯一推荐入口。

以后新项目主要改这个函数：

```csharp
public static IEnumerable<SopMatchedState> Match(SopRuleContext context)
```

这里可以根据：

- `context.Window.SourceKey`：相机/视频来源。
- `context.Window.TaskId`：当前 task。
- `context.Window.TaskKind`：任务类型。
- `context.Window.Objects`：窗口内目标状态。
- `context.Analysis.Config`：阈值配置。
- `context.Analysis.Steps`：SOP 步骤配置。

写 switch/case 或 if 分支。

示例：

```csharp
if (context.Window.SourceKey == "camera:1")
{
    // 写相机 1 的条件
}

if (context.Window.TaskId == "yolo11m-sop3-detect")
{
    // 写指定模型任务的条件
}
```

当前示例规则：

- 稳定出现某类别时，自动输出类别状态，例如 `内盒`、`产品`、`class:0`。
- `product_in_inner_box`：产品中心点在内盒框内。

### `SopConditionHelpers.cs`

条件工具函数。

已有常用函数：

- `TryGetObject(window, labelOrClassCode, out obj)`：按类别名或 `class:n` 取窗口对象。
- `IsStable(obj, minVisibleRatioQ1000)`：判断目标是否稳定出现。
- `IsInside(container, target, marginPx)`：目标框是否完全在容器框内。
- `CenterInside(container, target, marginPx)`：目标中心点是否在容器框内。
- `IntersectionOverTarget(container, target)`：目标框被容器框覆盖的比例。
- `StableClassStates(window, threshold)`：把稳定出现的类别直接输出为状态。

项目里出现五花八门的几何关系、组合条件、顺序记忆条件时，优先把可复用的小函数补到这里。

### `SopWindowStateBuilder.cs`

把最近 N 秒内的 `FsmFrameMetrics` 汇总成 `SopWindowState`。

它会按类别聚合：

- 出现次数。
- 出现帧数。
- 窗口帧数。
- 最佳置信度。
- 最佳框 `BestBox`。
- 所有候选实例 `Instances`。

如果同类有多个目标，不要只看 `BestBox`，可以遍历 `Instances`。

### `SopModels.cs`

SOP 规则用到的数据模型。

常用类型：

- `SopBoundingBox`
- `SopObjectInstance`
- `SopObjectWindowState`
- `SopWindowState`
- `SopMatchedState`
- `SopRuleContext`

## SOP 步骤与状态码

步骤顺序来自 SOP 步骤配置窗口，顺序就是默认执行序列。

每个步骤可以配置 `ExpectedStateCode`：

- 直接填类别名：`内盒`、`产品`、`充电器`
- 填 class id：`class:0`、`class:1`
- 填组合状态：`product_in_inner_box`
- 多个候选可用 `|`、`,`、`;` 分隔

如果不填 `ExpectedStateCode`，默认按步骤顺序映射：

```text
第 1 步 -> class:0
第 2 步 -> class:1
第 3 步 -> class:2
```

## 新项目如何写规则

通常只需要三步：

1. 在 SOP 步骤配置窗口配置步骤顺序和目标状态。
2. 在 `SopProjectRules.Match(...)` 中补项目状态条件。
3. 如果有复杂判断，把 helper 函数加到 `SopConditionHelpers.cs`。

推荐模式：

```csharp
public static IEnumerable<SopMatchedState> Match(SopRuleContext context)
{
    // 1. 保留基础类别出现状态
    foreach (var state in SopConditionHelpers.StableClassStates(
                 context.Window,
                 context.Analysis.Config.SopMinVisibleRatioQ1000))
    {
        yield return state;
    }

    // 2. 写项目组合条件
    if (SopConditionHelpers.TryGetObject(context.Window, "内盒", out var innerBox) &&
        SopConditionHelpers.TryGetObject(context.Window, "产品", out var product) &&
        innerBox.BestBox is { } inner &&
        product.BestBox is { } target &&
        SopConditionHelpers.CenterInside(inner, target, marginPx: 20))
    {
        yield return new SopMatchedState(
            "product_in_inner_box",
            "产品在内盒",
            product.BestScore,
            Object: product);
    }
}
```

## 多任务和多相机

### 多任务

结果链路现在会保留：

- `TaskId`
- `TaskKind`

如果一个相机/session 同时运行多个 task，建议在配置里指定：

```json
{
  "Analysis": {
    "SourceTaskId": "yolo11m-sop3-detect"
  }
}
```

这样 SOP 只消费指定 task 的结果。

### 多相机

当前每个 session 有自己的 `AnalysisEngine`。这是默认安全结构。

未来如果需要“相机 A 依赖相机 B 的结果”，不要把状态混写进 `SopRuleAnalysisStrategy`，建议增加一个共享上下文服务，由不同 session 的分析结果写入，再由项目规则读取。

## 不建议改的地方

一般情况下不要改：

- `AnalysisEngine` 的转移补充逻辑。
- `SopRuleAnalysisStrategy` 的步骤推进外壳。
- `SopWindowStateBuilder` 的基础聚合语义，除非窗口对象数据不够用。

优先改：

- `SopProjectRules.Match(...)`
- `SopConditionHelpers`
- SOP 步骤配置里的 `ExpectedStateCode`

