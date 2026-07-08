# Jetson 运行与诊断补充

这份文档补充说明 `VideoInference.Jetson` 最近新增的启动约定和 Linux 环境诊断能力。

## 新增参数

- `--diagnose`
  只运行环境诊断并退出，不启动相机和推理主流程。
- `--log-dir <path>`
  指定诊断日志输出目录。默认是当前部署目录下的 `./logs`。

## 默认目录约定

如果没有额外传参，Jetson 宿主会按下面的相对路径解析：

- `./camera_config.json`
- `./DL`
- `./logs`

其中：

- `camera_config.json` 用于相机配置发现
- `DL` 用于模型 bundle 发现
- `logs` 用于环境诊断日志输出

## 推荐先跑一次诊断

第一次上板或替换 ORT / TensorRT / 模型后，建议先跑一遍：

```bash
dotnet run --project src/VideoInference.Jetson -- \
  --diagnose \
  --model-root ./DL \
  --ort-native /opt/onnxruntime/libonnxruntime.so \
  --providers tensorrt,cuda,cpu
```

## 当前诊断覆盖范围

当前 Linux 诊断会检查：

- OpenCV 基础探测
- FFmpeg 基础探测
- 模型目录发现
- ORT session/provider 可用性
- 关键路径解析

诊断结果会：

- 输出到控制台
- 写入 `./logs/environment_diagnostics.log`

## 适用场景

这套能力的目标不是替代完整部署脚本，而是尽快回答这几个问题：

- OpenCV native 是否真的能用
- FFmpeg native 是否真的能用
- 模型目录是否能发现到有效 bundle
- ORT 在当前 `.so` 和 provider 顺序下是否能起来
- 当前宿主到底在用哪些实际路径
