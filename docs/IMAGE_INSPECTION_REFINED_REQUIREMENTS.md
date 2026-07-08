# 图像检测需求收敛版

## 1. 这次需求的真实形态

这次不是做一个泛化的“图片打开 -> 跑模型 -> 看结果”的小工具。

更准确地说，这是一个偏工位 / 产线场景的图像检测系统，特点是：

- 以“触发一次，检测一次”为核心工作模式
- 需要接入外部通讯协议
- 不强调视频录制与视频保存
- 需要多 ROI 管理
- 每个 ROI 支持旋转
- 每个 ROI 都可以裁剪并旋正成水平图
- 旋正后的 ROI 需要方便送入固定尺寸模型
- 需要一套独立的 ROI 设置界面
- 数据存储以结果为主，表结构可以简单
- 数据库需要支持 `SQLite` 和 `MySQL`
- 参数配置需要按“产品型号 + 位置号”区分配方

因此，这个新应用虽然可以复用现有视觉推理能力，但产品形态已经明显不是视频行为分析，也不是视频在线分析的变体。

## 2. 与之前理解相比，需要修正的地方

之前的理解更偏：

- 单图 / 批量图像检测工具
- ROI 规则判定
- 离线导出

这次补充后，新的重点变成：

1. 有触发机制
2. 有对外通讯
3. 有工位级 ROI 配置
4. 有 ROI 旋转裁剪预处理
5. 有轻量结果存储
6. 不保存视频
7. 有明确的配方键：`产品型号 + 位置号`

也就是说，这个系统更接近：

`触发式工业视觉检测应用`

而不是普通的图片检测桌面工具。

## 3. 产品定位建议

建议新应用定位为：

- `ImageInspection.Desktop`

它的职责不是视频录制和行为分析，而是：

- 接收触发
- 采图
- 按产品型号和位置号解析当前配方
- 按 ROI 列表裁剪 / 旋转 / 推理
- 做规则判定
- 回传结果
- 落库结果

## 4. 核心业务流程

建议把主流程理解成：

```text
Trigger
  -> Acquire Image
  -> ROI List Build
  -> Crop And Rotate Each ROI
  -> Vision Inference
  -> Inspection Rules
  -> Save Result
  -> Protocol Response / Signal Output
```

更细一点：

1. 外部触发到来
2. 系统抓拍一张图
3. 读取该工位当前配置的 ROI 列表
4. 对每个启用的 ROI：
   - 进行裁剪
   - 根据角度旋正到水平
   - 送入对应模型或统一模型
5. 汇总所有 ROI 的检测结果
6. 做 OK / NG 规则判定
7. 保存检测结果
8. 通过通讯协议输出结果或回包

这里的“当前配置”不应只按工位固定，而应优先由：

- `产品型号`
- `位置号`

共同决定。

## 5A. 配方概念

这是这次补充里最重要的新边界。

### 5A.1 配方键

新系统中的大部分运行参数，都应通过以下组合来区分：

- `ProductModel`，产品型号
- `PositionNo`，位置号

建议把它们作为一组显式配方键：

```csharp
public sealed record InspectionRecipeKey(
    string ProductModel,
    string PositionNo);
```

### 5A.2 哪些配置应由配方决定

建议以下内容默认由配方决定，而不是散落在各个模块：

- ROI 列表
- 标定参数
- 使用哪些模型
- 模型执行顺序
- 是否有级联检测
- 规则阈值
- 通讯返回模板中的业务字段

### 5A.3 配方与工位的关系

建议这样理解：

- 工位决定“系统装在哪、相机是谁、协议怎么接”
- 配方决定“这次图像该怎么看、用哪些 ROI、跑哪些模型、怎么判定”

也就是：

- `Station` 偏设备级配置
- `Recipe` 偏业务级配置

## 5B. 不要把检测主流程过度设计

你这里特别强调了一个点，这很重要：

- 具体检测不要过度设计

