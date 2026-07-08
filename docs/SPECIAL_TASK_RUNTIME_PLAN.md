# MediaPipe 手关键点与 OCR 特殊任务接入方案

## 结论

如果后面不只是手关键点，还大概率会接 OCR，那现在就把系统从“单一检测模型流水线”升级为“可扩展视觉任务流水线”是值得的。

`MediaPipe Hand Landmarker` 的确更像一个独立 runtime，不适合硬塞进当前 `YOLO/Sequence` 模型分发体系。但如果把它作为“特殊任务”接入，侵入性是可控的，而且这一步还能顺便为 OCR、姿态、分割等后续任务铺路。

建议不要把 MediaPipe 伪装成 `detection`，也不要把 OCR 伪装成 `detection`。  
更稳妥的方向是：保留现有检测链路，同时新增“视觉任务”抽象层。

## 当前系统的耦合点

当前代码已经支持多种模型，但底层假设仍然偏强：

- 模型类型只认 `detection` 和 `sequence_bands`
- 推理目标只实现了 `YoloInferenceTarget` 和 `SequenceInferenceTarget`
- 推理结果统一收敛为 `DetectionEntity`
- 结果落库统一走 `FrameDetections`
- 渲染只支持画检测框和画 sequence bands

关键文件：

- [ModelCatalog.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/ModelCatalog.cs)
- [ModelPipelineFactory.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Factory/ModelPipelineFactory.cs)
- [VideoPipeline.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs)
- [VideoPipeline.InferenceRender.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.InferenceRender.cs)
- [PipelineInferenceTargets.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineInferenceTargets.cs)
- [PipelineFrameAnnotator.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineFrameAnnotator.cs)
- [InferenceResultModels.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/InferenceResultModels.cs)
- [PipelineResultPublisher.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineResultPublisher.cs)

这套设计对目标检测很自然，但对以下任务会开始吃力：

- 手关键点
- OCR
- 姿态估计
- 分割
- 旋转框/文本框

## 设计目标

新方案的目标不是推翻现有 pipeline，而是在尽量少动现有检测链路的前提下，让系统支持“同一套采集/播放/会话框架下挂不同视觉任务”。

理想状态下，一类视觉任务应该至少能独立提供：

- 自己的 runtime
- 自己的结果结构
- 自己的渲染逻辑
- 自己的导出/落库逻辑

这样：

- `YOLO Detection` 继续走 ONNX Runtime
- `SequenceBands` 继续走 ONNX Runtime
- `MediaPipe Hand Landmarker` 走独立 runtime
- `OCR` 将来可接 PaddleOCR、RapidOCR、ONNX OCR 或其他 runtime

## 两条改造路线

这里给两条路线：

- 小改版：尽快落地 MediaPipe 手关键点
- 正规版：顺手把架构升级成任务扩展型

## 路线 A：小改版

### 适用场景

- 目标是先尽快接入 MediaPipe 手关键点
- 后续 OCR 虽然有可能，但短期内还没开始
- 希望尽量少改现有数据库和 UI 结构

### 做法

保留现有 `IInferenceTarget` 和 `DetectionEntity` 主链路不动，额外增加一个“特殊任务旁路”。

#### 核心思路

1. 视频采集、回放、会话控制完全不动
2. `VideoPipeline` 增加一个可选的“特殊视觉任务执行器”
3. 当当前任务类型不是 `detection/sequence_bands` 时，跳过现有 `TryRunInference` 逻辑，改走特殊执行器
4. 特殊执行器自己负责：
   - 跑 runtime
   - 渲染图像
   - 可选生成结构化结果

### 建议新增的最小对象

#### 1. 新增任务类型

在模型或任务层面增加：

- `hand_landmarks`
- 以后预留 `ocr_text`

这里不建议把它放进当前 `ModelTaskType` 后继续复用 `ModelPipelineFactory` 强行返回 `InferenceModelKind.YoloDetection`。  
如果要加，就明确加成新类型。

#### 2. 新增特殊任务运行接口

建议新增类似接口：

```csharp
public interface ISpecialVisionTask : IDisposable
{
    string TaskKind { get; }
    string? ActiveDeviceLabel { get; }
    SpecialVisionTaskResult Execute(Mat image);
    bool TryHandleFailure(Exception ex, out string message);
}
```

