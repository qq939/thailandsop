# TCNModel - TCN 时序分类示例

这个目录提供一个最小可用的 TCN（Temporal Convolutional Network）
训练示例，使用公开数据集，方便你在第一次接触 TCN 时快速上手。

## TCN 是什么（简版）
- 用 1D 卷积在时间维度上建模，不是 RNN。
- 膨胀卷积（dilation）扩大感受野，覆盖更长的时间范围。
- 残差块让训练更稳定、收敛更快。
- 分类任务通常在时间维度做池化，再接一个全连接层输出类别。

## 本示例使用的数据集
我们使用 UCI HAR 数据集（手机传感器的人体动作识别）。
数据集提供固定长度的时序窗口以及对应的动作类别标签。
本示例使用 9 路惯性传感器通道。

数据形状：
- 每个样本: (channels=9, timesteps=128)
- 标签: 6 类，已转换为 0 基索引

## 目录结构
TCNModel/
  download_uci_har.py
  train_tcn.py
  datasets/
    uci_har.py
  models/
    tcn.py
  data/            (下载后生成，已加入 gitignore)
  output/          (训练后生成，已加入 gitignore)

## 环境要求
- Python 3.9+（推荐）
- NumPy
- PyTorch

先按官方说明安装 PyTorch（CPU 或 GPU 版本），再安装 NumPy。

## 快速开始
1) 下载数据集:
   python download_uci_har.py

2) 训练一个基础模型:
   python train_tcn.py --epochs 20 --batch-size 64

3) 如有 GPU:
   python train_tcn.py --device cuda

## 常用训练参数
--epochs         训练轮数
--batch-size     批量大小
--lr             学习率
--hidden         隐藏通道配置，如 "64,128,128"
--kernel-size    卷积核大小（默认 3）
--dropout        残差块内的 Dropout
--device         auto | cpu | cuda
--output-dir     模型保存目录

示例:
  python train_tcn.py --epochs 30 --batch-size 128 --hidden "64,128,256" --dropout 0.2

## 输出结果
最佳模型保存为:
  TCNModel/output/tcn_uci_har_best.pt

该文件包含:
- model: state_dict
- acc: 最佳测试集准确率

## 模型结构（高层）
- 输入形状 (N, C, T)
- 多个 TCN 残差块（Conv1d + dilation）
- 全局平均池化压缩时间维度
- 线性层输出类别

## 优秀库推荐与取舍
- Darts：接口最友好，适合快速验证。
- Keras-TCN：学术实现最标准。
- pytorch-tcn（pip）：首选，原生支持因果/非因果切换及流式推理状态导出。
- 本地实现：灵活性最高，适合处理结构简单的特定任务。

关于流式推理：通过维护历史状态、只处理新增时间步，可以避免对整段序列的重复计算，
降低显存与算力占用，也能减少端到端延迟，更适合在线/实时场景。

## 迁移到你的 FSM 时序数据
如果使用你自己的时序数据，建议：
1) 把原始信号切成固定长度窗口 (N, C, T)。
2) 对每个通道做归一化（均值方差或 min-max）。
3) 修改 datasets/ 里的数据加载逻辑，读取你的文件格式。
4) 设置 input_channels 为你的通道数，num_classes 为你的类别数。
5) 根据 FSM 的时间尺度调整窗口长度和采样率。

如果序列很长，推荐用滑动窗口（可重叠）；
实时推理时可以维护一个长度为 T 的滚动缓存。

## 常见问题
找不到数据集:
- 确认 download_uci_har.py 已完成并生成:
  TCNModel/data/uci_har/UCI HAR Dataset

CUDA 不可用:
- 使用 --device cpu

训练很慢:
- 降低 --batch-size
- 缩小 --hidden 配置
- 尝试 GPU

## 下一步
- 如需 C# 推理，可补充导出 ONNX 脚本。
- 用你的 FSM 数据替换当前数据加载器。
