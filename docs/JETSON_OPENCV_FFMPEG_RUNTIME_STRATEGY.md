# Jetson OpenCvSharp / FFmpeg Runtime 策略

这份文档记录当前 `VideoInference.Jetson` 在 `OpenCvSharp` 和 `FFmpeg` 原生运行时上的现状、结论和建议。

## 当前验证结论

基于这次实际检查：

- [project.assets.json](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Jetson/obj/project.assets.json)
- [artifacts/jetson/publish](c:/Users/ljia/source/repos/VideoInferenceDemo/artifacts/jetson/publish)
- 本机 NuGet 缓存

可以确认：

### 1. ORT 已经能随 `linux-arm64` 发布输出

发布目录中已经有：

- `libonnxruntime.so`
- `libonnxruntime_providers_shared.so`

说明当前 `Microsoft.ML.OnnxRuntime` 这个包对 `linux-arm64` 是有 native 资产的。

### 2. SQLite 也已经能随发布输出

发布目录中已经有：

- `libe_sqlite3.so`

这部分当前不构成 Jetson 阻塞。

### 3. OpenCvSharp 目前只有托管层，没有 Linux native runtime

发布目录里只有：

- `OpenCvSharp.dll`

没有看到：

- `OpenCvSharpExtern`
- `OpenCvSharpExtern.so`

同时当前项目引用只有：

- [VideoInference.Core.csproj](c:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/VideoInference.Core.csproj) 中的 `OpenCvSharp4`

本机 NuGet 缓存也只有：

- `opencvsharp4`
- `opencvsharp4.runtime.win`

没有现成的 Linux runtime 包被接入当前方案。

### 4. Sdcb.FFmpeg 目前也只有托管层，没有 Linux native runtime

发布目录里只有：

- `Sdcb.FFmpeg.dll`

没有看到：

- `libavcodec.so`
- `libavformat.so`
- `libswscale.so`

当前本机 NuGet 缓存里也只看到：

- `sdcb.ffmpeg`
- `sdcb.ffmpeg.runtime.windows-x64`

说明 Jetson 当前并不会自动带出 FFmpeg Linux native。

## 结论

当前 Jetson 发布产物的 native 状态可以概括为：

- `ORT`：随发布输出
- `SQLite`：随发布输出
- `OpenCvSharp`：仅托管层，native 需要额外解决
- `FFmpeg`：仅托管层，native 需要额外解决

所以：

**Jetson 现在不能指望“只拷一份 publish 目录就直接跑起来”。**

至少在 `OpenCvSharp` 和 `FFmpeg` 这两层，还需要宿主环境或额外部署资产支持。

## 推荐策略

我建议当前阶段采用：

**ORT 随应用发布，OpenCvSharp/FFmpeg 由宿主环境提供 native。**

也就是分三层：

### 应用随发布输出

- `VideoInference.Jetson.dll`
- `VideoInference.Core.dll`
- 配置文件
- `DL`
- `libonnxruntime.so`
- `libonnxruntime_providers_shared.so`
- `libe_sqlite3.so`

### 宿主环境提供

- `OpenCvSharpExtern`
- OpenCV 依赖链
- FFmpeg 依赖链
- CUDA / TensorRT

### 应用启动时显式指定

- `--ort-native`
- `--providers`

## 为什么当前不建议先把 OpenCV/FFmpeg 也硬塞进发布目录

原因不是不能做，而是当前投入产出比不高：

1. `OpenCvSharpExtern` 的 Linux/Jetson 版本通常需要和 OpenCV 版本匹配
2. FFmpeg `.so` 链也容易和 Jetson 系统环境耦合
3. 你现在最需要的是尽快上板验证主链，而不是先做一套复杂 native 打包系统

所以现阶段更稳的路线是：

- 先把宿主环境依赖说清楚
- 先用 `--diagnose` 验证
- 真跑通后，再决定要不要把 OpenCV/FFmpeg native 也做成应用级部署资产

## 当前建议的 Jetson 依赖边界

### 应用自己负责

- ORT 主库与共享 provider
- 配置、模型、日志目录
- 诊断入口
- systemd 模板

### Jetson 宿主负责

- `OpenCvSharpExtern`
- OpenCV `.so`
- FFmpeg `.so`
- CUDA / TensorRT `.so`

## 下一步建议

如果继续推进 Jetson，我建议优先顺序是：

1. 在真实 Jetson 上先跑一次：
   - `--diagnose`
2. 明确宿主上：
   - `OpenCvSharpExtern` 从哪里提供
   - FFmpeg 从哪里提供
3. 真机跑通后，再决定是否要做：
   - 应用目录携带 `OpenCvSharpExtern`
   - 应用目录携带 FFmpeg native

## 现阶段一句话结论

**当前 Jetson 路线里，ORT 已经能跟随发布；OpenCvSharp 和 FFmpeg 还应先视为宿主依赖，而不是应用自带依赖。**
