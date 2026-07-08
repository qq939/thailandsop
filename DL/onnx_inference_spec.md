# ONNX 推理接入说明

## 1. 文档目的

这份文档用于给推理端说明当前模型的任务定义、输入输出格式、预处理、后处理、以及 C# `onnxruntime` 接入时必须保持一致的约定。

目标是：

- 推理端拿到 ONNX 模型和本文档后，可以独立开始设计并实现推理程序
- 保证推理端与训练端/验证端在输入输出定义上完全一致
- 避免因为预处理或后处理不一致而导致部署效果偏差

## 2. 交付文件

当前应交付给推理端的文件：

- ONNX 模型：`sequence_model.onnx`
- ONNX 元数据：`sequence_model.meta.json`
- 本说明文档：`onnx_inference_spec.md`

参考实现文件：

- 训练模型定义：训练仓库内的 `train_sequence_model.py`
- Python 推理参考：训练仓库内的 `infer_sequence_model.py`
- ONNX 导出脚本：训练仓库内的 `export_onnx.py`

## 3. 任务定义

### 3.1 业务任务

输入一张完整图像，输出沿图像高度方向的类别序列。

该任务不是检测，也不是分割，而是：

- 整图输入
- 高度方向稠密分类
- 后处理后得到最终层序列

### 3.2 类别定义

类别固定为 4 类：

- `0 = background`
- `1 = A`
- `2 = B`
- `3 = C`

### 3.3 序列方向

序列方向固定为：

- `bottom_to_top`

即：

- 序列索引 `0` 对应图像底部附近
- 序列索引越大，位置越靠近图像顶部

最终输出的层序列也按从下到上的顺序组织。

## 4. 模型信息

### 4.1 模型结构

当前部署模型是：

- `ResNet18 backbone`
- 保留纵向特征
- 沿宽度维做平均池化
- 1D sequence head 输出 `4 x 256`

这是一个分类模型，不输出边界框，不输出 mask。

### 4.2 ONNX 信息

当前 ONNX 模型约定：

- 输入名：`input`
- 输出名：`logits`
- opset：`17`
- 支持动态 batch

### 4.3 输入输出形状

输入张量形状：

- `[N, 3, 512, 256]`

输出张量形状：

- `[N, 4, 256]`

其中：

- `N` 是 batch size
- `4` 是类别数
- `256` 是高度序列长度 `seq_len`

## 5. 预处理

推理端必须严格复现以下预处理步骤。

### 5.1 图像读取

- 读取原图
- 颜色顺序必须是 `RGB`
- 不要用 `BGR`

### 5.2 尺寸变换

必须直接 resize 到固定尺寸：

- `width = 256`
- `height = 512`

注意：

- 当前实现是**直接拉伸 resize**
- 不做等比例缩放
- 不做 padding
- 不做裁剪

这点非常重要，推理端如果改成“保持比例 + padding”，结果会和训练端不一致。

### 5.3 数值归一化

原始像素处理方式：

1. `uint8 -> float32`
2. 除以 `255.0`，映射到 `[0, 1]`
3. 按 ImageNet 均值方差标准化

均值：

- `mean = [0.485, 0.456, 0.406]`

标准差：

- `std = [0.229, 0.224, 0.225]`

公式：

```text
x = pixel / 255.0
x[0] = (x[0] - 0.485) / 0.229
x[1] = (x[1] - 0.456) / 0.224
x[2] = (x[2] - 0.406) / 0.225
```

### 5.4 张量布局

输入布局必须是：

- `NCHW`

即：

- `N = batch`
- `C = 3`
- `H = 512`
- `W = 256`

## 6. ONNX 输出解释

模型输出 `logits` 的形状为：

- `[N, 4, 256]`

含义是：

- 对于每张图
- 对于每个高度位置 `seq_idx in [0, 255]`
- 模型给出 4 个类别的原始分数

注意：

- 这是 `logits`
- 不是 softmax 概率
- 最终类别可直接通过 `argmax` 获得

如果需要置信度，可对类别维做 softmax，再取最大概率。

## 7. 后处理

### 7.1 基础后处理

对于单张图，建议按下面步骤做：

1. 对 `logits[:, seq_idx]` 做 `argmax`
2. 得到长度为 `256` 的类别序列
3. 对短噪声段做平滑
4. 合并连续相同类别段
5. 去掉背景段
6. 输出最终层序列

### 7.2 推荐短段平滑规则

推荐参数：

- `min_segment_length = 4`

规则与当前 Python 参考实现保持一致：

如果某一连续段长度 `< min_segment_length`，则把这一段并入邻居，具体规则如下：

1. 如果整条序列只有一个段，则不处理
2. 如果是第一个段，则并入后一个段
3. 如果是最后一个段，则并入前一个段
4. 如果左右邻居类别相同，则并入该共同类别
5. 如果左右邻居类别不同，则并入更长的那个邻居
6. 如果左右邻居长度相同，则并入左边邻居

