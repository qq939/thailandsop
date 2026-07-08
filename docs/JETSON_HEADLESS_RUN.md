# Jetson Headless 运行说明

本文档说明如何在 Jetson Ubuntu 上以无界面方式运行 `VideoInference.Jetson`。

## 目标

当前 headless 宿主负责：

- 发现并加载模型
- 打开 OpenCV 相机
- 启动推理
- 按配置启动录像
- 输出运行状态日志

当前不包含：

- Avalonia UI
- HikRobot Linux provider
- Linux 打包脚本

## 前置条件

运行前建议先确认下面几项：

- 已安装 .NET 8 运行时或 SDK
- 已准备好 Jetson 对应的 ONNX Runtime `.so`
- 相机可通过 OpenCV 打开
- 模型目录 `DL` 已随程序部署，或通过参数指定
- `camera_config.json` 已随程序部署，或通过参数指定

## 目录约定

默认情况下，程序会从当前输出目录查找：

- `./camera_config.json`
- `./DL`

如果你的部署目录不同，可以通过参数覆盖：

- `--camera-config`
- `--model-root`

## 基本启动方式

最小启动命令：

```bash
dotnet run --project src/VideoInference.Jetson -- \
  --camera-index 0 \
  --model-root ./DL
```

这条命令表示：

- 使用 OpenCV 相机 `0`
- 从 `./DL` 自动发现模型
- 自动选择模型目录中的第一个可用模型

## 推荐启动方式

如果已经有 Jetson 版 ORT `.so`，建议显式传入 provider 顺序和 native 路径：

```bash
dotnet run --project src/VideoInference.Jetson -- \
  --camera-index 0 \
  --model-root ./DL \
  --model yolo \
  --ort-native /opt/onnxruntime/libonnxruntime.so \
  --providers tensorrt,cuda,cpu \
  --device-id 0 \
  --trt-fp16 \
  --trt-engine-cache \
  --trt-engine-cache-path ./trt_engine_cache
```

如果暂时只想验证 CPU 或 CUDA，可改成：

```bash
dotnet run --project src/VideoInference.Jetson -- \
  --camera-index 0 \
  --model-root ./DL \
  --providers cuda,cpu
```

## 本机快速验证

如果本机已经有跑通过的 `/home/ams123/JetsonYoloOrtDemo`，现在可以直接复用那里的 ORT GPU runtime：

```bash
./scripts/jetson/run-jetson-local.sh \
  --diagnose \
  --camera-index 0 \
  --model-root /home/ams123/VideoInferenceDemo/src/VideoInference.Jetson/bin/Debug/net8.0/linux-arm64/DL \
  --log-dir /home/ams123/VideoInferenceDemo/src/VideoInference.Jetson/bin/Debug/net8.0/linux-arm64/logs \
  --providers tensorrt,cuda,cpu \
  --trt-fp16 \
  --trt-engine-cache \
  --trt-engine-cache-path /home/ams123/VideoInferenceDemo/src/VideoInference.Jetson/bin/Debug/net8.0/linux-arm64/trt_engine_cache
```

脚本会自动：

- 构建 `VideoInference.Jetson`
- 构建并同步与当前 `OpenCvSharp4` 匹配的 `libOpenCvSharpExtern.so`
- 在输出目录缺少 FFmpeg 6.1 runtime 时，自动编译并同步 `libavformat.so.60` 等 `.so`
- 把 `JetsonYoloOrtDemo/native` 下的 ORT GPU `.so` 同步到 Jetson 输出目录
- 注入 `LD_LIBRARY_PATH`
- 直接启动 `VideoInference.Jetson.dll`

首次执行会额外下载 Ubuntu 22.04 的 OpenCV `deb`、克隆 `opencvsharp` 源码并在仓库 `.cache/jetson-opencv` 下构建 native runtime；如果输出目录里还没有 FFmpeg 6.1 `.so`，也会在 `.cache/jetson-ffmpeg` 下本地编译一套 runtime，后续会复用下载缓存。

如果 `JetsonYoloOrtDemo` 不在默认目录，可通过环境变量覆盖：

```bash
JETSON_YOLO_ORT_DEMO_ROOT=/your/path/JetsonYoloOrtDemo ./scripts/jetson/run-jetson-local.sh --diagnose
```

当前脚本也会自动把仓库里的海康 Linux runtime 加进 `LD_LIBRARY_PATH`：

- `ThirdParty/CameraHIK/linux-arm64`

这一步只负责让 Jetson 进程能够解析海康 `.so`，还不代表已经完成了 Linux 版 Hik provider 接入。

## 常用参数

相机相关：

- `--camera-config <path>`
- `--camera <id-or-name>`
- `--camera-index <index>`
- `--fps <value>`

