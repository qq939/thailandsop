# MediaPipe Hand Bundle

这个 bundle 现在走“本地准备 task 文件 + 程序自动查找本地 Python 环境”的方案，不依赖环境变量。

目录结构：

```text
DL/MediaPipeHand/
  task.json
  README.md
```

说明：

- `hand_landmarker.task` 不再提交到 Git
- 首次运行准备脚本时，会自动下载到当前目录

默认运行方式：

- `task.json` 里的 `taskFilePath` 直接使用输出目录中的 `hand_landmarker.task`
- `task.json` 里的 `workerPythonPath` 使用仓库相对路径 `.runtime\micromamba\envs\mediapipe-hand-py311\python.exe`
- 程序启动时会从当前输出目录逐级向上查找这个 Python 路径
- `workerScriptPath` 指向输出目录下复制过去的 `workers/mediapipe_hand/worker.py`

首次准备：

```powershell
.\scripts\setup-mediapipe-hand-runtime.ps1
```

脚本会自动完成：

1. 准备本地 `micromamba`
2. 创建 `MediaPipe` worker 专用 Python 环境
3. 安装 `mediapipe` 和依赖
4. 下载 `hand_landmarker.task`

完成后重新编译并启动桌面程序即可。
