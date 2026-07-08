# Jetson 迁移方案

本文档说明如何将 `VideoInferenceDemo` 适配到 Jetson Ubuntu 上运行，同时保持核心逻辑只有一套共享代码。

配套运行文档见：

- [JETSON_HEADLESS_RUN.md](./JETSON_HEADLESS_RUN.md)

## 目标

目标不是“一个应用二进制跑所有平台”。

真正的目标是：

- 一套共享的核心代码，负责 pipeline、推理、录像、存储、相机抽象
- 一个 Windows 桌面宿主，保留当前 WPF 工作流
- 一个 Jetson 宿主，用于 Ubuntu 部署

这样可以做到核心逻辑只维护一份，同时把平台相关的 UI 和原生集成隔离开。

## 简短结论

Jetson 适配是可行的，但当前项目有比较强的 Windows 属性。

目前的主要阻碍有：

- WPF UI 和 `System.Windows` 依赖
- Windows 专用的 native 加载逻辑
- Windows 专用的相机 SDK 集成
- Windows 专用的 OpenCV 和 FFmpeg runtime 打包方式
- 当前推理初始化路径不统一

因此，推荐的架构是：

- `VideoInference.Core`
- `VideoInference.Desktop`
- `VideoInference.Jetson`

这个方案优于把所有东西都塞进一个应用项目，再用大量条件编译去硬兼容。

## 与 `JetsonYoloOrtDemo` 的适配关系

Jetson demo 已经验证了最关键的几件事：

- `.NET` 在 `linux-arm64` 上可运行
- 自定义 ONNX Runtime `.so` 可以被正确加载
- `TensorRT -> CUDA -> CPU` 的 provider 回退逻辑可行
- 在 Jetson 上直接创建 ORT session 是可行的

因此，Jetson demo 很适合作为下面几块的参考实现：

- native 库预加载策略
- provider 回退策略
- ORT session 工厂设计

最好的复用方式不是把 demo 直接并进当前项目，而是把它的 ONNX Runtime 初始化思路抽成共享推理层。

## 哪些部分已经适合共享

下面这些模块很适合提取成共享核心项目：

- `VideoPipeline`
- `SegmentedVideoRecorder`
- `FrameRecorder`
- SQLite 写入和 schema 相关代码
- `Camera/CameraAbstractions.cs` 中的相机抽象
- `SequenceOnnxModel.cs` 中的序列模型推理
- `TcnOnnxInferenceEngine.cs` 中的 TCN 推理
- 分析和结果落地相关逻辑

这些模块目前大多数已经基本不依赖 UI。

## 当前缺口

## 1. UI 是 Windows 专属

当前应用是一个 WPF 程序：

- `VideoInferenceDemo.csproj` 目标框架是 `net8.0-windows`
- 开启了 `UseWPF`
- `MainWindow.xaml` 及相关 ViewModel 直接依赖 WPF 类型

典型的 Windows UI 耦合包括：

- `System.Windows`
- `Dispatcher`
- `ImageSource`
- `WriteableBitmap`
- XAML 窗口和对话框

这意味着现有桌面 UI 不能直接在 Jetson Ubuntu 上运行。

## 2. Native 加载逻辑是 Windows 专属

`MainViewModel.cs` 中存在 Windows DLL 加载逻辑：

- `SetDllDirectory`
- `SetDefaultDllDirectories`
- `AddDllDirectory`
- `LoadLibrary`
- `FreeLibrary`
- `kernel32.dll` 的 P/Invoke

这部分必须替换为平台抽象。

对于 Jetson，更接近的做法应该是：

- 受控的 `LD_LIBRARY_PATH`
- 显式预加载 ORT native 库
- Linux 专用的启动自检

## 3. 相机支持目前还不是跨平台的

当前有两个相机 provider：

- `OpenCvCameraProvider`
- `HikCameraProvider`

其中 `HikCameraProvider` 依赖 `MvCameraControl.Net.dll`，目前是按 Windows 引用方式集成的。

这是采集侧最大的一个平台缺口。

实际含义是：

