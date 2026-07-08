# ROI 预处理层设计

## 背景

当前 `Core` 中已经有较清晰的模型预处理层：

- YOLO 预处理负责 `letterbox / normalize / layout`
- Sequence 预处理负责 `crop / resize / normalize`

这套结构已经可以覆盖当前主要模型需求，但如果后面继续增加：

- 固定区域检测
- 多个候选视野
- 不同模型绑定不同局部区域
- 基于业务规则切换裁剪区域

那么继续把逻辑堆进具体 `Preprocessor`，会让每个模型的预处理越来越重，也会让坐标回映越来越难维护。

因此建议在模型预处理之前，引入一层独立的 ROI 层。

## 目标

将复杂的图像空间裁剪、区域选择、局部视野组织，从模型预处理层中前移，形成：

- 原始输入层
- ROI 层
- 模型预处理层
- 后处理层

其中：

- ROI 层只负责“从原图选出区域，并维护几何关系”
- 模型预处理层只负责“把 ROI 图像变成模型张量”
- 后处理层只负责“把结果从模型输入空间还原到 ROI / 原图空间”

## 分层结构

### 1. 原始输入层

输入来源：

- 相机帧
- 视频帧

输出：

- 原始整帧图像
- 原图尺寸和时间信息

### 2. ROI 层

职责：

- 定义 ROI 区域
- 从原图生成 ROI 视图或 ROI 副本
- 维护 ROI 到原图的几何映射
- 支持单 ROI 或 ROI 列表

不负责：

- normalize
- letterbox
- tensor 排布
- 模型特定像素变换

### 3. 模型预处理层

职责：

- 基于 ROI 图像做模型所需的标准化处理
- resize / letterbox / normalize / blob / tensor
- 维护“模型输入空间到 ROI 空间”的几何关系

不负责：

- 业务层的区域选择策略
- 原图上的 ROI 组织

### 4. 后处理层

职责：

- 将模型输出结果还原到 ROI 坐标系
- 再从 ROI 坐标系还原到原图坐标系
- 输出统一结果对象

## 推荐对象模型

### RoiRegion

表示一个 ROI 定义。

建议字段：

- `RoiId`
- `Name`
- `X`
- `Y`
- `Width`
- `Height`
- `Enabled`
- `Tags`
- `TargetModels`

语义：

- 坐标统一基于原图
- 支持固定矩形 ROI
- 以后可扩展旋转框、多边形或动态 ROI

### RoiImageContext

表示从原图裁剪出的 ROI 图像及其上下文。

建议字段：

- `RoiRegion`
- `SourceWidth`
- `SourceHeight`
- `RoiWidth`
- `RoiHeight`
- `OffsetX`
- `OffsetY`

职责：

- 提供 ROI 图像到原图坐标系的回映能力

### RoiTransform

表示 ROI 图像和原图之间的空间关系。

建议提供方法：

- `AdjustPointToSource(...)`
- `AdjustRectToSource(...)`

说明：

- 对于固定矩形 ROI，这层通常只是加上偏移
- 未来如果支持缩放、旋转、多边形裁剪，这层仍然可以扩展

### RoiSelectionPlan

表示一次推理前要使用哪些 ROI。

建议字段：

- `Mode`
- `Regions`
- `PrimaryRegion`
- `AllowEmpty`

模式可包括：

- `FullFrame`
- `SingleRoi`
- `MultiRoi`

## 与现有预处理层的关系

### 当前 YOLO

当前 `YoloDetectionPreprocessor` 负责：

- 输入图像转 BGR
- `letterbox`
- `blobFromImage`
- `ImageAdjustmentGeometry`

接入 ROI 层后：

- 上游先给它 ROI 图像
- 它继续只关心：
  - ROI 图像 -> 模型输入张量
  - 模型输入空间 -> ROI 空间

后处理时：

- 先用 `ImageAdjustmentGeometry` 回到 ROI 坐标
- 再用 `RoiTransform` 回到原图坐标

### 当前 Sequence

当前 `SequenceBandPreprocessor` 已经有“裁底部 + 中心裁宽”这种预处理逻辑。

接入 ROI 层后，建议区分：

- 业务上的固定观察区域：放 ROI 层
- 模型固有的输入裁剪规则：继续放 Sequence 预处理层

换句话说：

- ROI 层解决“看哪块区域”
- Sequence 预处理解决“这块区域如何喂给模型”

## 坐标回映建议

建议采用两段回映：

1. 模型输入空间 -> ROI 空间
2. ROI 空间 -> 原图空间

优点：

- 每层职责清楚
- 更容易调试
- 多模型、多 ROI 时不容易混乱

## 多 ROI 的扩展方向

后续可以支持：

- 单模型单 ROI
- 单模型多 ROI 顺序推理
- 单模型多 ROI 合批推理
- 多模型各自绑定不同 ROI

建议先从最简单的开始：

- 单模型单 ROI
- 或单模型 `ROI 列表 + 顺序推理`

等行为稳定后，再考虑合批。

## 配置建议

后续如果要做配置驱动，可在模型或相机配置中加入：

- `roi_mode`
- `roi_id`
- `roi_ids`
- `roi_regions`

ROI 定义建议和模型定义分离：

- 相机级 ROI：更适合描述视野区域
- 模型级 ROI 绑定：更适合描述模型使用哪个区域

## 先不做的内容

当前不建议一开始就做：

- 动态 ROI 跟踪
- 多边形 ROI
- 旋转 ROI
- GPU ROI 裁剪
- 多 ROI 自动调度合批

先把矩形 ROI 和回映链路跑顺，收益最高。

## 实施建议

建议后续按下面顺序落地：

### Phase 1

引入最小 ROI 模型：

- `RoiRegion`
- `RoiTransform`
- `RoiImageContext`

先支持单矩形 ROI。

### Phase 2

让一个模型预处理支持“输入可以是原图，也可以是 ROI 图像”。

优先从 YOLO 开始验证。

### Phase 3

把后处理回映改成：

- 模型输入 -> ROI
- ROI -> 原图

### Phase 4

再考虑模型配置与 ROI 绑定，以及多 ROI 推理。

## 结论

复杂预处理不应继续无限堆入具体模型预处理器。

更合理的长期结构是：

- ROI 层处理“看哪里”
- 模型预处理层处理“怎么喂模型”
- 后处理层处理“怎么映射回来”

这样后面无论是继续扩 YOLO、Sequence，还是做 Jetson/Avalonia 复用，结构都会更稳。
