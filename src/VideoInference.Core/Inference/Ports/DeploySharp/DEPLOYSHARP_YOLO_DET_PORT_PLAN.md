# DeploySharp YOLO Det 迁移计划

## 目标

在不引入 `DeploySharp` 推理引擎、配置体系和平台依赖的前提下，选择性迁移其 YOLO 检测后处理中的纯算法部分到 `VideoInference.Core`。

迁移后的原则：

- 继续使用我们自己的 ORT runtime
- 继续使用我们自己的预处理入口
- 只迁移纯算法和结果映射逻辑
- 不把 `DeploySharp` 的 `IModel` / `Config` / `InferEngine` 带进来

## 来源清单

主要参考以下源码：

- `O:/DeploySharp/src/DeploySharp/Model/ModelService/Yolo/IYolov8DetModel.cs`
- `O:/DeploySharp/src/DeploySharp/Data/Processor/NonMaxSuppression.cs`
- `O:/DeploySharp/src/DeploySharp/Data/ProcessData/ImageAdjustmentParam.cs`
- `O:/DeploySharp/src/DeploySharp/Data/ResultData/DetResult.cs`
- `O:/DeploySharp/src/DeploySharp/Data/ResultData/Result.cs`

## 迁移边界

适合迁移：

- YOLO 检测输出解码思路
- 候选框结构
- 矩形 NMS
- 坐标反映射参数模型
- 结果结构与字段命名参考

不迁移：

- `Yolov11DetModel` / `IYolov8DetModel` 整体类
- `Yolov11DetConfig` / `YoloConfig` / `IConfig`
- `InferEngineFactory` / `OnnxRuntimeInferEngine`
- `OpenVINO` / `TensorRTSharp` / `DeploySharp` 的运行时封装
- `CvDataProcessor`

## 分阶段实施

### Phase 1

迁移矩形检测候选框和 NMS，先让我们自己的 `YoloDetectionPostprocessor` 使用新的独立 NMS 组件。

目标：

- 建立 `DeploySharp` 迁移专用目录
- 引入 `RectDetectionCandidate`
- 引入 `RectDetectionNms`
- `YoloDetectionPostprocessor` 改为依赖新组件

当前状态：

- 已完成

### Phase 2

抽象坐标反映射层，评估是否需要把 `ImageAdjustmentParam` 的思路进一步标准化到我们的 `YoloImageTransformContext`。

目标：

- 对齐 letterbox / pad / scale 的映射语义
- 让后处理不直接依赖上层调用约定

当前状态：

- 已完成第一版
- 已引入 `ImageAdjustmentGeometry`
- `YoloDetectionPreprocessor` 和 `YoloDetectionPostprocessor` 已改为通过标准几何对象协作

### Phase 3

对比 `IYolov8DetModel` 的输出解码逻辑，确认是否要进一步对齐：

- 置信度过滤
- 类别分数读取
- 是否需要支持更多 YOLO 输出形状

当前状态：





- 已完成第一版
- 已支持通过 `model.json -> yolo` 元数据显式声明输出布局与打分模式
- 当前已支持：
  - `channels_first`
  - `boxes_first`
  - `class_only`
  - `objectness_and_class`

### Phase 4

如果迁移结果稳定，再评估是否继续参考 `DeploySharp` 的：

- pose
- seg
- obb

但仍保持“只迁算法，不迁引擎”。

当前状态：

- 已补充迁移边界说明
- 当前仍不实施 pose / seg / obb 代码迁移

## 验收标准

- `VideoInference.Core` 不新增 `DeploySharp` 运行时依赖
- `YoloDetectionPostprocessor` 行为与现有模型输出兼容
- Ubuntu / Jetson 路线不因为该迁移引入新的平台耦合
- 后处理逻辑可单独测试，不依赖 ORT session 或宿主 UI
