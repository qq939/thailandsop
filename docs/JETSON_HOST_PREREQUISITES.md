# Jetson 宿主先决条件清单

这份清单用于回答一个最实际的问题：

**把 `VideoInference.Jetson` 的发布目录拷到 Jetson 以后，宿主环境还必须提前准备什么？**

这份结论基于两类已验证信息：

- 当前 `linux-arm64 publish` 产物结构
- `WSL Ubuntu` 下实际运行 `--diagnose` 的结果

## 当前已经能随发布目录提供的

下面这些现在会跟随 `publish` 一起输出：

- `VideoInference.Jetson.dll`
- `VideoInference.Core.dll`
- `camera_config.json`
- `analysis_config.json`
- `db_config.json`
- `tcn_feature_config.json`
- `tcn_infer_config.json`
- `DL/`
- `libonnxruntime.so`
- `libonnxruntime_providers_shared.so`
- `libe_sqlite3.so`

也就是说：

- ORT 主库已随发布输出
- SQLite native 已随发布输出

## 当前不会随发布目录提供的

下面这些在当前 `publish` 结果里**没有**：

- `OpenCvSharpExtern`
- OpenCV 依赖链 `.so`
- FFmpeg 依赖链 `.so`
- CUDA / TensorRT 依赖链 `.so`

这也是为什么当前不能只拷一份发布目录就直接跑。

## 已验证出的 Linux 缺口

在 `WSL Ubuntu` 里实际运行：

```bash
dotnet VideoInference.Jetson.dll --diagnose
```

已经确认会报这两类缺口：

### 1. OpenCV 缺口

缺少：

- `OpenCvSharpExtern`

典型报错特征：

- `Unable to load shared library 'OpenCvSharpExtern'`

### 2. FFmpeg 缺口

缺少：

- `libavformat.so.60`

通常还会连带缺：

- `libavcodec.so.60`
- `libswscale.so.7`
- `libavutil.so.58`

典型报错特征：

- `Unable to load shared library 'libavformat.so.60'`

## Jetson 宿主必须准备的内容

### A. ORT

虽然 `libonnxruntime.so` 已经可以随发布输出，但在 Jetson 上仍建议显式准备一份你自己确认过的 ORT：

- 推荐位置：
  - `/opt/onnxruntime/libonnxruntime.so`

启动时显式传：

```bash
--ort-native /opt/onnxruntime/libonnxruntime.so
```

### B. OpenCvSharp native

Jetson 宿主需要保证：

- `OpenCvSharpExtern` 或 `libOpenCvSharpExtern.so`
- 它依赖的 OpenCV `.so`

当前项目还没有把这部分做成应用级发布资产，所以应先视为：

**宿主环境依赖**

### C. FFmpeg native

Jetson 宿主需要保证至少这些库在动态链接器可见路径中：

- `libavformat.so.60`
- `libavcodec.so.60`
- `libswscale.so.7`
- `libavutil.so.58`

具体小版本可能随 FFmpeg 构建变化，但当前 `WSL` 诊断里最先报出的就是：

- `libavformat.so.60`

当前仓库先以 `FFmpeg 6.1` 为落地版本，`7.0` 升级会在 Jetson 路线稳定后单独处理。

### D. CUDA / TensorRT

如果打算走：

```text
TensorRt -> Cuda -> Cpu
```

则宿主还要保证：

- CUDA `.so`
- TensorRT `.so`

能够被系统找到。

## 推荐的宿主策略

当前阶段建议把宿主环境分成两部分：

### 应用自己提供

- ORT 主库
- 应用 DLL
- 配置
- 模型
- 日志目录

### 宿主系统提供

- `OpenCvSharpExtern`
- OpenCV 依赖
- FFmpeg 依赖
- CUDA / TensorRT

## 推荐的上板检查顺序

第一次上 Jetson 时，建议严格按这个顺序核对：

1. 先确认 `dotnet` 能运行
2. 再确认 `VideoInference.Jetson.dll` 能启动 `--help`
3. 再确认 `--diagnose` 能运行
4. 再确认 ORT 不报主库缺失
5. 再确认 OpenCV probe 不报 `OpenCvSharpExtern`
6. 再确认 FFmpeg probe 不报 `libavformat`
7. 最后再接相机和模型

## 推荐的诊断命令

```bash
dotnet /opt/videoinference/camera0/VideoInference.Jetson.dll \
  --diagnose \
  --model-root /opt/videoinference/camera0/DL \
  --log-dir /opt/videoinference/camera0/logs \
  --ort-native /opt/onnxruntime/libonnxruntime.so \
  --providers tensorrt,cuda,cpu
```

## 看到这些错误时怎么理解

### 如果报 `OpenCvSharpExtern`

说明：

- `OpenCvSharp` 托管层已加载
- 但 OpenCV native 没准备好

### 如果报 `libavformat.so.60`

说明：

- FFmpeg 托管层已加载
- 但 FFmpeg native 没准备好

### 如果诊断最终显示 `Selected provider: Cpu`

说明：

- ORT 主库能起
- 但 CUDA / TensorRT provider 当前不可用，或依赖链未满足

## 当前一句话结论

**Jetson 现在已经可以做 Linux 编译、发布和诊断，但真机运行前，宿主仍必须先补齐 OpenCvSharp native、FFmpeg native，以及 CUDA/TensorRT 依赖。**
