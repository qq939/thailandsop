# DeploySharp YOLO Det 迁移说明

## 为什么不是直接复制模型类

`DeploySharp` 的 YOLO 检测实现并不是一个单文件后处理器，而是分散在：

- 模型类
- 配置类
- 结果类型
- 数据处理器
- NMS
- 引擎抽象

如果直接复制 `Yolov11DetModel` 或 `IYolov8DetModel`，会顺带把这些耦合带进来：

- `DeploySharp.Engine`
- `DeploySharp.Model.Config`
- `DataTensor`
- `IModel`

这会和我们当前的 `Core` 目标冲突。

## 当前采用的迁移策略

采用“摘算法，不搬框架”的方式：

- 保留我们自己的预处理
- 保留我们自己的 ORT runtime
- 保留我们自己的结果模型
- 只把 `DeploySharp` 的纯算法拆成更小的内部模块

## 当前第一批迁移内容

本轮先迁移最纯的一块：

- 矩形候选框结构
- 矩形 NMS

原因：

- 无平台依赖
- 无引擎依赖
- 不牵扯配置体系
- 可直接替换我们现有 `YoloDetectionPostprocessor` 里的内联 NMS

## 后续是否继续迁移

后续只在满足以下条件时继续：

- 明确能降低我们现有实现复杂度
- 不引入 `DeploySharp` runtime 依赖
- 不破坏 Jetson / Ubuntu 的平台纯度

如果某一部分只是“写法不同”，但不能带来结构收益或兼容收益，则不迁移。
