# SOP 规则式在线分析接入计划

本文档整理当前项目中 SOP/FSM 链路的现状，并给出在数据量不足、暂时无法训练 TCN 的情况下，如何用最近 N 秒目标检测结果写规则策略来判断当前 SOP 状态、做目标状态校验并向 UI 报 NG。

## 目标

当前 SOP 检测模型已经基本可用，类别顺序为：

```text
0 内盒
1 圆片
2 产品
3 充电器
4 外盒
5 保修卡
```

短期目标不是训练 TCN，而是：

1. 持续接收 YOLO 检测结果。
2. 基于最近 N 秒窗口内的目标出现、置信度、位置关系、持续时间，推断当前 SOP 状态。
3. 每个 SOP 步骤配置一个或多个目标状态条件。
4. 当前状态与目标状态不匹配时，输出 NG。
5. 后台分析服务独立于 UI，UI 只消费状态、步骤和 NG 结果。

## 当前已有能力

### 检测结果链路

当前视觉结果会经过：

```text
IVisionTask.Execute
  -> VisionFrameResult
  -> LegacyDetectionCompatibilityVisionResultSink
  -> FrameDetections
  -> CompositeLegacyDetectionResultSink
```

其中 `CompositeLegacyDetectionResultSink` 已经会把同一帧检测结果分发给：

- `SqliteResultWriter`：写入数据库。
- `AnalysisEngine`：在线 SOP/FSM 分析。
- 可选 TCN feature/inference fanout：目前可继续保留，但不是本方案的依赖。

### 在线分析框架

核心文件：

- `src/VideoInference.Core/Analysis/AnalysisEngine.cs`
- `src/VideoInference.Core/Analysis/AnalysisModels.cs`
- `src/VideoInference.Core/Analysis/AnalysisConfig.cs`
- `src/VideoInference.Core/Workflow/FsmFeatureCalculator.cs`

已经存在关键扩展点：

```csharp
public interface IAnalysisStrategy
{
    AnalysisResult Analyze(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> window,
        AnalysisContext context);
}
```

这就是适合承载 SOP 规则的策略类入口。当前默认实现 `BasicDistanceAnalysisStrategy` 只是演示性质：用 class 0/1 到 class 2 的距离判断 near/far，然后映射到一个步骤。它不能覆盖当前 SOP 业务。

### UI 步骤配置

当前 SOP 步骤配置窗口：

- `src/VideoInference.Desktop/Workflow/FsmConfigWindow.xaml`
- `src/VideoInference.Desktop/Workflow/FsmConfigViewModel.cs`
- `src/VideoInference.Core/Workflow/FsmModels.cs`

目前 `FsmStepDefinition` 只有：

```csharp
public int Step { get; set; }
public string Name { get; set; }
public string? ActionCode { get; set; }
public string? TcnLabel { get; set; }
```

也就是说，UI 已经有“步骤列表配置”，但还没有“目标状态/规则条件配置”。

### UI 状态展示与 NG

当前 `AnalysisEngine.ApplyTransition` 会补充：

- `IsTransition`
- `IsReset`
- `TransitionOk`
- `FromStep`
- `ToStep`

桌面端 `CameraSessionViewModel.OnAnalysisResult` 会根据 `TransitionOk`：

- `true`：OK 计数增加。
- `false`：NG 计数增加。
- 当前步骤 `IsNg = true`，UI 可标识异常。

所以“NG 显示”和“OK/NG 计数”已有基础，但当前判定只检查步骤是否按顺序跳转，没有检查目标状态是否满足。

## 建议整体架构

建议沿用现有 `AnalysisEngine + IAnalysisStrategy`，不要新起一条和 UI 强耦合的链路。

```text
YOLO 检测结果
  -> FrameDetections
  -> SopFrameStateExtractor
  -> RingBuffer<SopFrameState> 最近 N 秒
  -> SopRuleAnalysisStrategy
  -> AnalysisResult
  -> CameraSessionViewModel / MainViewModel
  -> SOP UI 步骤状态 + OK/NG
```

### 新增核心对象建议

建议新增一组与 SOP 业务相关的模型，不复用当前只为 0/1/2 距离设计的 `FsmFrameFeatures`。

推荐文件：

```text
src/VideoInference.Core/Analysis/Sop/SopModels.cs
src/VideoInference.Core/Analysis/Sop/SopFrameStateExtractor.cs
src/VideoInference.Core/Analysis/Sop/SopRuleAnalysisStrategy.cs
src/VideoInference.Core/Analysis/Sop/SopRuleEvaluator.cs
```

