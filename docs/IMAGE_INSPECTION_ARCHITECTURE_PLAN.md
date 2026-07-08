# 图像检测项目架构方案

## 1. 背景

当前仓库的主目标是视频行为分析，包括：

- 相机采集
- 视频逐帧推理
- 时序状态跟踪
- SOP / FSM 在线分析
- Jetson 运行与部署

现在新增的需求是“图像检测 / 视觉识别”，更接近传统机器视觉场景，例如：

- 单张图像检测
- 批量图像检测
- ROI 区域规则判断
- 几何关系判断
- 缺陷 / 位置 / 装配状态判定
- 结果导出与复检

这两类需求有一部分底层能力可以共用，但上层流程、UI 交互、业务规则明显不同。

因此不建议通过长期分支把当前项目“改造成另一个产品”，而建议在同一仓库内新增一个图像检测应用，并复用已有核心能力。

## 2. 结论

采用“同仓库双应用，共用核心能力”的方案：

- 保留现有视频主线
  - `src/VideoInference.Desktop`
  - `src/VideoInference.Jetson`
- 新增图像检测应用
  - `src/ImageInspection.Desktop`
- 共用底层能力
  - `src/VideoInference.Core`

这意味着：

- 视频行为分析继续沿用现有结构演进
- 图像检测使用新的应用入口和新的业务编排
- 视觉推理、模型发现、通用结果结构等能力继续共享
- 视频时序分析与图像规则分析分层处理，不强行揉成一套

## 3. 为什么不建议用长期分支

长期分支不适合承载两个长期演化的产品方向，主要问题有：

1. 功能会持续分叉
   - 视频分析强调连续帧、时间窗口、状态机
   - 图像检测强调单图判断、批量处理、ROI 与规则输出

2. 修复会重复搬运
   - 共用代码修复后，需要在多个长期分支间反复挑拣
   - 容易出现功能不同步、遗漏、冲突积累

3. 主线语义会变乱
   - 当前项目命名、结构、运行入口明显偏 `VideoInference`
   - 如果直接在主产品中混入另一套图像工作流，后续维护成本会持续升高

4. 版本管理会更痛苦
   - 分支适合短期开发、功能试验、阶段性交付
   - 不适合长期承载两个并列业务产品

## 4. 总体架构

建议整体拆成三层：

### 4.1 视觉执行层

职责：

- 模型目录发现
- 任务定义装配
- ONNX / 其他 runtime 推理执行
- 推理结果载荷建模
- 基础输入预处理

这一层继续复用当前 `VideoInference.Core` 的既有能力，主要位于：

- `src/VideoInference.Core/Inference`
- `src/VideoInference.Core/Vision`

该层同时服务：

- 视频逐帧检测
- 单张图像检测
- 批量图像检测

### 4.2 编排层

职责：

- 组织输入源
- 调用视觉任务
- 驱动业务执行流程
- 汇总结果并返回 UI

这一层建议按产品线区分：

- 视频方向
  - 相机
  - 视频流
  - 录像
  - Jetson
  - 会话管理
- 图像方向
  - 单图加载
  - 文件夹批处理
  - 导出
  - 人工复检

这一层不建议强行共用。

### 4.3 业务分析层

职责：

- 根据推理结果做业务判断

视频方向：

- 帧窗口统计
- SOP / FSM 时序推进
- OK / NG 在线状态判定

图像方向：

- ROI 规则
- 包含 / 相交 / 中心点判断
- 缺陷、偏移、漏装、错装判断
- 单图或批量结果决策

这两类规则体系不建议直接合并。

## 5. 建议的项目结构

第一阶段建议如下：

```text
src/
  VideoInference.Core/
    Analysis/           # 保留给视频时序分析
    Inference/          # 共用
    Vision/             # 共用
    Geometry/           # 新增，放通用几何能力
    Inspection/         # 新增，放图像检测规则与结果模型

  VideoInference.Desktop/   # 现有视频桌面应用
  VideoInference.Jetson/    # 现有 Jetson 运行入口
  ImageInspection.Desktop/  # 新增图像检测桌面应用
```