这意味着我们不需要一上来造很重的工作流引擎或复杂 DSL。

更合适的做法是先明确一个简单稳定的执行入口。

### 5B.1 建议的核心入口

建议第一阶段以一个明确的执行函数为中心：

```csharp
InspectionCycleResult Execute(
    Mat originalImage,
    string productModel,
    string positionNo);
```

或者如果想更利于扩展：

```csharp
InspectionCycleResult Execute(InspectionRequest request);
```

其中 `InspectionRequest` 至少包含：

- 原图
- 产品型号
- 位置号
- 触发信息
- 时间戳

### 5B.2 这个入口内部应做什么

这个 `Action` / `Execute` 入口内部，按你描述的真实需求，应能完成：

1. 根据 `产品型号 + 位置号` 找配方
2. 查询 ROI 列表
3. 查询标定参数
4. 根据 ROI 裁剪并旋正子图
5. 根据配方解析应使用的模型引用
6. 执行一个或多个模型
7. 支持级联执行
8. 汇总推理结果
9. 输出最终结果对象

### 5B.3 先不要做得太复杂

第一阶段不建议先做：

- 通用可视化工作流编排器
- 复杂规则脚本引擎
- 过度抽象的节点图系统

第一阶段更适合：

- 配方解析
- 简单执行链
- 结果输出

## 5C. 建议的执行器形态

建议把核心执行器控制在比较简单的语义：

```csharp
public interface IInspectionAction
{
    InspectionCycleResult Execute(InspectionRequest request);
}
```

其内部依赖可以拆成轻量服务：

- `IInspectionRecipeProvider`
- `IRoiProvider`
- `ICalibrationProvider`
- `IModelReferenceResolver`
- `IRoiCropper`
- `IInspectionResultStore`
- `IInspectionProtocolAdapter`

但外部入口仍然保持一个明确动作，而不是一套很重的平台。

## 5. 触发机制

这是本次需求的核心之一。

### 5.1 触发式而不是持续视频流

新系统默认不应以“持续视频流 + 每帧分析”为中心。

默认工作模式应是：

- 外部事件触发
- 单次采图
- 单次检测
- 单次返回结果

### 5.2 触发源抽象建议

建议新增一层触发抽象：

```csharp
public interface IInspectionTriggerSource
{
    event EventHandler<InspectionTriggerEventArgs> Triggered;
}
```

### 5.3 第一阶段建议支持的触发源

建议按优先级设计：

1. 手动触发
   - UI 按钮
   - 便于调试

2. 软件触发
   - 由通讯指令触发一次检测

3. 相机触发
   - 如果后续工业相机支持软触发 / 硬触发，可继续接入

### 5.4 触发上下文

建议 `InspectionTriggerEventArgs` 至少带：

- `StationId`
- `TriggerId`
- `TriggerTime`
- `Source`
- `Payload`（可选）
- `ProductModel`（可选，若触发方提供）
- `PositionNo`（可选，若触发方提供）

这样后续保存结果和协议返回时都能追踪。

## 6. 通讯协议支持

这是第二个关键需求。

### 6.1 通讯的职责

通讯层建议负责：

- 接收触发命令
- 回传检测结果
- 输出状态
- 上报错误
- 传递或回填 `产品型号` / `位置号`

### 6.2 不要把协议逻辑写进 UI 或规则层

协议适配应独立成一层，不要直接写进 ViewModel 或检测规则中。

建议抽象：

```csharp
public interface IInspectionProtocolAdapter
{
    string Name { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task PublishResultAsync(InspectionCycleResult result, CancellationToken cancellationToken);
}
```

### 6.3 第一阶段协议策略

当前先不要过早绑定具体协议实现，但架构上要预留扩展位。

建议第一阶段按“适配器模式”做，后续可以挂：

- TCP
- HTTP
- Modbus TCP
- 串口
- PLC 对接协议

如果暂时不确定协议种类，也建议先做：