- 基于 OpenCV 的相机采集，是最容易先在 Jetson 上落地的路径
- HikRobot 在 Jetson 上的支持，应当视为一个独立的 Linux provider 实现任务

现有 provider 抽象已经是一个不错的基础，但 Linux provider 仍然需要补实现。

## 4. Runtime 打包现在是 Windows 导向

当前项目引用了：

- `OpenCvSharp4.runtime.win`
- `Sdcb.FFmpeg.runtime.windows-x64`
- 面向 Windows 的 ONNX Runtime GPU 包

Jetson 需要完全不同的 native 打包策略：

- Linux ARM64 的 OpenCV runtime
- Linux ARM64 的 FFmpeg runtime，或者系统级 FFmpeg 部署方案
- 你自己编译的 Jetson ONNX Runtime `.so`

这不只是切一个编译开关，而是整个运行时打包和启动逻辑都要变成平台感知的。

## 5. 推理初始化路径不统一

当前项目里推理有不止一条路径：

- `SequenceOnnxModel.cs` 里直接创建 ORT session
- `TcnOnnxInferenceEngine.cs` 里直接创建 ORT session
- `Yolo11Detector.cs` 里 YOLO 走的是 `DeploySharp`

对 Jetson 来说，这样有风险，因为：

- `SequenceOnnxModel` 和 `TcnOnnxInferenceEngine` 比较容易往 Jetson demo 的方式统一
- `Yolo11Detector` 绑在 `DeploySharp` 上，它和 Jetson demo 那套直接 ORT 的思路并不一致

建议：

- 引入共享的 ONNX Runtime session 工厂
- 把 YOLO 推理逐步从当前 `DeploySharp` 依赖迁移到直接使用 ORT

这样最有利于 Windows 和 Jetson 两边推理行为保持一致。

## 6. 单项目条件编译 technically 可行，但并不理想

从技术上说，可以做成一个应用项目，多目标编译：

- 多目标框架
- 条件化包引用
- 条件化源码包含
- 平台专属 partial class

但对当前这个代码库来说，这会让项目文件变得很重，因为里面同时要处理：

- WPF/XAML 与 非 WPF 启动方式
- Windows 专属对话框
- Windows 专属 native 加载
- Windows 专属相机 SDK 引用
- 不同平台的 runtime 载荷

最后虽然“还是一个仓库”，但不会是一个好维护的单宿主项目。

建议是：

- 保持一个仓库
- 保持一套共享核心库
- 使用两个宿主项目

这样既能保证核心业务逻辑只维护一份，也不会把应用项目变成一个很复杂的条件编译矩阵。

## 推荐的目标结构

## `VideoInference.Core`

职责：

- pipeline 编排
- 推理抽象和 session 工厂
- 录像抽象与实现
- 数据库写入
- 模型元数据与预处理
- 相机 provider 接口
- 共享配置模型

不应依赖：

- WPF
- `System.Windows`
- Windows 专属 P/Invoke
- Windows 专属 SDK 引用

## `VideoInference.Desktop`

职责：

- WPF 窗口
- 桌面端 ViewModel
- Windows native preload 行为
- Windows 相机 provider 注册
- 桌面配置对话框

## `VideoInference.Jetson`

职责：

- console 或 service 宿主
- Linux 启动和 native 自检
- Jetson ORT native preload
- Linux 相机 provider 注册
- 无界面配置和运行监控流程

这个宿主一开始可以先做成 console 程序，后面再包一层 service。

## UI 路线建议

当前最推荐的 UI 路线是：

- Windows 端继续使用 WPF
- Jetson 端使用 Avalonia
- 两端先共享 `Core`

这条路线的优点是：

- 现有 Windows UI 不需要立即重写
- Jetson 端可以直接使用支持 Linux 的桌面 UI 技术
- 业务逻辑共享，而不是硬共享一套平台相关界面
- 风险明显低于“一次性把 WPF 全部迁到新框架”

### 为什么 Jetson 端优先选 Avalonia

Avalonia 官方支持 Windows、macOS、Linux 等平台，并且桌面 Linux 文档明确说明：