### 5.1 `Analysis`

保留现有语义：

- 只负责视频时序分析
- 继续承载 SOP / FSM / 在线分析
- 不承载单图规则判定

### 5.2 `Geometry`

建议新增，作为纯几何与空间关系工具层。

建议放入：

- `BoundingBox`
- `Point2D`
- `Polygon`（如果后续需要）
- `GeometryRelations`
- `RoiDefinition`
- `MeasurementHelpers`

典型能力：

- 包含
- 相交
- 中心点是否落入区域
- 目标被覆盖比例
- 目标间距离
- 尺寸 / 长宽比 / 面积

### 5.3 `Inspection`

建议新增，作为图像检测业务层。

建议放入：

- `InspectionContext`
- `InspectionRuleDefinition`
- `InspectionResult`
- `InspectionFinding`
- `InspectionDecision`
- `IInspectionRule`
- `InspectionRuleEngine`

用途：

- 接收视觉任务输出
- 根据 ROI / 几何关系 / 阈值规则进行业务判断
- 输出单图或批量图像的结果

## 6. 可直接复用的能力

从当前仓库看，以下内容适合直接复用，或只做轻量抽象后复用：

### 6.1 模型与任务层

- `ModelCatalog`
- `ModelCatalogVisionTaskMapper`
- `VisionTaskDefinition`
- `VisionRuntimeKind`

### 6.2 推理执行层

- ONNX runtime 执行链
- 模型目录扫描
- 推理结果标准化输出

### 6.3 结果载荷层

- `DetectionPayload`
- `SegmentationPayload`
- `HandLandmarksPayload`
- `VisionTaskExecutionResult`

### 6.4 相机抽象层

如果图像检测后续也需要接工业相机，可以复用：

- `CameraProvider`
- `CameraSession`
- 相机配置模型

但图像检测第一阶段不一定需要把相机接入做进来，可以先从文件 / 图片目录加载开始。

## 7. 不建议直接复用的部分

以下部分建议明确保留给视频产品线：

- `Pipeline`
- `Recording`
- `Jetson`
- `CameraSessionWorkspace`
- 视频 Dashboard / Workspace 流程
- `Analysis` 中的在线 SOP / FSM 逻辑

原因：

- 它们高度依赖“连续帧 / 会话 / 时间顺序”
- 而图像检测的核心流程是“单图输入 -> 规则判定 -> 结果导出”

如果把这两类流程硬并在一起，会让模型、UI、状态管理和配置结构越来越混乱。

## 8. 图像检测应用的最小流程

新应用 `ImageInspection.Desktop` 第一阶段建议只做最小闭环：

```text
ImageSource
  -> VisionTask
  -> VisionTaskExecutionResult
  -> InspectionRuleEngine
  -> InspectionResult
  -> UI / Export
```

### 8.1 输入源

建议第一阶段支持：

- 单张图片打开
- 文件夹批量加载

后续再扩展：

- 相机抓拍
- 在线图像流

### 8.2 UI 能力

建议第一阶段提供：

- 图片浏览
- 模型 / 任务选择
- 结果叠加显示
- ROI 配置
- 规则参数配置
- 批量执行
- 导出 CSV / JSON / 标注图

### 8.3 结果层

建议输出两层结果：

1. 视觉结果
   - 原始检测框 / mask / landmarks

2. 业务结果
   - OK / NG
   - 失败原因
   - 命中规则
   - 定位信息

## 9. 图像检测规则引擎建议

不要复用当前 `AnalysisEngine` 来做图像规则。

原因：

- `AnalysisEngine` 天然围绕帧窗口、状态历史、step 转移
- 图像规则更适合“一次输入，一次判定”

建议新增：

```csharp
public interface IInspectionRule
{
    InspectionFinding? Evaluate(
        VisionTaskExecutionResult input,
        InspectionContext context);
}
```

再由 `InspectionRuleEngine` 统一编排：

