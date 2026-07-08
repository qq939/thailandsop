# MediaPipe One-Click Runtime

这套方案的目标是让 `MediaPipe Hand` 以仓库内自管理 runtime 的方式部署，不依赖系统 Python，也不要求现场手工装包。

## 仓库内容

- `.runtime/micromamba/bootstrap/micromamba.exe`
- `workers/mediapipe_hand/environment.yml`
- `workers/mediapipe_hand/requirements.txt`
- `scripts/setup-mediapipe-hand-runtime.ps1`
- `scripts/setup-mediapipe-hand-runtime.cmd`

## 一键准备

在仓库根目录执行：

```powershell
.\scripts\setup-mediapipe-hand-runtime.ps1
```

或者：

```cmd
scripts\setup-mediapipe-hand-runtime.cmd
```

脚本会自动完成：

1. 准备 `micromamba`
2. 在 `.runtime/micromamba/envs/mediapipe-hand-py311` 创建 Python 环境
3. 安装 `mediapipe`、`numpy`
4. 下载官方 `hand_landmarker.task`
5. 重新编译桌面程序

## 运行方式

`DL/MediaPipeHand/task.json` 中：

```json
"taskFilePath": "hand_landmarker.task"
```

`DL/MediaPipeHand/task.json` 中：

```json
"workerPythonPath": ".runtime\\micromamba\\envs\\mediapipe-hand-py311\\python.exe"
```

程序启动 worker 时会从输出目录逐级向上查找仓库里的 `.runtime` 环境。`hand_landmarker.task` 仍然直接复制到输出目录，但改成了“目标缺失时才复制”，避免运行中被锁住时阻塞构建。

## Git 建议

建议提交：

- `.runtime/micromamba/bootstrap/micromamba.exe`
- 所有脚本和环境清单

不建议提交：

- `.runtime/micromamba/envs/`
- `.runtime/micromamba/pkgs/`

也就是说，提交的是“可重建 runtime 的引导器和清单”，不是整套解释器环境。