#### 3. 新增特殊任务结果

```csharp
public sealed class SpecialVisionTaskResult
{
    public Action<Mat>? Annotate { get; init; }
    public object? Payload { get; init; }
    public string? DeviceLabel { get; init; }
}
```

`Payload` 在小改版里先保持宽松：

- MediaPipe 手关键点时放 `HandLandmarkResult`
- OCR 时放 `OcrResult`

### MediaPipe 手关键点的小改版挂载方式

新增：

- `MediaPipeHandLandmarkTask`
- `HandLandmarkResult`
- `HandLandmarkPoint`
- `HandLandmarkRenderer`

#### 推荐结果结构

```csharp
public sealed record HandLandmarkPoint(
    int Index,
    float X,
    float Y,
    float Z,
    float Visibility);

public sealed record HandLandmarkResult(
    string Handedness,
    float Score,
    IReadOnlyList<HandLandmarkPoint> Points);
```

#### 渲染职责

在 `HandLandmarkRenderer` 里：

- 画 21 个点
- 按骨架连接线画掌骨/手指
- 支持左右手不同颜色

这一步不要硬塞进 `PipelineFrameAnnotator.DrawDetections`。  
可以：

- 新增 `DrawHandLandmarks`
- 或者让 `MediaPipeHandLandmarkTask.Execute` 直接返回自己的 `Annotate`

### OCR 的小改版挂载方式

未来 OCR 可以复用同样模式：

- `OcrTask`
- `OcrTextBlock`
- `OcrResult`
- `OcrRenderer`

`OcrTask.Execute` 返回：

- 识别出的文本块
- 多边形/矩形框
- 在图像上叠加文本的 `Annotate`

### 小改版需要改哪些地方

#### 1. `VideoPipeline`

在 [VideoPipeline.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs) 和 [VideoPipeline.InferenceRender.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.InferenceRender.cs) 增加：

- `_specialTask`
- 特殊任务的加载/卸载
- `TryRunInference` 中判断当前是否走特殊任务

#### 2. `ModelCatalog` 或任务配置读取

如果 MediaPipe 不放进 `DL` 模型目录，而是走单独配置，可以新建任务配置文件。

如果仍想统一放在 `DL`，建议目录里存的是：

- `task.json`
- MediaPipe 的 `.task` 文件路径

而不是假装它是 ONNX。

#### 3. `MainViewModel` / `CameraSessionViewModel`

主要改的是加载逻辑和状态文案，不需要大改交互框架。

### 小改版的优点

- 对当前检测链路影响小
- MediaPipe 能较快接进来
- OCR 后面也能照抄这条模式

### 小改版的缺点

- 特殊任务和标准模型会并存两套入口
- 结果存储会开始分裂
- 时间长了会变成“主链路 + 多个旁路”

## 路线 B：正规版

### 适用场景

- 你们确认后面还会接 OCR
- 未来可能继续接手关键点、姿态、OCR、分割等
- 希望一次把扩展结构搭稳

### 核心思路

把当前“模型推理”提升为“视觉任务执行”。

现有 pipeline 关注的是：

- 拿到帧
- 执行任务
- 渲染帧
- 发布结果

而不再假设“任务结果一定是 `DetectionEntity[]`”。

### 推荐的新抽象

#### 1. 统一任务接口

```csharp
public interface IVisionTask : IDisposable
{
    string TaskKind { get; }
    string? ActiveDeviceLabel { get; }
    VisionTaskExecutionResult Execute(Mat image, VisionTaskDrawContext drawContext);
    void Warmup(int width, int height);
    bool TryHandleFailure(Exception ex, out string message);
}
```

#### 2. 统一任务结果

```csharp
public sealed class VisionTaskExecutionResult
{
    public required VisionTaskPayload Payload { get; init; }
    public required Action<Mat> Annotate { get; init; }
    public string? DeviceLabel { get; init; }
}
```

#### 3. 统一 payload 基类

```csharp
public abstract class VisionTaskPayload
{
}
```

派生：