```csharp
public sealed class InspectionRuleEngine
{
    public InspectionResult Evaluate(
        VisionTaskExecutionResult input,
        InspectionContext context);
}
```

### 9.1 规则类型建议

第一阶段优先支持：

- 目标是否存在
- 目标数量是否正确
- 目标中心点是否在 ROI 内
- A 是否在 B 内
- A 与 B 是否重叠到指定阈值
- 宽高、面积、长宽比是否在阈值范围
- 多目标组合是否满足工艺位置关系

### 9.2 结果表达建议

建议 `InspectionResult` 包含：

- `Decision`
  - `Ok`
  - `Ng`
  - `Warning`
- `Findings`
- `PrimaryMessage`
- `AnnotatedRegions`
- `Metrics`

这样后续无论是 UI 展示、导出报表还是现场对接都比较稳定。

## 10. 第一阶段实施方案

建议按以下顺序推进：

### 阶段 1：先建新应用，不做大重构

新增：

- `src/ImageInspection.Desktop`

目标：

- 能加载图片
- 能选模型
- 能执行现有视觉任务
- 能显示结果

### 阶段 2：补图像规则层

新增：

- `src/VideoInference.Core/Inspection`
- `src/VideoInference.Core/Geometry`

目标：

- 能做 ROI 和几何关系判断
- 能给出 OK / NG 结果

### 阶段 3：支持批处理与导出

目标：

- 文件夹批量检测
- 输出 CSV / JSON
- 保存带叠加结果的图像

### 阶段 4：再看是否抽公共 UI / 服务

当图像应用稳定后，再评估：

- 是否从 `VideoInference.Desktop` 抽共用服务
- 是否需要把模型管理、配置管理进一步平台化

## 11. 当前仓库中的落地边界

建议本阶段先遵守以下边界：

1. 不修改现有 `VideoInference.Desktop` 的主流程语义
2. 不把图像规则塞进 `AnalysisEngine`
3. 不先做大规模重命名
4. 不先拆仓库
5. 先让新应用跑起来，再逐步抽公共层

## 12. 命名建议

推荐新项目名称：

- `ImageInspection.Desktop`

也可选：

- `VisionInspection.Desktop`

更不推荐：

- `VideoInference.Desktop` 直接改名
- `VideoInferenceV2`
- `TraditionalVision`

原因：

- `ImageInspection` 语义清楚
- 和当前 `VideoInference` 并列关系清晰
- 后续好理解、好维护

## 13. 风险与控制

### 风险 1：过早大重构

风险：

- 把现有视频项目打乱
- 新需求还没落地就先消耗大量时间

控制：

- 新建应用先跑通最小链路
- 共用层只做必要抽象

### 风险 2：图像与视频规则混在一起

风险：

- 配置模型混乱
- 业务语义冲突
- 后续迭代困难

控制：

- `Analysis` 与 `Inspection` 明确分层

### 风险 3：抽象过头

风险：

- 为了“未来通用”过度设计
- 当前交付速度下降

控制：

- 只抽已证明被两边都需要的能力
- 先应用后抽象

## 14. 推荐决策

最终推荐如下：

1. 在当前仓库内新增 `ImageInspection.Desktop`
2. 继续复用 `VideoInference.Core` 中的推理与任务层
3. 保留 `Analysis` 只负责视频时序分析
4. 新增 `Geometry` 与 `Inspection` 负责图像检测规则
5. 先做“单图 / 批量图片 + 推理 + ROI 规则 + 导出”的最小闭环

## 15. 下一步建议

建议下一步直接进入工程落地设计，输出以下内容：

1. `ImageInspection.Desktop` 的目录树
2. 第一批类清单
3. 配置文件结构
4. 最小 UI 页面结构
5. 第一阶段实现顺序

如果开始实施，建议优先目标是：

- 打开图片
- 选择现有模型
- 执行检测
- 叠加显示
- 输出 OK / NG

先拿到第一个可运行版本，再继续演进。
