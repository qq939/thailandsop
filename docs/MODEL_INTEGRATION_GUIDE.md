# 新增模型接入说明

## 结论

如果我们要在当前项目里“加一个模型”，第一步确实是把模型放进 `DL/<bundle_name>/` 目录，并准备好 `model.json`。

但这还不是全部。是否还能“直接用”，取决于新模型能不能落到项目现有的两条推理链路之一：

- `Detection`：当前对应 `YOLO ONNX` 检测链路
- `SequenceBands`：当前对应序列分层带状输出链路

可以先用一句话概括：

- 如果新模型只是“同一类任务的新权重”，并且输入输出格式与现有链路兼容，那么基本就是“放到 `DL` + 补元数据 + 适配少量配置”。
- 如果新模型的输入预处理、输出张量结构、后处理逻辑、可视化方式与现有链路不兼容，那么就不只是放模型了，还需要新增或改造前处理、后处理，必要时还要扩展模型类型分发代码。

所以你的理解“把模型放到 `DL`，然后适配前处理和后处理”总体是对的，但更准确地说应该是：

1. 先把模型做成一个 `DL` bundle，让系统能发现它。
2. 再判断它是否能复用现有推理链路。
3. 不能复用时，再补前处理、后处理和模型分发代码。

## 当前项目的模型接入链路

当前桌面端和 Jetson 端都走同一套 `DL` 模型发现逻辑。

### 1. 模型发现

入口在：

- [ModelWorkspaceService.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Workspace/ModelWorkspaceService.cs)
- [ModelCatalog.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/ModelCatalog.cs)

行为如下：

- 默认扫描 `AppContext.BaseDirectory/DL`
- 扫描 `DL` 下的一级子目录，每个子目录视为一个模型 bundle
- 每个 bundle 优先读取 `model.json`
- 如果 `DL` 下没有子 bundle，则退回兼容旧版的 `DL` 根目录直扫模式

推荐目录结构：

```text
DL/
  yolo/
    model.json
    best.onnx
  sequence_bands/
    model.json
    sequence_model.onnx
```

### 2. Bundle 元数据解析

`ModelCatalog` 会从 `model.json` 中解析：

- `id`
- `displayName`
- `description`
- `taskType`
- `modelFile`
- 类别和绘制配置
- `yolo` 元数据
- `sequence` 元数据

其中最关键的是 `taskType`，它决定后面走哪条模型链路。

当前只支持两类：

- `detection`
- `sequence_bands`

### 3. 绑定计划与模型描述

入口在：

- [ModelBindingPlanFactory.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Factory/ModelBindingPlanFactory.cs)
- [ModelPipelineFactory.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Factory/ModelPipelineFactory.cs)

这一步会做两件事：

- 从 `model.json` 读取类别名、框颜色、线宽、文字大小等绘制配置
- 根据 `taskType` 和模型文件扩展名，生成 `InferenceModelDescriptor`

当前分发规则很明确：

- `SequenceBands` -> `InferenceModelKind.SequenceBands`
- `.onnx` 检测模型 -> `InferenceModelKind.YoloDetection`
- `.engine` 不再直接支持

### 4. Pipeline 加载

入口在：

- [PipelineSessionController.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineSessionController.cs)
- [VideoPipeline.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs)
- [PipelineInferenceTargets.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineInferenceTargets.cs)

加载时会根据 `InferenceModelKind` 进入不同目标：

- `YoloInferenceTarget`
- `SequenceInferenceTarget`

这意味着当前系统不是“任意 ONNX 都能直接跑”，而是“任意 ONNX 只要符合当前支持的任务类型和输入输出约定，才能直接跑”。

## 什么时候只放到 DL 就够了

以下情况通常只需要新增 bundle，不需要改核心代码。

### 场景 1：新增一个 YOLO 检测权重

比如：

- 还是目标检测
- ONNX 输入仍然是图像
- 输出仍然是 YOLO 风格的检测头
- 后处理仍然是框、类别、置信度、NMS

这类模型通常可以直接复用：

- [Yolo11Detector.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Yolo11Detector.cs)
- [YoloDetectionPreprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Preprocess/YoloDetectionPreprocessor.cs)
- [YoloDetectionPostprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Postprocess/YoloDetectionPostprocessor.cs)

这时要做的主要是：

1. 把模型放到 `DL/<name>/model.onnx`
2. 写好 `model.json`
3. 配好类别名和显示颜色
4. 如果输出布局或打分方式和现有默认假设不同，补 `yolo` 元数据