- `ManualProtocolAdapter`
- `LoopbackProtocolAdapter`

用于联调。

## 7. 数据保存

这次的重点不是保存视频，而是保存检测结果。

### 7.1 明确不保存视频

新应用默认不承接：

- 视频录制
- 视频分段
- 视频归档
- 行为回放

这部分和当前 `Recording` 主线彻底区分开。

### 7.2 保存内容

建议第一阶段只保存必要数据：

- 一次检测的主记录
- 产品型号
- 位置号
- 每个 ROI 的子结果
- 最终 OK / NG
- 关键指标
- 原图路径或快照路径（可选）

### 7.3 数据库抽象

建议新增统一结果仓储接口：

```csharp
public interface IInspectionResultStore
{
    Task SaveAsync(InspectionCycleResult result, CancellationToken cancellationToken);
}
```

实现上支持：

- `SqliteInspectionResultStore`
- `MySqlInspectionResultStore`

### 7.4 表结构原则

你已经明确说了表结构可以简单，所以建议不要一开始就做很复杂的范式化设计。

建议第一阶段两张主表就够：

1. `inspection_cycle`
   - 一次触发 / 一次检测一条

2. `inspection_roi_result`
   - 一次检测中的每个 ROI 一条

### 7.5 推荐字段

`inspection_cycle`

- `id`
- `station_id`
- `product_model`
- `position_no`
- `trigger_id`
- `trigger_time`
- `image_path`
- `final_decision`
- `summary_message`
- `created_at`

`inspection_roi_result`

- `id`
- `cycle_id`
- `roi_id`
- `roi_name`
- `model_id`
- `model_alias`
- `decision`
- `score`
- `metrics_json`
- `finding_json`

这样足够轻，也足够后面扩展。

## 8. ROI 模型要重新定义

这是本次需求最关键的结构点之一。

### 8.1 ROI 不是单个，而是一组列表

一个图像上可能有多个 ROI，所以模型不能是单个 `Roi`，而应是：

- `List<RoiDefinition>`

并且每个 ROI 都应有独立配置。

同时这组 ROI 默认应挂在配方下，而不是全局唯一配置。

### 8.2 ROI 需要支持旋转

矩形 ROI 已经不够。

每个 ROI 至少需要这些字段：

- `Id`
- `Name`
- `Enabled`
- `CenterX`
- `CenterY`
- `Width`
- `Height`
- `Angle`
- `ModelId`（可选）
- `SortOrder`

### 8.3 ROI 的真正用途

本次 ROI 不只是“画个框给人看”，而是：

- 作为检测输入区域
- 裁出子图
- 根据角度旋转回水平
- 再送入固定尺寸模型

这意味着 ROI 层应明确承担：

1. 空间定义
2. 裁剪
3. 旋正
4. 坐标回映

另外还要能支持：

5. 通过 `产品型号 + 位置号` 查询对应 ROI 列表

## 9. ROI 预处理链路

建议标准化成以下流程：

```text
Original Image
  -> ROI Rect With Angle
  -> Crop
  -> Rotate To Horizontal
  -> Resize / Normalize / Tensor
  -> Model Inference
```

### 9.1 为什么要先旋正

因为你这里的 ROI 更像固定工位、固定姿态区域：

- 通过 ROI 角度把区域旋平
- 后面的模型输入就更稳定
- 同一个模型更容易复用在多个 ROI 上

### 9.2 需要保存的几何关系

为了把模型结果映射回原图，运行时要保存：

- ROI 原始中心点
- ROI 宽高
- ROI 角度
- 裁剪后图像尺寸
- 旋正变换矩阵
- ROI 图像到原图坐标的逆变换

建议新增：

- `RoiRuntimeContext`
- `RoiImageContext`
- `RoiTransform`

## 10. ROI 与模型绑定关系

建议先支持两种模式：

1. 每个 ROI 绑定一个模型
2. 所有 ROI 共用当前选中的模型

第一阶段如果想简单一点，可以先做：