- Avalonia 在 Linux 上直接面向 X11
- Ubuntu 和 Debian 的 ARM64 桌面 Linux 在支持矩阵中
- Wayland 仍在持续推进中

这意味着对于 Jetson Ubuntu，这条路线是现实可行的。

相比之下，.NET MAUI 官方主平台不包含 Linux，因此不适合作为 Jetson UI 主方案。

### 后期 Windows 是否可以迁到 Avalonia

可以，而且这是一个合理的后续方向。

也就是说，长期路线可以分为两个阶段：

#### 阶段一

- Windows 继续 WPF
- Jetson 使用 Avalonia
- 双方共享 `Core`

#### 阶段二

- 新增一个 Windows 的 Avalonia 宿主
- 逐步把 WPF 的界面功能迁过去
- 等 Avalonia 版 Windows 足够稳定后，再决定是否退休 WPF

这样做的优点是：

- 不需要一次性重写现有 Windows UI
- Jetson 端可以先把 Linux UI 跑通
- 未来如果 Avalonia 在 Windows 上稳定，可以进一步复用更多界面层
- 整个迁移过程可以分阶段推进，风险更低

### 为了未来界面复用，现在必须守住的边界

如果希望未来 Windows 也迁到 Avalonia，并尽量复用 UI 层逻辑，那么现在需要尽量保证：

- `Core` 里绝不出现 WPF 或 Avalonia 类型
- ViewModel 的业务逻辑尽量不绑定 WPF 专属类型
- 图片显示、线程调度、文件对话框、通知等都放到平台适配层
- UI 只负责展示和交互，不反向承载业务流程

建议长期把界面相关再拆成三层理解：

- `Core`
  - 完全跨平台
- `UI.Shared`
  - 放跨 WPF / Avalonia 可共享的 ViewModel、页面状态、命令、表单模型
- `UI.Wpf` / `UI.Avalonia`
  - 只放各自控件、窗口、绑定、图像显示适配

这样后面如果 Windows 迁到 Avalonia，主要迁的是视图层，而不是整套业务。

## 推荐实施顺序

## Phase 1. 抽出共享 Core

先把下面这些区域移动或重构到平台无关项目中：

- `VideoPipeline`
- `FrameRecorder` 与录像实现
- SQLite 结果写入
- 分析引擎
- 模型元数据和后处理
- 相机抽象接口

成功标准：

- core 项目不再依赖 `System.Windows`
- desktop 宿主改为引用 core 项目

## Phase 2. 统一 ONNX Runtime 初始化

创建一个共享的 ORT session 工厂，设计思路参考 `JetsonYoloOrtDemo`。

它应该统一负责：

- provider 顺序
- provider 回退
- native preload 钩子
- 通用 session options
- 模型 session 创建

成功标准：

- `SequenceOnnxModel` 改为使用共享 session 工厂
- `TcnOnnxInferenceEngine` 改为使用共享 session 工厂
- 新的 YOLO 路径也复用这一套 session 工厂

## Phase 3. 替换 `DeploySharp` 的 YOLO 推理依赖

这是推理层最关键的一步清理。

如果想让 Jetson 路线更干净，YOLO 最好重构成：

- 直接用 ORT 加载模型
- 预处理由项目自己维护
- 后处理由项目自己维护
- provider 选择由共享 ORT session 工厂统一控制

成功标准：

- YOLO 主推理路径不再依赖 `DeploySharp`
- 同一套 YOLO 推理代码能同时跑在 Windows 和 Jetson 上

## Phase 3.5. 引入模型装配工厂

当 `YOLO / Sequence / TCN` 都已经拆成“预处理 / 运行时 / 后处理”三段后，下一步最值得做的，不是继续增加更多具体模型类，而是把“怎么把这些零件组装起来”统一收口。

当前虽然结构已经比以前清晰很多，但模型装配逻辑仍然分散在各个具体类里：

