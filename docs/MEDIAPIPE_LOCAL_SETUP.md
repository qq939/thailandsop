# MediaPipe Local Setup

## 目标

这份清单用于把当前仓库里的 `MediaPipe Hand Worker` 真正跑起来，并完成第一次联调。

对应代码入口：

- [worker.py](C:/Users/ljia/source/repos/VideoInferenceDemo/workers/mediapipe_hand/worker.py)
- [requirements.txt](C:/Users/ljia/source/repos/VideoInferenceDemo/workers/mediapipe_hand/requirements.txt)
- [MediaPipeHandLandmarkTask.cs](C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Vision/Tasks/MediaPipe/MediaPipeHandLandmarkTask.cs)
- [task.hand_landmarks.template.json](C:/Users/ljia/source/repos/VideoInferenceDemo/docs/templates/task.hand_landmarks.template.json)

## 准备项

本地需要准备：

- Python 3.10 或 3.11
- 可用的 `pip`
- 一份真实的 `hand_landmarker.task`
- 当前仓库代码

建议先单独建一个 worker 虚拟环境，不要和训练环境混用。

## 安装依赖

在仓库根目录执行：

```powershell
python -m venv .venv-mediapipe
.venv-mediapipe\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r workers\mediapipe_hand\requirements.txt
```

如果 PowerShell 不允许激活脚本，也可以直接用：

```powershell
.venv-mediapipe\Scripts\python.exe -m pip install --upgrade pip
.venv-mediapipe\Scripts\python.exe -m pip install -r workers\mediapipe_hand\requirements.txt
```

## 放置模型文件

建议在 `DL` 目录里新建一个任务 bundle，例如：

```text
DL/
  MediaPipeHand/
    task.json
    hand_landmarker.task
```

仓库里现在已经预置了：

- [DL/MediaPipeHand/task.json](C:/Users/ljia/source/repos/VideoInferenceDemo/DL/MediaPipeHand/task.json)
- [DL/MediaPipeHand/README.md](C:/Users/ljia/source/repos/VideoInferenceDemo/DL/MediaPipeHand/README.md)

所以现场通常只需要把真实的 `hand_landmarker.task` 放进去，再按实际情况调整 `workerPythonPath`。

其中 `task.json` 可以从模板复制：

- [task.hand_landmarks.template.json](C:/Users/ljia/source/repos/VideoInferenceDemo/docs/templates/task.hand_landmarks.template.json)

至少确认这几个字段是对的：

- `taskFilePath`
- `workerPythonPath`
- `workerScriptPath`
- `workerProtocol`
- `maxHands`

如果你们继续使用当前模板路径，`workerScriptPath` 应该保证最终能解析到：

`workers/mediapipe_hand/worker.py`

## 推荐的 task.json 例子

```json
{
  "id": "mediapipe-hand-landmarks",
  "displayName": "MediaPipe Hand Landmarks",
  "taskKind": "HandLandmarks",
  "runtimeKind": "MediaPipe",
  "metadata": {
    "taskFilePath": "hand_landmarker.task",
    "workerKind": "mediapipe_hand",
    "workerPythonPath": "python",
    "workerScriptPath": "..\\..\\workers\\mediapipe_hand\\worker.py",
    "workerProtocol": "NamedPipe",
    "maxHands": "2",
    "minHandDetectionConfidence": "0.5",
    "minHandPresenceConfidence": "0.5",
    "minTrackingConfidence": "0.5",
    "preferredInputSize": "640"
  }
}
```

## 当前代码的已知前提

当前 `MediaPipeHandTaskFactory` 默认启动命令是：

```text
"<workerPythonPath>" "<workerScriptPath>" --endpoint "<pipeName>" --task-file "<taskFilePath>" --worker-kind "<workerKind>"
```

如果没有显式配置 `workerPythonPath`，代码仍会回退到 `python`。  
但现场部署更推荐直接写成虚拟环境里的绝对路径或相对路径。

## 首次联调建议

第一轮建议这样验：

1. 先确认 `task.json` 能被发现
2. 在桌面端选中 `MediaPipe Hand Landmarks`
3. 绑定到某个相机 session
4. 启动 session
5. 看 worker 是否被成功拉起
6. 看预览画面上是否出现手部骨架

## 失败时先看哪里

当前代码已经把 worker 失败信息尽量往上带了，重点看：

- `InferenceStatus`
- `InferenceDeviceText`
- 异常消息里的：
  - `WorkerState`
  - `Endpoint`
  - `LastError`

如果 worker 启动失败，最常见的几个原因是：

- `python` 不在 PATH
- `mediapipe` 没安装成功
- `hand_landmarker.task` 路径不对
- worker 脚本路径不对
- Python 版本不兼容

## 建议的排查顺序

### 1. 先确认 Python 可直接执行

```powershell
python --version
```

如果 `task.json` 里配置的是显式 Python 路径，也要额外确认那个路径真实存在。

### 2. 再确认 worker 依赖已安装

```powershell
python -c "import mediapipe; import numpy; print('ok')"
```

### 3. 再确认 task 文件存在

```powershell
Get-Item .\DL\MediaPipeHand\hand_landmarker.task
```

### 4. 手工跑 worker 看 import 和启动错误

可以手工执行：

```powershell
python workers\mediapipe_hand\worker.py --endpoint test-pipe --task-file .\DL\MediaPipeHand\hand_landmarker.task --worker-kind mediapipe_hand
```

注意：

- 直接手工跑时，因为还没有主进程创建 named pipe，这个命令本身不会完成联通
- 但如果脚本有 import 错误、路径错误、参数错误，会立刻暴露

## 验收标准

联调成功时，应该满足：

- session 可以正常启动
- worker 不会马上退出
- `Warmup` 不报错
- 运行时不再返回 stub 空结果
- 画面中能看到真实手关键点骨架

## 下一步建议

现场首次跑通后，建议紧接着做这两件事：

1. 把 worker stderr 和状态透到 UI 上，便于长期维护
2. 视情况把 `workerPythonPath` 再提升成桌面设置项，便于多机统一配置
