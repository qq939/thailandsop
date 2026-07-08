# 在线分析框架

本文档说明一个不依赖 TCN 训练的在线分析框架。目标是结构清晰、易扩展、易维护。

## 目标

1. 使用轻量级的每帧特征（与 `fsm_frame_features` 对齐）。
2. 维护最近 N 帧窗口。
3. 维护最近 N 条分析结果（状态历史）。
4. 通过策略类输出 step/state。
5. UI 仅消费结果，分析逻辑与 UI 分离。

## 数据流

```
FrameDetections
  -> FsmFrameMetricsExtractor（特征提取）
  -> RingBuffer<FsmFrameMetrics>（最近 N 帧）
  -> IAnalysisStrategy（策略分析）
  -> AnalysisResult
  -> UI 更新（FSM step 状态）
```

## 核心类型

### 1) 帧特征

`FsmFrameFeatures` 是与 DB 视图对齐的指标：

- `dist_id0_to_id2_q1000`
- `dist_id1_to_id2_q1000`
- `score_id0_q1000`, `score_id1_q1000`, `center_score_q1000`
- `area_id0_px`, `area_id1_px`, `area_id2_px`

提取逻辑在 `FsmFeatureCalculator.Compute(...)`，与数据库写入一致。

### 2) 帧指标

`FsmFrameMetrics` = 帧元数据 + `FsmFrameFeatures`：

- `RunUuid`, `RunStartedUtcMs`, `SourceKey`
- `FrameIndex`, `PtsMs`, `FrameUtcMs`
- `Features`

### 3) 环形队列

`RingBuffer<T>` 是固定长度队列：

- O(1) 插入
- 满时覆盖最旧数据
- 用于帧窗口和状态历史

### 4) 分析上下文 / 状态

- `AnalysisState`：可变且持久（如 `ActiveStep`, `HoldCounter`）
- `AnalysisContext` 组合：
  - `AnalysisConfig`
  - `FsmStepDefinition` 列表
  - 最近 `AnalysisResult` 历史
  - `AnalysisState`

### 5) 策略

`IAnalysisStrategy` 是可插拔的分析逻辑：

```csharp
AnalysisResult Analyze(
  FsmFrameMetrics current,
  IReadOnlyList<FsmFrameMetrics> window,
  AnalysisContext context);
```

默认策略是 `BasicDistanceAnalysisStrategy`（简单阈值 + hold），可替换为真实算法。

### 6) 引擎

`AnalysisEngine` 负责串联：

- 维护帧窗口与状态历史
- 调用特征提取与策略
- 触发 `ResultReady` 事件供 UI 更新

它实现 `IResultSink`，可直接挂到推理管线。

## UI 集成

`MainViewModel` 监听 `AnalysisEngine.ResultReady`。

当 `Step` 有值时，更新 `FsmSteps`：

- 小于当前 step：`Done`
- 当前 step：`InProgress`
- 大于当前 step：`Waiting`

这样 UI 逻辑保持简单。

### 顺序与异常（NG）

分析引擎会在每次 **step 发生变化** 时判断是否顺序正常，并输出一个布尔值：

- 顺序正确：`TransitionOk = true`
- 顺序异常：`TransitionOk = false`
- 没有发生 step 变化：`TransitionOk = null`

规则：

1. 触发 **第一个 step** 时认为是一次“重置”：
   - `IsReset = true`
   - 清空 UI 的历史状态与异常标记
2. 非首个 step：
   - 必须与上一 step **顺序相邻** 才算正常
   - 否则判定为 NG（异常）

UI 会在 `IsNg = true` 时标红。

## 配置

在线分析配置使用 `analysis_config.json`，与其它配置独立。
编译时会复制到输出目录。

### 字段说明

- `EnableOnlineAnalysis`（bool）
  - `true`：启用在线分析
  - `false`：不创建分析引擎

- `FrameWindowSize`（int）
  - 帧窗口长度（最近 N 帧）

- `StateWindowSize`（int）
  - 结果历史长度（最近 N 条）

- `NearThresholdQ1000`（int）
  - 距离阈值（q1000）

- `NearStep`（int 或 null）
  - 指定一个 step 作为“近距离”输出
  - 为 null 时使用 FSM 配置中的第一个 step

- `HoldFrames`（int）
  - 当条件不满足时，仍保持上一次 step 的帧数

### 默认配置

```json
{
  "EnableOnlineAnalysis": true,
  "FrameWindowSize": 100,
  "StateWindowSize": 30,
  "NearThresholdQ1000": 300,
  "NearStep": null,
  "HoldFrames": 10
}
```

## 扩展点

1. 替换策略：
   - 实现 `IAnalysisStrategy`
   - 注入 `AnalysisEngine`

2. 自定义特征提取：
   - 实现 `IFrameMetricsExtractor`
   - 用其它特征替代当前指标

3. 自定义状态逻辑：
   - 扩展 `AnalysisState` 字段
   - 策略读写这些状态

## 备注

- 不依赖 TCN 训练。
- 不要求数据库写入，完全内存执行。
- 如需记录分析输出，可扩展为写库或写日志。