建议对象：

```csharp
public sealed record SopObjectState(
    int ClassId,
    string Label,
    int Count,
    float BestScore,
    RectF? BestBox,
    double VisibleRatio);

public sealed record SopFrameState(
    long FrameIndex,
    long PtsMs,
    long FrameUtcMs,
    IReadOnlyDictionary<int, SopObjectState> Objects);

public sealed record SopWindowState(
    long StartPtsMs,
    long EndPtsMs,
    IReadOnlyDictionary<int, SopObjectState> Objects);
```

这里的 `VisibleRatio` 是最近 N 秒窗口里某类目标出现的帧占比，用来做去抖和稳定判断。

## 规则策略设计

### 当前状态判定

当前状态不是看单帧，而是看最近 N 秒窗口。

示例：

- 最近 1.5 秒内 `产品` 持续出现，且 `内盒` 出现，判定为“产品入内盒”相关状态。
- 最近 1.5 秒内 `充电器` 出现比例超过 60%，判定为“放入充电器”相关状态。
- 最近 2 秒内 `保修卡` 出现比例超过 60%，判定为“放入保修卡”相关状态。
- 最近 2 秒内 `外盒` 出现并覆盖或靠近其它目标，判定为“装外盒/完成”相关状态。

### 目标状态校验

每个 SOP step 应配置一个目标状态。策略输出当前识别状态后，与步骤配置的目标状态对比：

```text
currentState == expectedState -> OK / 可转入该 step
currentState != expectedState -> NG / 不允许或标红
```

建议区分两类 NG：

1. 顺序 NG：识别到的状态跳过了当前应执行的步骤。
2. 条件 NG：当前步骤顺序没错，但目标检测条件不满足。

现有 `TransitionOk` 只有 bool，可以短期继续用 `false` 表示 NG；中期建议扩展 `AnalysisResult` 增加：

```csharp
public string? NgReason { get; init; }
public string? CurrentStateCode { get; init; }
public string? ExpectedStateCode { get; init; }
```

这样 UI 和日志能说明到底错在哪里。

## 配置设计

### 最小可落地方案

直接扩展 `FsmStepDefinition`：

```csharp
public string? ExpectedStateCode { get; set; }
public string? RuleCode { get; set; }
public int WindowMs { get; set; }
public int MinVisibleRatioQ1000 { get; set; }
```

优点：

- 改动少。
- 可以直接接现有 SOP 步骤窗口。
- 配置跟步骤绑定，UI 容易理解。

缺点：

- 复杂规则表达能力有限。
- 后面不同产品/不同 SOP 版本会让字段变多。

### 推荐中期方案

在 `AppConfig` 中新增独立 `SopAnalysis` 配置：

```json
{
  "SopAnalysis": {
    "Enable": true,
    "WindowMs": 1500,
    "MinScore": 0.45,
    "MinVisibleRatioQ1000": 600,
    "Rules": [
      {
        "StateCode": "inner_box_ready",
        "RequiredClasses": ["内盒", "产品"]
      }
    ]
  }
}
```

`FsmStepDefinition` 只保留引用：

```csharp
public string? ExpectedStateCode { get; set; }
```

这样步骤配置和规则定义解耦，后续可以给不同产品切换规则包。

## UI 接入

当前 `FsmConfigWindow` 需要增加字段：

- 目标状态：`ExpectedStateCode`
- 规则/条件：短期可用下拉框或文本框。
- 窗口时长：默认使用全局 `SopAnalysis.WindowMs`，不建议每步都必填。
- NG 说明展示：来自 `AnalysisResult.NgReason`。

建议 UI 表格列：

```text
步骤 | 名称 | 动作编码 | 目标状态 | TCN 标签
```

短期不建议在 UI 上做复杂规则编辑器，先用固定规则 + 下拉选择状态。规则写在策略类或 JSON 中，稳定后再做可视化配置。

## 后台服务落地步骤

### 第一阶段：规则策略跑通

1. 新增 `SopFrameStateExtractor`，从 `FrameDetections` 提取 6 类目标状态。
2. 新增 `SopRuleAnalysisStrategy`，内部维护最近 N 秒窗口。
3. 输出 `AnalysisResult.Step`，并在 `DebugNote` 里记录 `currentState/expectedState/reason`。
4. `AnalysisEngine` 构造时注入新策略，替换 `BasicDistanceAnalysisStrategy`。
5. 保持现有 UI 只根据 `Step/TransitionOk/IsReset` 工作。

