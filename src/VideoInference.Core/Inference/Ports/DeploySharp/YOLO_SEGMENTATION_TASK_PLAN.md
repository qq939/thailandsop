# YOLO Segmentation 任务接入计划

## 目标

在现有 YOLO Detection ONNX 链路的基础上，增加 YOLO Segmentation 任务类型。后续只要在模型目录放入分割 ONNX 和 `model.json`，应用就能把它识别为分割任务，并在视频画面上叠加实例 mask、检测框和标签。

## 模型目录约定

推荐的 `model.json`：

```json
{
  "id": "sample-yolo-seg",
  "displayName": "Sample YOLO Seg",
  "description": "YOLO segmentation ONNX model.",
  "taskType": "segmentation",
  "modelFile": "best-seg.onnx",
  "classesFile": "classes.json",
  "yolo": {
    "outputLayout": "channels_first",
    "scoreMode": "class_only",
    "classCount": 3,
    "detectionOutputName": "output0",
    "prototypeOutputName": "output1",
    "maskThreshold": 0.5
  }
}
```

`taskType` 支持 `seg`、`segment`、`segmentation`、`yolo_seg`、`yolo_segment`、`yolo_segmentation`。如果输出名为空，程序会按张量形状自动猜测 detection head 和 prototype head。

## 当前第一版范围

- 复用现有 `YoloDetectionPreprocessor`、letterbox 几何映射和 DeploySharp 迁移来的矩形 NMS。
- 支持常见 YOLOv8/YOLO11 seg ONNX 输出：
  - detection head: `[1, features, boxes]` 或 `[1, boxes, features]`
  - prototype head: `[1, maskChannels, maskHeight, maskWidth]` 或 `[1, maskHeight, maskWidth, maskChannels]`
- 支持按类别分数筛选、按 bbox 做 NMS、按 mask coefficients 和 prototype 生成实例 mask。
- 渲染阶段显示半透明 mask、bbox 和 label。

## 后续待验证

- 用真实导出的 YOLO seg ONNX 校验输出名、输出 shape、score 模式和 mask 系数数量。
- 如需要落库，建议先只存 bbox/class/score；mask 可后续再设计轮廓或压缩格式。
- 如果 mask 生成成为性能瓶颈，可把逐实例 mask 的生成范围进一步裁剪到 prototype 空间，或增加缓存/并行策略。
