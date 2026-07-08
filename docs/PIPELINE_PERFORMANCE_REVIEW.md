# Pipeline 性能与数据流检查

本文档用于记录当前 `Desktop` 多路相机场景下的 pipeline 数据流检查结果，重点回答两个问题：

1. 当前 CPU / GPU 占用偏高是否合理。
2. 现有实现中是否存在非必要的拷贝和 CPU 操作。

本文基于当前代码结构分析，不是压测报告。

## 当前场景

- 3 路相机同时采集
- 3 路相机同时推理
- 3 路相机同时保存视频
- Windows Desktop 宿主运行

## 重要前提

### 每个 pipeline 独立持有模型是当前设计约束

这一点不是当前阶段的优化目标。

原因是：

- 每个相机的任务可能不同
- 每个相机后续可能绑定不同模型
- 每个相机会话当前就是独立运行单元

因此下面的分析里：

- “每个相机会话各自持有模型”视为当前业务约束
- 不把“把所有相机改成共享同一份模型”作为默认优化方案

但需要注意：

- 独立模型实例会天然抬高 GPU 显存占用
- 这是结构性成本，不应与“非必要拷贝”混为一谈

## 当前数据流概览

以相机模式为例，单路数据流大致如下：

1. 相机驱动采集帧到 `Mat`
2. `VideoPipeline.CaptureLoopCamera(...)` 将帧送入推理队列
3. 如果启用录像，同一帧会送入 recorder
4. `InferLoop(...)` 取帧做预处理、推理、结果绘制
5. `ToRenderPacket(...)` 生成渲染包
6. `RenderLoop(...)` 把图像交给 WPF 层显示

对应代码位置：

- [VideoPipeline.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs)
- [SegmentedVideoRecorder.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Recording/SegmentedVideoRecorder.cs)
- [WpfFramePresenter.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Rendering/WpfFramePresenter.cs)

## 目前合理的部分

### 1. 使用了有界队列

`VideoPipeline` 对采集队列和渲染队列都使用了有界 `BlockingCollection`，并且相机模式下偏向丢旧帧而不是无限堆积。

这部分方向是正确的，避免了多路相机长时间运行后内存和延迟无上限增长。

### 2. `FramePacket` / `RenderPacket` 有所有权转移

当前并不是每一层都盲目 `Clone()`。

`FramePacket` 和 `RenderPacket` 通过释放回调转移 `Mat` 所有权，这一层设计本身是合理的。

## 当前确认的主要热点

## 1. 录像链在采集线程里做深拷贝

这是当前最明确、最值得优化的问题之一。

代码位置：

