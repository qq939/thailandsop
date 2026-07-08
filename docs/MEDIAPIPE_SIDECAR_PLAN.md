# MediaPipe Sidecar Worker Plan

## 目标

我们选择路线 C：

- 架构上把 `MediaPipe Hand Landmarker` 接成正式的本地任务服务
- 第一版实现使用 `Python sidecar worker`
- 协议和生命周期管理按“可复用给 OCR / 其他特殊 runtime”设计

这样做的目的不是只把手关键点跑起来，而是把系统从“主进程内嵌一切 runtime”升级成“主进程调度多个视觉任务后端”。

## 为什么选 C

相比把 MediaPipe 直接塞进 WPF 进程，方案 C 的优势更贴合当前代码状态：

- 现在已经有 `VisionTaskDefinition -> IVisionTask -> VisionFrameResult` 的抽象层
- `VideoPipeline` 已经支持 `primary task + sidecar tasks`
- `MediaPipeHandLandmarkTask` 已经有占位骨架
- 后面大概率还有 `OCR`，继续堆特殊分支会越来越乱

路线 C 相当于把 `Python sidecar` 从“临时脚本调用”升级成正式的 `Vision Worker` 机制：

- 当前 worker：`MediaPipe Hand Worker`
- 未来 worker：`OCR Worker`
- 再以后也可以接 `Segmentation Worker`、`Pose Worker`

## 总体结构

```text
WPF Main Process
  -> Session / VideoPipeline
    -> IVisionTask
      -> WorkerBackedVisionTask
        -> Local Worker Host
          -> Python Worker Process
            -> MediaPipe Runtime
```

职责划分：

- 主进程负责 session、相机采集、任务编排、渲染、状态展示
- worker host 负责进程拉起、协议通信、超时、重试、健康检查
- Python worker 负责真正的 MediaPipe 推理

## 一期范围

第一期只做能稳定落地的最小闭环：

- 支持 `MediaPipe Hand Landmarker`
- 每个 `CameraSession` 可挂 1 个或多个 worker-backed sidecar task
- worker 输入为单帧图像
- worker 输出为 `HandLandmarksPayload`
- 主进程负责渲染骨架和显示运行状态

第一期先不做：

- OCR 接入
- 多 worker 类型混部调度
- 跨机器部署
- 零拷贝共享内存
- 分布式任务编排

## 设计原则

### 1. 主进程只认任务，不认具体 runtime

`VideoPipeline` 不应该知道“这是 Python”还是“这是 MediaPipe”。  
它只应该知道：

- 这是一个 `IVisionTask`
- 它可以执行
- 它会返回 payload

因此，MediaPipe 的 Python 细节要尽量收敛在 worker host 和 adapter 里。

### 2. worker 协议按通用视觉任务设计

不要把协议写成“只为手关键点服务”的特例。  
请求/响应结构应该允许以后接：

- `hand_landmarks`
- `ocr_text`
- `pose_landmarks`
- `segmentation_mask`

### 3. 多相机优先稳定，不抢一步到高并发

第一版建议：

- 每个 session 绑定自己的 worker client
- worker 进程按任务实例独立拉起
- session 内部各 sidecar 仍然串行

这会牺牲一点资源复用，但换来更清晰的故障隔离。

### 4. 失败可降级，不拖垮主链路

worker 挂掉后：

- 当前 sidecar task 标记失败
- 主 session 继续跑 primary task
- UI 明确展示 sidecar 不可用
- host 负责按策略重启 worker

## 推荐的一期实现形态

### 进程模型

推荐第一版采用：

- 一个 `WorkerHost` 对应一个本地 Python 进程
- 一个 `MediaPipeHandLandmarkTask` 持有一个 `WorkerHostClient`
- 一个相机 session 使用自己的一组 worker 实例

好处：

- 多相机之间完全隔离
- 一个 worker 崩溃不会影响别的相机
- 调试最简单

代价：

- 进程数会随相机数增加
- Python runtime 占用会更高

在当前阶段，这个取舍是值得的。

### 通信方式

第一版推荐使用 `Named Pipe`。

理由：

- 同机通信足够快
- 不需要占用 HTTP 端口
- 不需要额外服务发现
- 在 Windows 桌面部署里更自然

备选：

- `Local HTTP`：实现快，但会引入口管理和更松散的错误边界
- `gRPC`：长期更规范，但第一版工程量更大

## 协议草案

