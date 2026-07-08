# Jetson 发布与部署

这份文档说明 `VideoInference.Jetson` 当前推荐的发布和部署方式。

## 目标形态

当前推荐的 Jetson 形态是：

- 单实例
- 单路
- headless
- 需要多路时使用多实例，而不是单进程多 pipeline

这样更利于资源隔离、故障隔离和 systemd 管理。

## 目录

本仓库已新增这些部署资产：

- [publish-jetson.ps1](c:/Users/ljia/source/repos/VideoInferenceDemo/scripts/jetson/publish-jetson.ps1)
- [videoinference-jetson@.service](c:/Users/ljia/source/repos/VideoInferenceDemo/deploy/jetson/videoinference-jetson@.service)
- [videoinference-jetson.env.example](c:/Users/ljia/source/repos/VideoInferenceDemo/deploy/jetson/videoinference-jetson.env.example)

## 本地发布

在 Windows 开发机上执行：

```powershell
./scripts/jetson/publish-jetson.ps1
```

默认行为：

- `Release`
- `linux-arm64`
- 输出到 `artifacts/jetson/publish`
- 非 self-contained

如果你需要显式指定输出目录：

```powershell
./scripts/jetson/publish-jetson.ps1 -Output "artifacts/jetson/camera0"
```

## 建议的 Jetson 部署目录

建议一份实例一个目录，例如：

```text
/opt/videoinference/camera0/
  VideoInference.Jetson.dll
  camera_config.json
  analysis_config.json
  db_config.json
  tcn_feature_config.json
  tcn_infer_config.json
  DL/
  logs/
  videoinference-jetson@.service
  videoinference-jetson.env.example
```

如果后面是多实例，就按 `camera0 / camera1 / camera2` 这种目录复制。

## 启动前诊断

第一次上板时，建议先运行：

```bash
dotnet /opt/videoinference/camera0/VideoInference.Jetson.dll \
  --diagnose \
  --model-root /opt/videoinference/camera0/DL \
  --log-dir /opt/videoinference/camera0/logs \
  --ort-native /opt/onnxruntime/libonnxruntime.so \
  --providers tensorrt,cuda,cpu
```

诊断结果会写到：

```text
/opt/videoinference/camera0/logs/environment_diagnostics.log
```

## systemd 部署

1. 把发布目录复制到 Jetson，例如：

```bash
sudo mkdir -p /opt/videoinference/camera0
sudo rsync -av ./artifacts/jetson/publish/ /opt/videoinference/camera0/
```

2. 准备环境变量文件：

```bash
sudo mkdir -p /etc/videoinference
sudo cp /opt/videoinference/camera0/videoinference-jetson.env.example \
  /etc/videoinference/videoinference-jetson-camera0.env
```

3. 按实际设备修改：

- `CAMERA_INDEX` 或 `CAMERA_SOURCE`
- `MODEL_ROOT`
- `RECORD_ROOT`
- `LOG_DIR`
- `ORT_NATIVE`
- `ORT_PROVIDERS`

4. 安装 service 模板：

```bash
sudo cp /opt/videoinference/camera0/videoinference-jetson@.service \
  /etc/systemd/system/videoinference-jetson@.service
sudo systemctl daemon-reload
```

5. 启动单实例：

```bash
sudo systemctl enable --now videoinference-jetson@camera0
```

## 多实例策略

如果未来要两路或三路，不建议先改成单进程多 pipeline。

更推荐：

- `camera0` 一个部署目录
- `camera1` 一个部署目录
- 每个实例一份 `.env`
- systemd 分别启动：
  - `videoinference-jetson@camera0`
  - `videoinference-jetson@camera1`

## 当前边界

这套部署资产主要解决：

- `linux-arm64` 发布
- 单实例目录约定
- 日志目录约定
- systemd 托管

还没有覆盖：

- OpenCV / FFmpeg / ORT `.so` 的自动打包
- Hik Linux provider
- Avalonia UI