- 全部 ROI 共用一个主模型

但数据结构最好一开始就保留 `ModelId` 字段，不然后面很容易返工。

### 10.1 模型管理要支持“多配方复用同一模型”

这次需求里还有一个关键点：

- 多个型号
- 多个位置号

可能会共用同一个模型。

所以模型管理不应简单理解为“每个配方拷贝一份模型配置”，而应做成：

- 模型资源独立管理
- 配方只引用模型

建议分成两层：

1. `ModelCatalogEntry`
   - 表示一个真实模型资源

2. `RecipeModelBinding`
   - 表示某个配方如何使用某个模型

这样可以支持：

- 多个配方引用同一个模型
- 一个配方按顺序使用多个模型
- 一个配方做级联推理

### 10.2 建议的绑定形态

建议一个配方里能描述：

- 模型引用列表
- 每个模型绑定哪些 ROI
- 模型执行顺序
- 是否依赖前一个模型输出

例如：

```text
Recipe A
  -> Model 1 on ROI-1
  -> Model 2 on ROI-2
  -> Model 3 on ROI-2, depends on Model 2
```

但第一阶段不需要上升到复杂 DAG，先支持“顺序执行链”就够。

## 10A. 标定参数

你已经明确提到“查询标定”，所以标定不能只是隐含逻辑。

建议标定作为显式配置源存在：

```csharp
public interface ICalibrationProvider
{
    CalibrationContext Get(string productModel, string positionNo);
}
```

标定的用途可以包括：

- 像素到物理尺寸换算
- ROI 坐标换算
- 图像矫正
- 规则阈值中的物理尺寸判断

第一阶段即使只先支持最简单的标定数据结构，也建议把查询接口先留出来。

## 11. ROI 设置界面

你描述得已经很明确了，这个界面应作为单独功能设计，而不是临时弹个属性框。

### 11.1 布局

建议采用左右结构：

- 左边：图像画布
- 右边：ROI 列表和操作区

### 11.2 左侧图像区

左侧需要支持：

- 显示当前图像
- 绘制多个 ROI
- 选中 ROI
- 拖拽移动
- 缩放宽高
- 旋转角度
- 高亮当前选中 ROI

### 11.3 右侧 ROI 列表

右侧列表建议显示：

- ROI 名称
- 启用状态
- 绑定模型
- 坐标摘要
- 角度摘要

### 11.4 右侧按钮

你提到“几个按钮操作”，建议第一阶段至少有：

- `新增 ROI`
- `删除 ROI`
- `复制 ROI`
- `上移`
- `下移`
- `启用/禁用`
- `保存`
- `取消`

### 11.5 右侧属性区

建议可编辑：

- 名称
- `X / Y`
- `Width / Height`
- `Angle`
- 绑定模型
- 是否启用

### 11.6 一个很关键的预览能力

建议右侧或底部增加一个小预览：

- 显示“当前 ROI 裁剪并旋正后的图”

这个能力非常重要，因为它能直接验证：

- ROI 角度是否对
- 裁剪区域是否正确
- 模型输入是否稳定

## 12. 新系统的主配置应包含哪些内容

建议新应用配置至少拆成四类：

1. 站位 / 工位配置
2. 配方配置
3. 协议配置
4. 存储配置

### 12.1 工位配置

- `StationId`
- `CameraId`
- `TriggerMode`
- `DefaultModelId`

### 12.2 配方配置

建议按 `ProductModel + PositionNo` 组织：

- ROI 列表
- 标定
- 模型绑定
- 规则参数

也就是：

- `RecipeKey`
- `Rois`
- `Calibration`
- `ModelBindings`
- `Parameters`

### 12.3 ROI 配置

ROI 本身仍是独立对象：

- `List<RoiDefinition>`

但它应归属在配方下使用。

### 12.4 协议配置

- 协议类型
- 地址
- 端口
- 超时
- 回包格式

### 12.5 存储配置