模型相关：

- `--model-root <path>`
- `--model <id-or-name>`
- `--conf <value>`
- `--iou <value>`

录像相关：

- `--record`
- `--no-record`
- `--record-root <path>`
- `--duration <seconds>`

ORT 相关：

- `--ort-native <path>`
- `--providers <csv>`
- `--device-id <id>`
- `--trt-fp16`
- `--trt-engine-cache`
- `--trt-engine-cache-path <path>`

## 模型选择规则

程序会先从 `--model-root` 指定的目录调用 `ModelCatalog` 发现模型。

如果传了 `--model`，则按下面顺序匹配：

- 模型 `id`
- 模型 `displayName`
- 模型文件名去扩展名

如果没有传 `--model`，则默认使用发现到的第一个模型。

## 相机选择规则

如果传了 `--camera-index`，程序会直接使用该 OpenCV 相机索引。

如果没有传，则会从 `camera_config.json` 中按以下顺序选择：

- 与 `--camera` 匹配的 `id` 或 `name`
- 第一个 `Enabled && AutoStart` 的相机
- 第一个 `Enabled` 的相机
- 第一个相机

## 运行日志

启动后控制台会持续输出这些信息：

- 当前相机
- 当前模型
- 当前 ORT provider 顺序
- 周期性 `stats`
- `pipeline-error`
- `pipeline-ended`

典型日志类似：

```text
Starting Jetson headless host. camera="Camera 0", provider=opencv, index=0, fps=30.00, recording=False, root="...", model="YOLO", ortProviders=TensorRt->Cuda->Cpu
[stats] camera="Camera 0" fps(c/i/r)=29.97/29.80/29.80 queue=0/0 pts=12345 drop=0/0/0
```

## 常见排查

如果提示没有模型：

- 检查 `--model-root`
- 检查 `DL` 下是否存在合法模型 bundle
- 检查 `model.json` 和模型文件是否一起部署

如果提示相机 provider 不支持：

- 当前 Jetson headless 只支持 `opencv`
- 需要确保配置中的相机 `ProviderId` 为 `opencv`，或直接使用 `--camera-index`

如果推理没起来：

- 检查 `--ort-native` 路径
- 检查 `--providers` 顺序
- 检查 Jetson 上 CUDA / TensorRT 运行时是否可用

如果录像没开启：

- 检查相机配置中的录像开关
- 或显式传 `--record`
- 检查 `--record-root` 是否有写权限

## 当前建议

第一次上板验证时，建议按这个顺序做：

1. 先用 `--camera-index` 跑通相机
2. 再确认 `--model-root` 能发现模型
3. 再加 `--ort-native` 和 `--providers`
4. 最后再打开录像

## 当前边界

现在 `run-jetson-local.sh` 已经会自动准备 OpenCV native，并且我已在本机确认程序能走到真实相机打开阶段。

如果仍然打不开相机，优先检查：

- `/dev/video*` 是否存在
- 当前用户是否有相机访问权限
- 设备是否更适合用 `--camera-source` + GStreamer pipeline

当前 `FFmpeg` 诊断仍可能提示缺少 `libavformat.so.60`，这主要影响 `Sdcb.FFmpeg` 探针和录像链路，不影响默认 `recording=false` 的 OpenCV 相机启动验证。

当前仓库已经整理进一份 HikRobot `linux-arm64` native runtime，但 Jetson 侧还没有接通 Linux 版 Hik provider。

当前仓库已经把 `Sdcb.FFmpeg` 调整到 `6.1` 线，Linux 下期望的是：

- `libavformat.so.60`
- `libavcodec.so.60`
- `libavutil.so.58`
- `libswscale.so.7`

注意：

- `Sdcb.FFmpeg` 官方只提供 `windows-x64` 的 runtime NuGet，没有 `linux-arm64` runtime NuGet
- Ubuntu `noble` 的 `libavformat60` 虽然版本匹配，但在这台 `jammy` 主机上会要求 `GLIBC_2.38`，不能直接拿来用
- 所以后续如果要补齐录像/FFmpeg probe，需要准备一套 **为 Ubuntu 22.04 / Jetson 编译的 FFmpeg 6.1 runtime**

一旦你手上已经有可用的 FFmpeg 6.1 `.so` 目录，可以直接这样接入：

```bash
FFMPEG_ROOT=/path/to/ffmpeg/lib ./scripts/jetson/run-jetson-local.sh --diagnose
```

## 版本说明

当前为了先快速跑通 Jetson 宿主，仓库先固定在 `FFmpeg 6.1` 线。

后续如果要统一到 `7.0`，建议和 Windows/Desktop 一起做一次整体验证，再回切 `Sdcb.FFmpeg` 包版本和 Linux runtime 目标库名。