- [SegmentedVideoRecorder.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Recording/SegmentedVideoRecorder.cs#L88)
- [SegmentedVideoRecorder.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Recording/SegmentedVideoRecorder.cs#L99)

当前行为：

- `CaptureLoopCamera(...)` 拿到一帧后调用 `_recorder?.TryEnqueue(frame, ...)`
- `SegmentedVideoRecorder.TryEnqueue(...)` 里立刻 `frame.Clone()`

问题在于：

- 深拷贝发生在采集线程
- 3 路相机同时录像时，采集线程会持续承担高分辨率帧复制成本
- 这部分负担直接抬高 CPU

结论：

- 这是非必要的热点
- 录像异步化不完整
- 应优先优化

## 2. 所有相机会话都在持续进行 WPF 渲染

代码位置：

- [CameraSessionViewModel.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Camera/CameraSessionViewModel.cs#L59)
- [WpfFramePresenter.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Rendering/WpfFramePresenter.cs#L19)
- [WpfFramePresenter.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Rendering/WpfFramePresenter.cs#L42)

当前行为：

- 每个相机会话自己的 pipeline 都订阅 `FrameReady`
- 每帧都进入 `WritePixels(...)`

这意味着：

- 即使用户只关注当前一个 tab，其他相机页也可能仍在持续推位图
- 多路情况下，WPF UI 线程和 CPU 会承担额外压力

结论：

- 这是明显的非必要 CPU / UI 开销
- 应尽量只渲染当前选中相机，或降低后台相机的渲染频率

## 3. 预处理完全走 CPU，且存在逐像素循环

代码位置：

- [YoloDetectionPreprocessor.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Preprocess/YoloDetectionPreprocessor.cs)
- [SequenceBandPreprocessor.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Preprocess/SequenceBandPreprocessor.cs)

当前行为包括：

- `CvtColor`
- `Resize`
- `CopyTo`
- 像素级 for 循环填充 `DenseTensor<float>`
- `Sequence` 路径中的多次 `Clone()`

这部分不一定是“错误”，但在多路场景下是稳定的大头 CPU 消耗。

结论：

- 这是合理但昂贵的 CPU 开销
- 不属于静默 bug
- 后续可以继续优化，但优先级低于录像拷贝和无效渲染

## 4. 结果绘制也完全落在 CPU

代码位置：

- [VideoPipeline.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs#L1164)
- [VideoPipeline.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs#L1249)

当前行为：

- 检测框绘制使用 OpenCV CPU 画框
- `Sequence` 叠加会先 `overlay = image.Clone()`

结论：

- 这部分在多路场景下确实吃 CPU
- 但相比录像深拷贝和多路 WPF 渲染，它不是第一优先级

## 5. 主 pipeline 存在一次重复 `LoadModel`

代码位置：

- [PipelineSessionController.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineSessionController.cs#L46)
- [DesktopModelActivationService.cs](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Services/DesktopModelActivationService.cs#L43)

当前行为：

- `PipelineSessionController.ApplyModel(...)` 已经会执行 `_pipeline.LoadModel(descriptor)`
- `DesktopModelActivationService.ActivateModel(...)` 又对同一个主 pipeline 调了一次 `_pipeline.LoadModel(descriptor)`

结论：

- 这是明确的重复操作
- 它不是最大热点
- 但属于应该清掉的无效工作

## 不应误判为问题的部分

## 1. 多路相机导致显存高

如果每个相机会话都有自己的模型实例，那么：

- GPU 专用显存高是可以预期的
- 这是独立模型架构的结果
- 不能简单归类为 bug

如果后续业务仍然要求“每路相机模型可不同”，那么显存预算应按多路独立实例来规划。

## 2. NVENC 编码占用一定 GPU

录像使用硬编码时，`Video Encode` 有明显占用是正常现象。

当前更值得关注的是：

- GPU 专用显存是否接近打满
- CPU 是否被额外的拷贝和渲染压高

## 优先优化建议

按收益和风险排序，建议优先做下面几项。

### 优先级 1：把录像深拷贝从采集线程挪走

目标：

- 采集线程不再执行 `frame.Clone()`
- 录像 worker 自己负责拿所有权或使用独立 buffer/pool

预期收益：

- 直接降低多路录像时 CPU 压力
- 让采集链更稳定

### 优先级 2：只渲染当前可见相机

目标：

- 非当前 tab 不持续 `WritePixels`
- 或者后台相机会话降低渲染频率

预期收益：

- 明显降低 CPU 和 UI 线程压力
- 不影响推理和录像主链

### 优先级 3：清理主 pipeline 的重复 `LoadModel`

目标：

- 避免同一描述符重复加载到主 pipeline

预期收益：

- 清理无效工作
- 降低模型切换时的多余开销

### 优先级 4：继续收预处理和绘制热点

目标：

- 减少 `Mat` 中间对象
- 减少逐像素访问成本
- 减少 `Sequence` 路径中的额外 `Clone()`

预期收益：

- 有助于降低 CPU
- 但改动面较大，适合在前 3 项之后处理

## 当前判断

从宏观上看，当前高占用并不是单一 bug，而是几类成本叠加：

- 多路独立模型实例带来的显存占用
- 录像链采集线程深拷贝
- 多路 WPF 渲染
- CPU 预处理
- CPU 画框与叠加

因此后续优化应区分：

- 哪些是业务约束导致的合理成本
- 哪些是当前实现里的非必要拷贝和非必要 CPU 工作

当前最值得优先处理的是后者。
