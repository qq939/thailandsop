# FSM 数据流与耦合说明（维护与扩展指南）

本文档面向后续 FSM 转移策略开发，梳理当前项目的关键函数、数据流和耦合关系，帮助在不破坏现有功能的前提下进行扩展。

## 1. 范围与目标

- 范围：实时推理主链路、FSM 在线分析链路、与标注/数据库的交互边界。
- 目标：
  - 明确“数据从哪里来，在哪里变形，最后写到哪里”。
  - 明确“哪些模块可替换，哪些契约不可破坏”。
  - 给出可落地的扩展 FSM 策略路径。

## 2. 核心文件与职责

### 2.1 主编排层

- `MainViewModel.cs`
  - 初始化配置与组件：`LoadDbConfig`、`LoadAnalysisConfig`、`LoadFsmConfig`。
  - 组装 sink：`BuildResultSink`。
  - 启停 run：`StartPipeline`、`FinalizeCurrentRun`。
  - 接收分析结果并驱动 UI：`OnAnalysisResult`、`ApplyAnalysisStep`。
  - 处理管线结束：`OnPipelineRunEnded`。

### 2.2 实时推理管线

- `VideoPipeline.cs`
  - 启动与线程编排：`StartInternal`。
  - 采集：`CaptureLoop`、`CaptureLoopFfmpeg`、`CaptureLoopOpenCv`。
  - 推理：`InferLoop`、`TryRunDetector`。
  - 结果打包并分发：`TryRecordResults`（构造 `FrameDetections` 发给 sink）。
  - 渲染：`RenderLoop`。
  - 生命周期事件：`RunEnded`（`PipelineRunEnded` + `PipelineRunEndReason`）。

### 2.3 分析引擎层（FSM 在线分析）

- `AnalysisEngine.cs`
  - 插件接口：`IFrameMetricsExtractor`、`IAnalysisStrategy`。
  - 默认实现：`FsmFrameMetricsExtractor`、`BasicDistanceAnalysisStrategy`。
  - 引擎主入口：`AnalysisEngine.TryEnqueue`（实现 `IResultSink`）。
  - 转移判定：`ApplyTransition`、`IsSequential`。
- `AnalysisModels.cs`
  - 分析中间对象：`FsmFrameMetrics`、`AnalysisState`、`AnalysisResult`、`AnalysisContext`。
- `RingBuffer.cs`
  - 分析窗口和历史缓存（帧窗口/状态窗口）。

### 2.4 特征计算与统一语义

- `FsmFeatureCalculator.cs`
  - 统一计算 `FsmFrameFeatures`（与 DB `fsm_frame_features` 语义对齐）。
  - 关键常量：
    - `CenterClassId = 2`
    - `MissingValue = 0xFFFF`

### 2.5 存储层

- `SqliteResultWriter.cs`
  - 异步批量写 `fsm_frame_features`，可选写 `raw_det`。
  - run 结束状态写回：`MarkRunEnded`。
  - schema 初始化/升级：`EnsureInitialized`（当前是版本不匹配直接重建）。
- `sql/schema_v4.sql`
  - 主表：`inference_runs`、`run_models`、`fsm_frame_features`、`fsm_labels`。
  - 视图：`v_run_summary`、`v_fsm_dist`。

### 2.6 可选 TCN/标注链路（与 FSM 并行）

- `ResultSinks.cs`
  - `CompositeResultSink`：并联 fanout。
  - `TcnFeatureFanoutSink`：把同一帧特征扇出给多个消费者。
- `ActionLabelViewModel.cs`
  - 手工/TCN标签流程与 `fsm_labels` 写入触发。
- `TcnLabelWriter.cs`、`TcnPredictionRecorder.cs`、`TcnOnnxInferenceEngine.cs`
  - 属于可选分支，不是 FSM 在线策略的必经链路。

## 3. 端到端数据流（当前实现）

## 3.1 启动阶段

1. `MainViewModel` 加载配置（DB、Analysis、FSM）。
2. 创建 `_resultWriter`、`_analysisEngine` 等组件。
3. `BuildResultSink` 按配置拼接 sink：
   - 必选：`SqliteResultWriter`
   - 可选：`AnalysisEngine`
   - 可选：TCN feature/inference sinks
4. `VideoPipeline.SetResultSink(...)` 完成注入。

## 3.2 实时阶段（每帧）

1. 采集线程得到图像和时间戳（`FramePacket`）。
2. 推理线程调用 detector，得到 `Result[]`。
3. `TryRecordResults` 把结果转换为：
   - `FrameEntity`（source/run/frame/timestamp/model）
   - `DetectionEntity[]`（class/score/box）
   - 组合为 `FrameDetections`
4. `FrameDetections` 进入 `IResultSink`：
   - `SqliteResultWriter.TryEnqueue`：异步写库。
   - `AnalysisEngine.TryEnqueue`：在线分析。
   - 可选 `TcnFeatureFanoutSink`：产出 TCN 特征并扇出。

## 3.3 分析阶段（在线 FSM）

1. `AnalysisEngine.TryEnqueue` 调用 `IFrameMetricsExtractor` 提取 `FsmFrameMetrics`。
2. metrics 入帧窗口（`RingBuffer<FsmFrameMetrics>`）。
3. `IAnalysisStrategy.Analyze(...)` 返回“原始结果”。
4. `ApplyTransition` 统一补充：
   - `IsTransition`
   - `IsReset`
   - `TransitionOk`
   - `FromStep/ToStep`