### 第二阶段：目标状态配置

1. 扩展 `FsmStepDefinition` 增加 `ExpectedStateCode`。
2. 扩展 `AppConfigStorage.Normalize/SaveFsmSteps`，保证字段可保存。
3. 扩展 `FsmConfigWindow` 增加“目标状态”列。
4. 策略从 `context.Steps` 读取每步目标状态。

### 第三阶段：NG 语义增强

1. 扩展 `AnalysisResult` 增加 `NgReason/CurrentStateCode/ExpectedStateCode`。
2. `CameraSessionViewModel` 将 NG 原因写到 `LastError` 或单独状态文本。
3. 数据库新增可选表 `sop_analysis_events`，记录状态切换、NG 原因、置信度摘要。

### 第四阶段：与 TCN 平滑衔接

后续数据量足够训练 TCN 后，不需要推翻当前方案。可以将策略升级为：

```text
规则结果 + TCN 预测结果 -> 融合策略 -> AnalysisResult
```

规则仍负责兜底和解释性校验，TCN 负责时序模式识别。

## 高风险项

### 1. 单帧误检导致误判

风险：YOLO 偶发漏检/误检会让状态抖动。

建议：

- 必须基于 N 秒窗口，不用单帧直接判定。
- 使用 `VisibleRatio`、连续帧计数和 hold/debounce。
- 对关键目标设置不同阈值，例如 `产品`、`内盒` 阈值高一些。

### 2. 当前 `FsmFeatureCalculator` 与 SOP 新模型类别不匹配

风险：现有 `FsmFeatureCalculator` 固定认为 class 2 是中心目标，只计算 class 0/1 到 class 2 的距离。这是旧业务假设，不适合 6 类 SOP 规则。

建议：

- 新 SOP 策略不要直接依赖当前 `FsmFrameFeatures`。
- 新增 SOP 专用 extractor，直接消费 `FrameDetections.Detections`。

### 3. 步骤顺序与目标状态混在一起

风险：现有 `TransitionOk` 只表示“是否顺序相邻”，无法表达“顺序正确但条件不满足”。

建议：

- 短期用 `DebugNote` 携带原因。
- 中期扩展 `AnalysisResult` 的 NG 原因字段。

### 4. 多相机/多 session 状态隔离

风险：多个相机同时运行时，策略内部状态若共享，会互相污染。

现状：每个 `CameraSessionViewModel` 会创建自己的 `AnalysisEngine`，这是合理的。但共享模型任务只共享推理任务，不能共享 SOP 状态机。

建议：

- SOP 策略实例必须按 session 创建。
- 策略内部缓存按 `RunUuid` reset，不能跨 run 继承。

### 5. UI 配置和后台规则版本不一致

风险：UI 选择了某个 `ExpectedStateCode`，但策略里没有对应规则，会导致永远 NG 或永远不触发。

建议：

- 启动时校验所有 `ExpectedStateCode` 是否存在。
- 配置窗口用下拉选项，少用自由文本。

### 6. 数据库目前未记录在线分析事件

风险：出现误判后，只能看 raw detections 和 UI 状态，追溯成本高。

建议：

- 增加轻量 `sop_analysis_events` 表或日志文件。
- 至少记录：run、frame、currentState、expectedState、transitionOk、ngReason、window summary。

### 7. 结束条件和 OK 计数定义不明确

风险：当前 transition OK 会增加 OK 计数，可能把每个步骤转移都算一次 OK，而不是“完成一件产品算一次 OK”。

建议：

- 明确 OK/NG 统计口径：按步骤、按产品、按完整 SOP cycle。
- 如果按完整 cycle，只有最后一步完成且无 NG 时才 `OK + 1`。

## 缺口清单

当前必须补的缺口：

1. SOP 专用目标状态 extractor。
2. SOP 规则策略类。
3. `FsmStepDefinition.ExpectedStateCode`。
4. SOP 步骤配置窗口的目标状态列。
5. NG 原因输出。
6. 配置校验。
7. 分析事件记录或调试日志。

可以延后的缺口：

1. 复杂规则可视化编辑器。
2. TCN/规则融合。
3. 多产品 SOP 模板管理。
4. 数据库完整迁移工具。

## 推荐最小实现方案

第一版建议这样做，范围最小且能满足当前需求：

1. 扩展 `FsmStepDefinition`：
   - `ExpectedStateCode`