### 初始化握手

主进程连接 worker 后先发送握手请求：

```json
{
  "type": "hello",
  "protocolVersion": 1,
  "workerKind": "mediapipe_hand",
  "taskId": "mediapipe-hand-landmarks",
  "sessionId": "camera-01",
  "config": {
    "taskFilePath": "C:\\...\\hand_landmarker.task",
    "maxHands": 2,
    "minHandDetectionConfidence": 0.5,
    "minHandPresenceConfidence": 0.5,
    "minTrackingConfidence": 0.5
  }
}
```

worker 返回：

```json
{
  "type": "hello_ack",
  "protocolVersion": 1,
  "workerInstanceId": "mp-hand-camera-01",
  "runtimeLabel": "MediaPipe / Python",
  "ready": true
}
```

### 推理请求

第一版建议采用“长度前缀 JSON 头 + 原始图像字节”的单消息模式。  
图像数据不要 base64，避免额外膨胀。

头部字段建议包含：

- `requestId`
- `frameId`
- `timestampUtc`
- `taskKind`
- `pixelFormat`
- `width`
- `height`
- `stride`
- `roi`
- `imageBytesLength`

其中 `roi` 支持未来做固定区域裁剪：

```json
{
  "x": 320,
  "y": 120,
  "width": 640,
  "height": 640
}
```

### 推理响应

手关键点响应建议映射到我们现有的 `HandLandmarksPayload`：

```json
{
  "type": "inference_result",
  "requestId": "6f0d...",
  "frameId": 18231,
  "ok": true,
  "runtimeLabel": "MediaPipe / Python / CPU",
  "hands": [
    {
      "id": 0,
      "label": "Right",
      "score": 0.97,
      "landmarks": [
        { "index": 0, "x": 0.52, "y": 0.61, "z": -0.03, "score": 0.99 }
      ]
    }
  ]
}
```

失败响应：

```json
{
  "type": "inference_result",
  "requestId": "6f0d...",
  "frameId": 18231,
  "ok": false,
  "errorCode": "runtime_error",
  "message": "MediaPipe task execution failed."
}
```

### 健康检查

worker host 应支持最小健康协议：

- `ping`
- `pong`
- `shutdown`

如果连续超时或返回错误超过阈值，host 将 worker 置为 unhealthy 并触发重启。

## .NET 侧改造计划

### 阶段 1：补 worker 抽象层

新增建议：

- `IVisionWorkerClient`
- `IVisionWorkerHost`
- `VisionWorkerRequest`
- `VisionWorkerResponse`
- `VisionWorkerHostFactory`

建议位置：

- `src/VideoInference.Core/Vision/Workers`

职责建议：

- `IVisionWorkerClient`：发请求、收响应
- `IVisionWorkerHost`：管理进程和连接
- `VisionWorkerHostFactory`：按 `VisionTaskDefinition` 创建 host

### 阶段 2：让 MediaPipe task 走 worker client

当前文件：

- [MediaPipeHandLandmarkTask.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Vision/Tasks/MediaPipe/MediaPipeHandLandmarkTask.cs)

从当前的 `NotImplementedException` 改成：

- 构造时注入 `IVisionWorkerClient`
- `Execute(...)` 把 `Mat` 转成请求
- 收到响应后映射成 `HandLandmarksPayload`
- `Dispose()` 时释放 client / host

### 阶段 3：新增 worker-backed factory

当前文件：

- [MediaPipeHandTaskFactory.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Vision/Tasks/MediaPipe/MediaPipeHandTaskFactory.cs)

改造目标：

- 不再只解析 metadata
- 同时负责：
  - 解析 worker 配置
  - 创建 `IVisionWorkerHost`
  - 创建 `IVisionWorkerClient`
  - 构造 `MediaPipeHandLandmarkTask`

### 阶段 4：状态反馈

建议把 worker 状态透给 session / UI：

- `Starting`
- `Ready`
- `Busy`
- `Degraded`
- `Restarting`
- `Stopped`
- `Faulted`

这样 `CameraSessionViewModel` 可以明确显示 sidecar 当前状态，而不是只显示一个模糊的“任务失败”。

## Python worker 侧计划

### 目录建议

建议新建：

- `workers/mediapipe_hand/`

第一版建议文件：

- `workers/mediapipe_hand/worker.py`
- `workers/mediapipe_hand/protocol.py`
- `workers/mediapipe_hand/runtime.py`
- `workers/mediapipe_hand/requirements.txt`