### 场景 2：新增一个同规格的 Sequence 模型

比如：

- 还是序列分层任务
- 输入还是单张图像
- 输出仍然是 `[1, class_count, seq_len]` 或兼容格式
- 仍然是按序列位置做 argmax，再合并连续段

这类模型通常可以直接复用：

- [SequenceOnnxModel.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/SequenceOnnxModel.cs)
- [SequenceBandPreprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Preprocess/SequenceBandPreprocessor.cs)
- [SequenceBandPostprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Postprocess/SequenceBandPostprocessor.cs)

这时重点是把 `sequence` 元数据写准确：

- 输入输出名
- 输入尺寸
- 类别名
- `seq_len`
- 裁剪、resize、归一化配置
- 后处理配置

## 什么时候必须改前处理和后处理

以下情况就不能只靠放文件和改 `model.json` 解决。

### 1. 输入预处理不一样

当前检测模型前处理固定做的是：

- 转 BGR 三通道
- letterbox 到模型输入尺寸
- 填充值 `114`
- `1/255`
- 输出 `NCHW`

对应代码见：

- [YoloDetectionPreprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Preprocess/YoloDetectionPreprocessor.cs)

当前序列模型前处理固定做的是：

- 按配置裁掉底部一部分
- 可选做 `center_width_crop`
- resize 到目标尺寸
- 按 `mean/std` 归一化
- 输出 `NCHW`

对应代码见：

- [SequenceBandPreprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Preprocess/SequenceBandPreprocessor.cs)

如果新模型要求这些中的任一项发生变化，就要改代码或新增预处理器：

- RGB 而不是当前实际使用的 BGR 流程
- 不做 letterbox，而是直接 resize
- 需要 pad 到特定倍数
- 输入不是 `NCHW`
- 输入不是单张图片，而是多帧、双输入、额外标量输入
- 归一化方法不同
- 需要 ROI 裁剪或几何变换

### 2. 输出张量结构不一样

当前检测后处理默认假设输出是 YOLO 风格三维张量，且能从中解析出：

- 框中心点和宽高
- objectness 或 class score
- 每类得分
- NMS

对应代码见：

- [YoloDetectionPostprocessor.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Postprocess/YoloDetectionPostprocessor.cs)

当前它能通过 `model.json -> yolo` 元数据适配的只是有限差异：

- `outputLayout`: `channels_first` 或 `boxes_first`
- `scoreMode`: `class_only` 或 `objectness_and_class`
- `classCount`

如果新模型输出是这些情况，就要改后处理：

- 输出不是 YOLO head
- 输出是多个 tensor
- 输出是 segmentation mask
- 输出是 keypoints / pose
- 输出是 rotated box / oriented box
- 输出是 heatmap
- 输出已经做过 decode，字段定义不同

### 3. 结果表达形式不一样

当前 pipeline 里真正统一的数据结构还是 `DetectionEntity`，可视化也只有两种：

- 画检测框
- 画 sequence bands

对应代码见：

- [PipelineInferenceTargets.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineInferenceTargets.cs)
- [VideoPipeline.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs)

如果你接入的是这些任务，就不能只改 pre/postprocess，还要扩展 pipeline 表达层：

- 语义分割
- 关键点
- 姿态估计
- OCR 文本框加文本串
- 多目标跟踪

## 当前项目下“新增模型”的三种接入难度

### A. 最简单：同类新权重

特征：

- 任务类型不变
- 输入尺寸可自动识别或已在元数据中声明
- 前处理一致
- 输出结构一致

做法：

- 只新增 `DL/<bundle>/`
- 写 `model.json`
- 不改 C# 代码

### B. 中等：同任务，但张量定义略有差异

特征：

- 还是检测或 sequence
- 但输出布局、score 规则、类别数、裁剪规则等不同

做法：

- 先看是否能通过 `model.json` 元数据解决
- 如果元数据表达不了，再小改现有 pre/postprocess

### C. 最重：新任务类型

特征：

- 不是现有 detection / sequence_bands
- 或者虽然是 ONNX，但输入输出组织完全不同

做法：

至少需要扩展这些层：

1. `ModelTaskType`
2. `InferenceModelKind`
3. `ModelCatalog` / `ModelPipelineFactory`
4. 新的 preprocessor
5. 新的 postprocessor
6. 新的 `IInferenceTarget`
7. `VideoPipeline.LoadModel`
8. 结果可视化和统一输出结构

## 现有检测模型的接入要求

如果新模型想走当前 `detection` 链路，建议满足以下约定。

