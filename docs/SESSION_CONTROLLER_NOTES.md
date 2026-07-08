# SessionController 说明

这份说明用于补充 Desktop 侧最近引入的 `PipelineSessionController`。

## 为什么要加这一层

在当前项目里，`MainViewModel` 和 `CameraSessionViewModel` 原来同时承担了两类职责：

- 页面状态和命令绑定
- 运行编排，例如应用模型、启动/停止 pipeline、维护运行上下文、控制录像切段

这会带来几个问题：

- ViewModel 体积继续膨胀
- 主界面和相机会话页容易各自长出一套流程
- 后面做 `UI.Shared` 或迁移到 Avalonia 时，业务编排会跟着 UI 一起搬动
- 将来拆 `VideoPipeline` 时，外层调用关系会比较乱

因此先引入一个轻量的 `SessionController`，把“运行编排”从 ViewModel 里剥出来一部分。

## 这次落地的最小范围

当前版本的 `PipelineSessionController` 先只负责几件事：

- 应用模型到 `VideoPipeline`
- 启动视频会话
- 启动相机会话
- 停止会话
- 请求录像切段
- 维护运行上下文：
  - `CurrentSourceKey`
  - `CurrentRunUuid`
  - `CurrentRunStartedUtcMs`
  - `IsModelLoaded`

也就是说，这一版并没有试图接管所有 UI 状态，只先把最容易重复、最容易漂移的那部分运行控制收口。

## 这一版没有做什么

当前还没有把下面这些职责搬进去：

- `Pipeline` 事件订阅与 UI 状态映射
- 页面上的状态文案更新
- 主界面和相机会话页的完整状态同步
- `VideoPipeline` 内部结构拆分

这些内容依然保留在 ViewModel 中，目的是让第一刀改动尽量小、风险尽量低。

## 当前结构理解

现在 Desktop 侧大致可以这样理解：

- `ViewModel`
  - 负责页面状态、命令、显示文案
- `PipelineSessionController`
  - 负责运行编排和运行上下文
- `VideoPipeline`
  - 负责真正的执行内核

这比之前“ViewModel 同时管页面和流程”更清晰，也给后续继续拆分留出了稳定落点。

## 下一步建议

如果后面继续推进，优先级建议是：

1. 继续把主界面和相机会话页中重复的运行流程收进 `SessionController`
2. 再考虑把 ViewModel 中的页面状态模型往 `UI.Shared` 方向提炼
3. 最后再拆 `VideoPipeline` 内部的大块职责

这样可以先把外层控制关系理顺，再去拆执行内核，整体风险会更低。