- `Provider = SQLite | MySQL`
- 连接字符串
- 是否保存图像快照
- 快照目录

## 13. 应用分层建议

新的 `ImageInspection.Desktop` 建议按下面的职责分层：

### 13.1 Trigger

- 接收触发
- 生成检测周期上下文

### 13.1A Recipe

- 根据产品型号和位置号解析配方
- 提供 ROI、标定、模型绑定和参数

### 13.2 Acquisition

- 从相机或本地输入取图

### 13.3 ROI Processing

- 处理 ROI 列表
- 裁剪
- 旋正
- 坐标变换

### 13.4 Vision Execution

- 执行现有模型推理
- 支持多模型顺序执行
- 支持简单级联

### 13.5 Inspection Rules

- 根据推理结果做 OK / NG 判定

### 13.6 Persistence

- 保存结果到 SQLite / MySQL

### 13.7 Protocol

- 向外回传结果

## 14. 哪些现有能力可以复用

仍然建议复用：

- `VideoInference.Core/Inference`
- `VideoInference.Core/Vision`
- 模型目录扫描
- 推理结果 payload 结构
- 相机抽象层（如果后续需要）

但不建议复用：

- `Recording`
- `Pipeline` 里的视频主流程语义
- `Analysis` 中的时序状态分析
- 视频工作区 UI

## 15. 这版需求下的最小闭环

如果按你这次的真实需求，最小闭环不应该再是“打开图片跑一下”。

更合理的最小闭环是：

1. 手动触发一次检测
2. 采一张图
3. 传入 `产品型号 + 位置号`
4. 解析配方
5. 按 ROI 列表裁剪并旋正
6. 跑一个或多个模型
7. 对每个 ROI 生成结果
8. 汇总成总结果
9. 保存到 SQLite
10. 在 UI 上显示结果

这个闭环才更贴近你后面真正要接协议、要接工位的形态。

## 16. 当前建议的项目结论

基于你这次补充的需求，我对项目形态的理解是：

- 这是一个触发式图像检测系统
- 不是视频行为分析分支
- 不是纯图片批处理小工具
- 需要单独的应用入口
- 需要独立的 ROI 配置能力
- 需要配方管理能力
- 需要协议接入层
- 需要轻量存储层
- 需要围绕“单次检测周期”设计主流程

因此，方向上仍然建议：

1. 新建 `ImageInspection.Desktop`
2. 复用 `VideoInference.Core` 的视觉执行能力
3. 新增：
   - `Inspection`
   - `Geometry`
   - `Recipe`
   - `Trigger`
   - `Protocol`
   - `Persistence`
4. 让 ROI 成为一等公民，而不是临时加在模型预处理里的小功能

## 16A. 推荐的简化主入口

综合这次补充后，推荐把主执行入口先定成下面这种风格：

```csharp
public sealed record InspectionRequest(
    Mat OriginalImage,
    string ProductModel,
    string PositionNo,
    string? StationId = null,
    string? TriggerId = null);

public interface IInspectionAction
{
    InspectionCycleResult Execute(InspectionRequest request);
}
```

执行逻辑建议固定成：

1. 解析配方
2. 取 ROI
3. 取标定
4. 解析模型引用
5. 裁剪和旋正 ROI
6. 执行模型链
7. 形成结果
8. 结果输出到：
   - SQL
   - 图片渲染
   - 通讯发送

这个形式足够简单，也最贴近你描述的真实使用方式。

## 17. 下一步应该出的设计

基于这版理解，下一步最值得继续落的不是泛化架构，而是具体工程设计：

1. `ImageInspection.Desktop` 的目录树
2. 配方、触发、协议、存储、ROI、规则几个模块的类清单
3. ROI 配置界面的 ViewModel 结构
4. `InspectionAction.Execute(...)` 的内部时序
5. SQLite / MySQL 的最小表结构
6. 一次检测周期的时序图

如果继续往下做，建议下一份文档直接落这些内容。