### 输入侧约定

- 模型文件为 `.onnx`
- 单输入图像模型
- 输入维度最好是 `NCHW`
- 能接受当前预处理产物

### 输出侧约定

- 输出是三维 tensor
- 能被当前 `YoloDetectionPostprocessor` 解释
- 类别得分和框字段顺序与当前解析逻辑兼容

### 元数据建议

最少准备：

```json
{
  "id": "your-model-id",
  "displayName": "Your Model",
  "description": "Your detection model.",
  "taskType": "detection",
  "modelFile": "model.onnx",
  "classes": ["class_a", "class_b"],
  "yolo": {
    "outputLayout": "channels_first",
    "scoreMode": "class_only",
    "classCount": 2
  }
}
```

可以参考：

- [docs/templates/model.detection.template.json](C:/Users/ljia/source/repos/VideoInferenceDemo/docs/templates/model.detection.template.json)
- [DL/yolo/model.json](C:/Users/ljia/source/repos/VideoInferenceDemo/DL/yolo/model.json)

## 现有 Sequence 模型的接入要求

如果新模型想走当前 `sequence_bands` 链路，建议满足以下约定。

### 输入侧约定

- `.onnx`
- 单图输入
- 通过 `sequence.preprocess` 就能描述裁剪、resize、归一化

### 输出侧约定

- 输出能映射为按序列位置分类的 logits
- 后处理仍然是 argmax + 连续段合并

### 元数据建议

`sequence` 字段至少要写清：

- `input_name`
- `output_name`
- `input_shape`
- `output_shape`
- `class_names`
- `background_id`
- `seq_len`
- `preprocess`
- `postprocess`

可以参考：

- [docs/templates/model.sequence_bands.template.json](C:/Users/ljia/source/repos/VideoInferenceDemo/docs/templates/model.sequence_bands.template.json)
- [DL/sequence_bands/model.json](C:/Users/ljia/source/repos/VideoInferenceDemo/DL/sequence_bands/model.json)

## 建议的新增模型落地流程

### 情况 1：先尝试零代码接入

适用于怀疑模型和现有链路兼容的情况。

步骤：

1. 在 `DL/<bundle_name>/` 下放 `model.onnx`
2. 复制模板生成 `model.json`
3. 补齐 `id`、`displayName`、`taskType`、`modelFile`
4. 补齐类别和 `yolo` 或 `sequence` 元数据
5. 启动程序，刷新模型列表并尝试加载
6. 用一张已知样例验证输出是否正确

如果这里就通了，说明不需要改代码。

### 情况 2：最小改动适配

如果模型能被发现，但推理结果不对，优先排查：

1. 输入尺寸是否识别正确
2. 类别数量是否正确
3. `outputLayout` 是否正确
4. `scoreMode` 是否正确
5. sequence 的裁剪和归一化配置是否匹配训练时设置

很多问题先改 `model.json` 就能解决，不要一上来就改 pipeline。

### 情况 3：正式扩展新模型类型

如果发现新模型和现有链路本质不兼容，建议不要硬塞进 `detection` 或 `sequence_bands`。

更稳妥的做法是：

1. 新增明确的 `taskType`
2. 新增对应 `InferenceModelKind`
3. 独立实现新的 pre/postprocess
4. 独立实现新的 `IInferenceTarget`
5. 明确这种模型的统一输出与渲染方式

这样后面维护成本更低，也不容易把现有 YOLO/Sequence 逻辑搞乱。

## 对你这个问题的直接回答

“是不是直接把模型放到 `DL` 文件夹，然后适配前处理和后处理？”

可以这样理解，但建议改成下面这句更准确：

“先把模型做成 `DL` bundle 让系统发现；如果它和现有 Detection 或 Sequence 链路兼容，就主要靠 `model.json` 配置接入；如果不兼容，再补前处理、后处理，必要时扩展新的模型类型。”

换句话说：

- `DL` 解决的是“模型发现和选择”
- `model.json` 解决的是“模型元数据和轻量适配”
- pre/postprocess 解决的是“模型输入输出语义对齐”
- pipeline 扩展解决的是“新任务类型接入”

## 建议

如果你们接下来要接的是“新的 YOLO 检测模型”，建议优先按“零代码接入”路线试一次，成功概率最高。

如果你们要接的是：

- 分割
- 姿态
- OCR
- 多输入网络
- 非 YOLO 检测头

那我建议直接按“新增模型类型”来设计，不要继续复用当前 `YoloDetectionPostprocessor`。
