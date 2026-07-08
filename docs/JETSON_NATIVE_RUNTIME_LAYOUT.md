# Jetson Native Runtime 布局说明

这份文档说明 `VideoInference.Jetson` 在 Ubuntu / Jetson 上对原生运行时的推荐布局。

目标不是把所有 `.so` 都打进仓库，而是把“哪些由系统提供、哪些由部署目录提供、程序怎么找到它们”说清楚。

## 总体原则

当前建议分三层：

1. **系统层**
   由 Jetson / Ubuntu 系统或你手工安装提供：
   - CUDA
   - TensorRT
   - OpenCV 依赖链
   - FFmpeg 依赖链

2. **ORT 层**
   由你自己准备的 ONNX Runtime `.so` 提供：
   - `libonnxruntime.so`
   - 以及对应 provider 依赖

3. **应用层**
   由 `VideoInference.Jetson` 发布目录提供：
   - `VideoInference.Jetson.dll`
   - 配置文件
   - 模型目录
   - 日志目录

## 推荐部署目录

建议一份实例一个目录，例如：

```text
/opt/videoinference/camera0/
  VideoInference.Jetson.dll
  VideoInference.Core.dll
  camera_config.json
  analysis_config.json
  db_config.json
  tcn_feature_config.json
  tcn_infer_config.json
  DL/
  logs/
```

如果后面有多实例，就继续扩成：

```text
/opt/videoinference/camera0/
/opt/videoinference/camera1/
/opt/videoinference/camera2/
```

## ORT `.so` 推荐位置

当前最推荐两种方式：

### 方式 A：统一放到系统目录

例如：

```text
/opt/onnxruntime/libonnxruntime.so
```

然后启动时显式传：

```bash
--ort-native /opt/onnxruntime/libonnxruntime.so
```

这是当前最清晰、最可控的方式。

### 方式 B：放到应用目录旁边

例如：

```text
/opt/videoinference/camera0/native/libonnxruntime.so
```

然后启动时传：

```bash
--ort-native /opt/videoinference/camera0/native
```

当前 `OrtNativeLibraryLoader` 会优先尝试：

- 显式传入的文件
- 显式传入的目录
- `${AppContext.BaseDirectory}/native`
- `${CurrentDirectory}/native`
- `${AppContext.BaseDirectory}`

在 Linux 下，它会查找：

- `libonnxruntime.so`
- `libonnxruntime.so.1`
- 以及目录中所有 `libonnxruntime.so*`

所以目录方式也是可行的。

## `LD_LIBRARY_PATH` 与 `--ort-native`

两者不是完全替代关系。

### `--ort-native`

作用：

- 告诉程序“主 ORT 库或 ORT 目录”在哪里
- 让程序主动预加载 `libonnxruntime.so`

适合：

- 你自己编了 ORT
- 不想把它装进系统默认库目录
- 希望实例间更可控

### `LD_LIBRARY_PATH`

作用：

- 帮 Linux 动态链接器找到 ORT / CUDA / TensorRT 依赖链

适合：

- provider 还会继续依赖别的 `.so`
- 你不想把所有依赖复制到一个目录

### 推荐做法

最稳的是两个一起配：

- `--ort-native /opt/onnxruntime/libonnxruntime.so`
- `LD_LIBRARY_PATH=/opt/onnxruntime:/usr/local/cuda/lib64:/usr/lib/aarch64-linux-gnu`

也就是说：

- `--ort-native` 负责告诉程序“ORT 主入口在哪”
- `LD_LIBRARY_PATH` 负责告诉系统“依赖链在哪”

## OpenCV 运行时

`Core` 代码里直接用了 `OpenCvSharp`，所以 Jetson 上至少要保证：

- `OpenCvSharpExtern`
- 它依赖的 OpenCV `.so`

这部分当前不由 `VideoInference.Jetson` 自动打包，建议由宿主环境统一准备。

如果后面你采用：

- 系统 OpenCV
- 自己编的 OpenCvSharp Linux runtime

都可以，但要保证 `OpenCvSharp` 能正常启动。

## FFmpeg 运行时

`Core` 里的：

- 视频文件读取
- 分段录像

都依赖 FFmpeg native。

当前建议：

- 让 FFmpeg 运行时由系统环境提供
- 或者由你自己的部署环境提供共享库路径

目前 `Jetson` 宿主只是做了基础探测，没有自动打包 FFmpeg `.so`。

## TensorRT / CUDA provider

如果走：

- `TensorRt -> Cuda -> Cpu`

那么除了 `libonnxruntime.so` 外，还要保证 TensorRT / CUDA 依赖在系统可见路径中。

这也是为什么 systemd 部署时，通常还要配一份环境变量文件。

## 推荐组合

当前我建议的最稳组合是：

1. ORT 自己指定：

```bash
--ort-native /opt/onnxruntime/libonnxruntime.so
```

2. provider 显式指定：

```bash
--providers tensorrt,cuda,cpu
```

3. 系统环境保证：

- CUDA 可见
- TensorRT 可见
- OpenCV 可见
- FFmpeg 可见

4. 首次上板先跑：

```bash
--diagnose
```

## 当前边界

这份布局说明解决的是：

- ORT 放哪
- 程序怎么找 ORT
- 系统和应用各自负责什么

还没有解决的是：

- 自动收集和打包全部 `.so`
- 不同 JetPack 版本的运行时差异
- OpenCV / FFmpeg Linux runtime 的自动化发布