### Python worker 职责

- 读取握手配置
- 初始化 MediaPipe Hand Landmarker
- 接收图像帧
- 执行推理
- 输出标准化结果
- 维持进程级日志和错误码

### 运行策略

第一版建议：

- worker 只处理单个 session 的单个任务实例
- worker 内部串行处理请求
- 不做批处理
- 不做内部多线程并发

这样最容易保证顺序和调试可控。

## 多相机和多实例策略

### 一期推荐

- 每个相机 session 独立 worker
- 每个特殊任务实例独立 host
- 不共享 worker 进程

这意味着：

- `Camera A + Hand Task` 是一个 worker
- `Camera B + Hand Task` 是另一个 worker

优点是隔离强，坏处是资源更多。

### 二期可优化

等功能稳定后再考虑：

- 同类型任务共享 worker 进程
- 一个 worker 支持多 session logical channel
- worker 池化
- 共享模型实例

这些都不建议第一版就做。

## 图像传输策略

第一版推荐：

- 在 .NET 侧先做 ROI 裁剪或缩放
- 只传 worker 实际需要的图像
- 默认传 `RGB24` 或 `BGR24`

不建议第一版：

- 直接传整帧大图
- 使用 base64 编码
- 把视频文件路径交给 worker 自己读

如果后面发现传输成本高，再考虑：

- JPEG 压缩传输
- 共享内存
- 零拷贝 ring buffer

## 超时与重启策略

建议先定一个保守策略：

- 单帧推理超时：`200ms ~ 500ms`
- 连续超时阈值：`3`
- 连续错误阈值：`3`
- 重启退避：`1s / 3s / 5s`

主进程行为：

- 单次失败：记录并跳过该帧
- 连续失败：标记 task degraded
- 达到阈值：重启 worker
- 重启失败：标记 faulted，但不阻塞 primary task

## 配置建议

`task.json` 建议扩展 metadata：

```json
{
  "id": "mediapipe-hand-landmarks",
  "displayName": "MediaPipe Hand Landmarks",
  "taskKind": "HandLandmarks",
  "runtimeKind": "MediaPipe",
  "metadata": {
    "taskFilePath": "hand_landmarker.task",
    "workerKind": "mediapipe_hand",
    "workerScriptPath": "workers/mediapipe_hand/worker.py",
    "workerProtocol": "named_pipe",
    "maxHands": "2",
    "minHandDetectionConfidence": "0.5",
    "minHandPresenceConfidence": "0.5",
    "minTrackingConfidence": "0.5",
    "preferredInputSize": "640"
  }
}
```

这样以后 OCR 也能用同样的 discovery 方式接进来。

## 实施顺序

### 第 1 步

补 `Workers` 抽象层和本地 host 骨架：

- `IVisionWorkerClient`
- `IVisionWorkerHost`
- `NamedPipeVisionWorkerHost`
- `PythonProcessLauncher`

### 第 2 步

让 `MediaPipeHandTaskFactory` 能创建 worker-backed task。

### 第 3 步

把 `MediaPipeHandLandmarkTask.Execute(...)` 接到真实 worker 协议。

### 第 4 步

实现 Python worker 最小版，先跑通：

- 启动
- 握手
- 单帧推理
- 返回手关键点

### 第 5 步

把 worker 状态接到 UI 文本上，能看到：

- 准备中
- 运行中
- 失败重试
- 已降级

### 第 6 步

补一个调试结果 sink，便于验证 sidecar 输出频率和延迟。

## 验收标准

做到以下几点，就算方案 C 一期完成：

- 从 `task.json` 能发现 `MediaPipe` 任务
- session 能绑定该任务
- 主进程能自动拉起 Python worker
- worker 能处理单帧并返回 21 点手关键点
- 主画面能稳定画出骨架
- worker 崩溃后主进程能自动降级或重启
- primary task 不受 sidecar 故障影响
- 多相机同时运行时互不影响

## 结论

路线 C 最重要的价值，不是“给 MediaPipe 单独开个进程”，而是把当前项目真正推进成一个可长期扩展的多 runtime 视觉任务平台。

对当前阶段，建议按下面的节奏推进：

1. 先做 worker 抽象和 host
2. 再接 MediaPipe hand
3. 跑通后把同一套机制复用给 OCR

这样后面的特殊任务会越来越整齐，而不是越来越难维护。