- `Yolo11Detector` 自己决定用哪个 runtime、哪个 preprocessor、哪个 postprocessor
- `SequenceOnnxModel` 自己决定怎么拼装序列模型链路
- `TcnOnnxInferenceEngine` 自己决定怎么拼装窗口输入和分类后处理

这意味着：

- 相同类型的模型还不能真正通过配置复用同一套后处理
- 新增模型时，往往还是要新建一个“知道如何拼装所有零件”的具体类
- 上层调用者仍然需要知道比较多的模型细节

### 这一层要解决的问题

建议引入一个“模型描述 + 组装工厂”的概念：

- `ModelDescriptor`
  - 描述模型路径、任务类型、输入输出名、后处理类型、运行时类型、可选参数
- `IModelPipelineFactory`
  - 根据 `ModelDescriptor` 选择并创建对应的 runtime / preprocessor / postprocessor

这样做之后，上层不再直接写死：

- `new Yolo11Detector(...)`
- `new SequenceOnnxModel(...)`
- `new TcnOnnxInferenceEngine(...)`

而是改成：

- 把模型描述交给工厂
- 由工厂决定如何组装模型执行链

### 这样做的好处

- 同类模型可以共享一份后处理实现
- 模型的“任务类型”和“推理后端”解耦
- Windows 和 Jetson 宿主都只依赖统一的装配入口
- 后续如果增加新的模型格式，只需要补新的后处理或新的装配规则，而不是继续膨胀具体模型类

### 适合当前项目的最小落地方式

不需要一开始就做非常泛化的大框架，先做一个够用的最小方案即可：

- 在 `Core` 中新增 `ModelDescriptor`
- 在 `Core` 中新增 `ModelPipelineFactory`
- 先支持三类描述：
  - `detection / yolo`
  - `sequence_band`
  - `tcn_classification`
- 先让工厂只负责选择：
  - `OrtModelRuntime`
  - 对应的 preprocessor
  - 对应的 postprocessor

### 成功标准

- 上层不再直接依赖具体模型类的构造细节
- 相同输出格式的模型可以通过配置复用后处理
- 后续新增模型时，优先补“模型描述 + 组装规则”，而不是继续堆更多大类

## Phase 4. 引入平台专属宿主

在 core 稳定后：

- 保留当前 WPF 应用作为 `VideoInference.Desktop`
- 新建 `VideoInference.Jetson` 作为 console/service 宿主

成功标准：

- desktop 宿主行为保持现状
- Jetson 宿主可以无界面启动 pipeline、推理、录像、结果写库

## Phase 5. 补齐 Linux 打包和相机 provider

完善 Jetson 部署细节：

- Linux ARM64 runtime 打包
- ORT `.so` 部署策略
- OpenCV Linux runtime 策略
- FFmpeg Linux runtime 策略
- Linux 相机 provider 注册
- 可选的 HikRobot Linux 支持

成功标准：

- Jetson 宿主能在目标设备上端到端启动运行

## 建议的项目规则

为了让共享代码长期保持清晰，建议遵守这些规则：

- `Core` 中不允许出现 `System.Windows`
- pipeline 和推理代码不允许依赖 WPF 类型
- `Core` 中不允许出现平台专属 P/Invoke
- 所有 native 加载都通过平台专属服务处理
- 相机 provider 按宿主进行注册
- 推理 provider 选择统一收敛

## 最现实的第一阶段目标

Jetson 最适合的第一阶段里程碑是：

- 无 UI
- 一路 OpenCV 兼容相机输入
- 使用 Jetson 自定义 ORT 库进行推理
- MKV 录像
- SQLite 输出

这个里程碑故意绕开了最难的部分：

- WPF 替代
- HikRobot Linux 集成
- 多相机运行运维工具

等这一阶段稳定后，Desktop 和 Jetson 就可以持续共享同一套核心逻辑。

## 最终建议

保持一个仓库，保持一套共享 core，但不要强行做成一个应用项目。

长期推荐结构：

- 一个共享核心库
- 一个 Windows 桌面宿主
- 一个 Jetson Ubuntu 宿主

这样团队真正只需要维护一套核心业务逻辑，同时把平台差异控制在该控制的位置上。