处理方式是循环执行，直到没有长度小于阈值的段为止。

### 7.3 合并连续相同段

平滑后，将连续相同类别合并为一个段。

例如：

```text
[0, 0, 1, 1, 1, 2, 2, 1, 1]
```

合并后得到：

```text
background, A, B, A
```

### 7.4 背景处理

最终层序列输出时：

- 默认忽略 `background`

例如：

```text
background, A, B, A, background
```

最终输出应为：

```text
[A, B, A]
```

## 8. 序列索引与图像 y 范围映射

### 8.1 基本定义

设：

- 原图高度为 `originalHeight`
- 序列长度为 `seqLen = 256`
- 每个 band 的高度为：

```text
bandHeight = originalHeight / seqLen
```

### 8.2 当前方向为 bottom_to_top

由于当前方向是 `bottom_to_top`，对段 `[seqStart, seqEnd)`，其在原图中的 y 范围按如下计算：

```text
yBottom = originalHeight - seqStart * bandHeight
yTop    = originalHeight - seqEnd   * bandHeight
y0 = min(yTop, yBottom)
y1 = max(yTop, yBottom)
```

因此：

- `seqStart = 0` 对应底部
- `seqEnd` 越大，位置越往上

### 8.3 最终段输出建议

每个段建议输出如下信息：

- `classId`
- `className`
- `seqStart`
- `seqEnd`
- `length`
- `y0`
- `y1`
- 可选 `confidence`

## 9. 推荐的 C# 推理流程

### 9.1 推理主流程

推荐的主流程如下：

1. 读取图像
2. 转 `RGB`
3. resize 到 `256 x 512`
4. 归一化为 `float32`
5. 组织成 `NCHW`
6. 调用 ONNX Runtime 推理
7. 读取输出 `logits`
8. 对类别维做 `argmax`
9. 进行短段平滑
10. 合并连续相同段
11. 去掉背景段
12. 得到最终层序列与每段 y 范围

### 9.2 C# 侧推荐输出对象

建议推理端定义两个层次的输出：

1. 原始稠密输出

- `int[256] SequenceIds`
- `float[256] Confidence`

2. 最终结构化输出

- `List<PredictedSegment>`

其中 `PredictedSegment` 建议包含：

- `int ClassId`
- `string ClassName`
- `int SeqStart`
- `int SeqEnd`
- `int Length`
- `float Y0`
- `float Y1`
- `float Confidence`

## 10. C# 侧必须保持一致的关键点

以下几项如果不一致，效果会明显变差：

1. 颜色必须是 `RGB`，不是 `BGR`
2. resize 必须直接拉伸到 `256 x 512`
3. 布局必须是 `NCHW`
4. 输入类型必须是 `float32`
5. 标准化均值方差必须完全一致
6. 输出类别顺序必须是 `background, A, B, C`
7. 序列方向必须按 `bottom_to_top`
8. 后处理建议使用 `min_segment_length = 4`

## 11. 推荐的验证方式

推理端接入完成后，建议至少用以下方式自测：

1. 拿 5 到 10 张训练/验证中已知结果的图片做回归测试
2. 与 Python 推理脚本输出逐张对比
3. 比较以下内容是否一致：

- 最终层序列
- 每段 `seqStart/seqEnd`
- 每段 `y0/y1`

如果这些信息和 Python 结果一致，通常说明 C# 侧的预处理、ONNX 推理和后处理已经对齐。

## 12. Python 参考结果

当前 Python 参考推理脚本支持：

- 直接输出最终层序列
- 输出每段对应的 y 范围
- 生成可视化图片

参考命令：

```bash
conda activate trainb-sequence
python scripts\infer_sequence_model.py --image-dir imagesLabels\images --glob "*.jpg" --checkpoint outputs\sequence_train\best.pt --device cuda --min-segment-length 4 --output-dir outputs\sequence_infer_all
```

## 13. 部署建议

对 C# 侧的建议是：

- ONNX Runtime 推理只负责模型前向
- 预处理和后处理放在 C# 本地实现
- `min_segment_length` 作为可配置项保留
- 是否保留背景段，也作为可配置项保留

这样后续如果现场数据有轻微变化，推理端只需要调后处理参数，不需要立刻重训模型。

## 14. 当前结论

当前交付给推理端的核心约定可以压缩为一句话：

> 输入 RGB 图像，直接拉伸到 `256 x 512`，按 ImageNet 均值方差做 `float32 NCHW` 归一化，送入 ONNX 得到 `[N, 4, 256]` 的 `logits`，对类别维做 `argmax` 得到长度为 `256` 的序列，再按 `min_segment_length = 4` 做短段平滑，合并连续相同段，忽略背景段，最终输出从下向上的层序列以及每段对应的 y 范围。