5. `ResultReady` 事件发给 UI。

## 3.4 UI 阶段（FSM 状态展示）

1. `MainViewModel.OnAnalysisResult` 接收结果。
2. `ApplyAnalysisStep` 根据 `step/isReset/transitionOk` 更新 `FsmSteps`：
   - `< current`: `Done`
   - `== current`: `InProgress`
   - `> current`: `Waiting`
3. `step == null` 时调用 `ClearActiveAnalysisStep`，回收残留 `InProgress` 状态。

## 3.5 结束阶段（run 生命周期）

1. 当 source 正常结束/异常/取消时，`VideoPipeline` 触发 `RunEnded`。
2. `MainViewModel.OnPipelineRunEnded` 根据原因映射状态：
   - `SourceEnded` -> `completed`
   - `SourceError` -> `failed`
   - `Canceled` -> `stopped`
3. `FinalizeCurrentRun` 调用 `SqliteResultWriter.MarkRunEnded` 落库收尾。

## 4. 数据契约（关键对象）

- 采集到分析的统一载体：`FrameDetections`
- 分析输入对象：`FsmFrameMetrics`（来自 `FsmFrameMetricsExtractor`）
- 分析输出对象：`AnalysisResult`
- FSM UI 配置对象：`FsmStepDefinition` / `FsmStepItem`

最关键的隐式契约：

1. `FsmFeatureCalculator.Compute` 的语义必须与 DB 写入语义一致。
2. `AnalysisEngine` 依赖 `Step` 为业务 step id（不是索引）。
3. `IsTransition/IsReset/TransitionOk` 是 UI NG 展示与重置行为的依据。
4. run 级字段（`RunUuid`、`RunStartedUtcMs`）用于全链路对齐与隔离。

## 5. 耦合分析（按风险分级）

## 5.1 高耦合（改动需谨慎）

1. `FsmFeatureCalculator` <-> `SqliteResultWriter` <-> `sql/schema_v4.sql`
   - 特征字段、缺失值编码、q1000 量化必须一致。
2. `AnalysisEngine.ApplyTransition` <-> `MainViewModel.ApplyAnalysisStep`
   - transition/reset 语义变化会直接影响 UI 状态与 NG 判定。
3. `VideoPipeline` run 生命周期 <-> `MainViewModel.FinalizeCurrentRun`
   - 若结束事件漏发，run 状态会残留 `running`。

## 5.2 中耦合（可扩展但需保持接口）

1. `IAnalysisStrategy` 与 `AnalysisContext`
   - 策略可替换，但要遵守 `Analyze` 输入输出契约。
2. `IFrameMetricsExtractor` 与 `FsmFrameMetrics`
   - 可替换输入特征来源，但要保持字段完整性。

## 5.3 低耦合（可独立演进）

1. TCN 链路（`Tcn*`）与 FSM 在线策略本身是并联关系。
2. `CurveLabelTool` 是离线工具，不影响实时策略执行路径。

## 6. 线程与事件耦合

实时链路是多线程结构：

- Capture / Infer / Render 三个任务并行。
- `FrameReady`、`StatsUpdated`、`RunEnded` 由管线抛出。
- UI 更新必须在 Dispatcher 上执行（`MainViewModel` 已处理）。

维护注意：

1. 不要在 `IAnalysisStrategy` 里做阻塞 IO（会直接拖慢推理线程）。
2. 不要在 `ResultReady` 回调里做重计算（会阻塞 UI 分发）。
3. `TryEnqueue` 失败目前是“尽量不中断主流程”的设计，应按监控处理，而不是抛异常中断。

## 7. FSM 转移策略扩展建议（建议路径）

## 7.1 最小侵入扩展

1. 新建策略类实现 `IAnalysisStrategy`（建议新文件，如 `FsmTransitionStrategyV2.cs`）。
2. 保持 `AnalysisResult` 的公共语义不变：
   - `Step`：当前 step id 或 null
   - 其余转移字段由引擎统一补充
3. 在 `MainViewModel` 构造 `AnalysisEngine` 时注入新策略。

## 7.2 推荐边界

- 策略内部只做“判定”，不做：
  - 数据库写入
  - UI 操作
  - 文件 IO
- 跨帧状态优先放入 `AnalysisState`，避免散落在 ViewModel。

## 7.3 何时扩展 `AnalysisState`

当策略需要以下能力时再扩展：

- 去抖（debounce）计数器
- 多阶段 hold
- 异常恢复窗口
- 子状态机（sub-state）上下文

## 8. 维护清单（改动前后必查）

1. 配置兼容性：
   - `analysis_config.json` 字段默认值是否仍有效。
2. run 收尾：
   - 视频自然结束后 `inference_runs.status` 是否为 `completed`。
3. UI 状态：
   - `Step = null` 时是否已回收 `InProgress`。
4. 特征一致性：
   - DB 的 `fsm_frame_features` 与在线分析输入是否一致。
5. 异常路径：
   - source 打开失败、推理异常、用户手动 stop 的 run 状态是否正确。

## 9. 建议的下一步（FSM 策略开发）

1. 先定义策略输入输出契约（step 判定条件、去抖规则、异常规则）。
2. 用离线样本先跑“纯策略单测”（不依赖 UI/DB）。
3. 再接到 `AnalysisEngine` 做在线验证。
4. 最后补一组端到端回放测试，核对 UI 与 DB。