2. 在 `FsmConfigWindow` 增加“目标状态”列。
3. 新增固定规则策略 `SopRuleAnalysisStrategy`：
   - 内部固定 6 类 SOP 规则。
   - 使用最近 1.5 到 2 秒窗口。
   - 输出当前匹配到的 step。
   - 不匹配时输出 `TransitionOk = false` 或让引擎判定异常。
4. 保持现有 `AnalysisEngine` 作为后台服务入口。
5. 在 UI 中显示当前 NG 原因，至少先落到 `InferenceStatus` 或 `LastError`。

## 当前基础实现约定

已落地的基础策略使用 `AnalysisStrategyNames.SopRules`，由 `AnalysisStrategyFactory` 挂到 `AnalysisEngine`。

当前实现已经拆成三层：

```text
SopRuleAnalysisStrategy       通用外壳：窗口、task 过滤、步骤流转、NG 输出
SopProjectRules.Match         项目入口：这里写每个项目的状态条件
SopConditionHelpers           条件工具：位置、包含、面积、稳定出现等可复用函数
```

后续新项目优先只改：

```text
src/VideoInference.Core/Analysis/Sop/SopProjectRules.cs
```

复杂、项目独有的几何或组合判断函数，放到：

```text
src/VideoInference.Core/Analysis/Sop/SopConditionHelpers.cs
```

当前规则外壳约定：

1. 最近窗口大小来自 `AnalysisConfig.SopWindowMs`。
2. 目标过滤阈值来自：
   - `SopMinScoreQ1000`
   - `SopMinVisibleRatioQ1000`
3. 步骤目标状态优先读取 `FsmStepDefinition.ExpectedStateCode`。
4. `ExpectedStateCode` 可以直接写模型类别名，例如 `内盒`、`产品`、`充电器`，也可以写项目组合状态，例如 `product_in_inner_box`。
5. 如果某个步骤未配置 `ExpectedStateCode`，默认按 SOP 步骤顺序映射到模型类别 id：
   - 第 1 步 -> `class:0`
   - 第 2 步 -> `class:1`
   - 第 3 步 -> `class:2`

项目规则入口示例：

```csharp
public static IEnumerable<SopMatchedState> Match(SopRuleContext context)
{
    foreach (var state in SopConditionHelpers.StableClassStates(
                 context.Window,
                 context.Analysis.Config.SopMinVisibleRatioQ1000))
    {
        yield return state;
    }

    if (SopConditionHelpers.TryGetObject(context.Window, "内盒", out var innerBox) &&
        SopConditionHelpers.TryGetObject(context.Window, "产品", out var product) &&
        innerBox.BestBox is { } inner &&
        product.BestBox is { } target &&
        SopConditionHelpers.CenterInside(inner, target, marginPx: 20))
    {
        yield return new SopMatchedState("product_in_inner_box", "产品在内盒", product.BestScore, Object: product);
    }
}
```

规则函数里可以通过 `context.Window` 拿到：

- `SourceKey`：相机/视频来源
- `TaskId`：任务 id
- `TaskKind`：任务类型
- `Objects`：最近 N 秒窗口内每个类别的状态

每个 `SopObjectWindowState` 包含：

- `ClassId`
- `Label`
- `BestScore`
- `BestBox`
- `Instances`：该类别在窗口里的候选框列表
- `VisibleRatioQ1000`：窗口内稳定出现比例

多相机目前仍然是每个 session 一个 `AnalysisEngine`，但策略本身只依赖输入窗口和 `AnalysisContext`，后续可以增加共享上下文用于“相机 A 依赖相机 B 的状态”这类耦合规则。

## 建议的初始状态码

可先定义这几个状态码，后续再微调：

```text
inner_box_ready      内盒出现
product_ready        产品出现
product_in_box       产品与内盒同时稳定出现
disc_ready           圆片出现
charger_ready        充电器出现
warranty_ready       保修卡出现
outer_box_ready      外盒出现
finished             外盒稳定出现，且关键物料曾经出现过
```

这些状态码与具体 step 的绑定放在 SOP 步骤配置里。

## 验收建议

1. 使用已标注视频或图片序列回放，记录每帧 currentState。
2. 检查状态切换是否稳定，不应每帧来回跳。
3. 故意跳过某个物料，确认 UI 报 NG。
4. 故意调换顺序，确认 UI 报 NG。
5. 多相机同时运行，确认各自 SOP 状态互不影响。
6. 停止并重新开始 run，确认状态窗口清空。