- `DetectionPayload`
- `SequenceBandsPayload`
- `HandLandmarksPayload`
- `OcrPayload`

#### 4. 统一结果发布接口

把当前 `IResultSink` 旁边新增一个更通用的发布接口，例如：

```csharp
public interface IVisionResultSink
{
    bool TryPublish(VisionFrameResult result);
}
```

其中 `VisionFrameResult` 包含：

- `FrameEntity`
- `VisionTaskPayload`

### 正规版下现有任务如何迁移

#### Detection

当前：

- `YoloInferenceTarget -> DetectionEntity[]`

迁移后：

- `YoloVisionTask -> DetectionPayload`

#### Sequence

当前：

- `SequenceInferenceTarget -> DetectionEntity[] + DrawSequenceBands`

迁移后：

- `SequenceBandsVisionTask -> SequenceBandsPayload`

#### MediaPipe Hand Landmarks

新增：

- `MediaPipeHandLandmarksVisionTask -> HandLandmarksPayload`

#### OCR

新增：

- `OcrVisionTask -> OcrPayload`

### 正规版需要重点改的文件

#### 1. `PipelineInferenceTargets.cs`

这里将从“检测目标集合”升级为“视觉任务集合”。

#### 2. `VideoPipeline.InferenceRender.cs`

当前 `TryRunInference` 写死了：

- `result.Detections`
- `TryRecordResults(packet, result.Detections)`
- `result.Annotate(image)`

这里要改成：

- `result.Payload`
- `TryPublishVisionResult(packet, result.Payload)`
- `result.Annotate(image)`

#### 3. `PipelineResultPublisher.cs`

当前只会发布 `FrameDetections`。  
正规版建议：

- 保留现有 `FrameDetections` 发布器给检测链路兼容使用
- 旁边增加通用 `VisionResultPublisher`

#### 4. `ResultSinks.cs`

`TCN` 这类依赖检测框特征的链路，只应该绑定在 `DetectionPayload` 上。  
这反而是正规版的好处：依赖关系会更清晰。

### 正规版的优点

- 后续接 MediaPipe、OCR、姿态、分割都顺
- 不需要把所有任务伪装成检测框
- 每类任务都能独立定义自己的存储和渲染

### 正规版的缺点

- 一次性改动面更大
- 需要重构 `VideoPipeline.InferenceRender.cs` 附近的结果流

## 我建议你们怎么选

### 如果目标是一个月内先把手关键点做出来

选路线 A。

原因：

- 改动可控
- MediaPipe 能快速验证业务价值
- 对当前检测与 sequence 功能影响最小

### 如果你们基本确定 OCR 很快也会来

选路线 B。

原因：

- OCR 和手关键点都不是“检测框优先”的天然模型
- 现在不抽象，后面会连续做两次特殊旁路
- 两次旁路叠加后会比一次正规重构更乱

## 推荐的实际落地顺序

我建议按下面顺序推进。

### 第 1 步：先做任务级概念，不急着全量重构

先在设计上承认三类东西：

- 标准 ONNX 检测任务
- 标准 ONNX sequence 任务
- 特殊 runtime 任务

哪怕代码上先只是小改版，也先把概念命名定下来。

### 第 2 步：先落 MediaPipe 手关键点

目标：

- 能在现有会话里跑
- 能画点和骨架
- 能显示左右手和置信度

先不要求统一落库。

### 第 3 步：在接 OCR 前升级结果抽象

一旦确认 OCR 要做，就开始把：

- `DetectionEntity`
- `FrameDetections`
- `IResultSink`

扩展成更通用的任务结果体系。

### 第 4 步：逐步把检测和 sequence 也迁到统一任务抽象

这一步不用一次做完，可以新老共存一段时间。

## 推荐结论

对你们现在这个项目，我的建议是：

- 短期：按“小改版”接 MediaPipe 手关键点
- 中期：在 OCR 立项时切到“正规版”统一任务抽象

如果你们已经很确定 OCR 很快就会做，那也可以直接一步到位上“正规版”。

最重要的一点是：

不要再把所有视觉能力都理解为“一个 ONNX 模型 + 检测框结果”。  
从手关键点开始，这个系统应该升级成“多视觉任务框架”了。
