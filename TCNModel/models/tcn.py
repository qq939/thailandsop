import torch
from torch import nn

# TCN (Temporal Convolutional Network) 核心观点 / 主流认识：
# - 用 1D 卷积替代 RNN/Transformer 的时序建模，训练更并行、梯度更稳定。
# - 通过“空洞卷积(dilation)”扩大感受野，捕获长程依赖。
# - 残差连接帮助深层网络收敛，避免退化。
# - 分类任务通常不需要严格因果(causal)约束；若要因果性，需要用单边 padding。
# 下面实现的是“非因果、长度保持”的 TCN 变体，适合整段序列分类。


class TemporalBlock(nn.Module):
    def __init__(self, in_channels: int, out_channels: int, kernel_size: int, dilation: int, dropout: float):
        super().__init__()
        # padding 设为 (k-1)*d/2 能让输出长度≈输入长度（建议 kernel_size 用奇数）
        # 若 kernel_size 为偶数，这里的整数除法可能导致长度轻微变化。
        padding = (kernel_size - 1) * dilation // 2
        # 两层空洞卷积 + ReLU + Dropout 是经典 TCN block 结构
        self.conv1 = nn.Conv1d(in_channels, out_channels, kernel_size, padding=padding, dilation=dilation)
        self.relu1 = nn.ReLU(inplace=True)
        self.drop1 = nn.Dropout(dropout)
        self.conv2 = nn.Conv1d(out_channels, out_channels, kernel_size, padding=padding, dilation=dilation)
        self.relu2 = nn.ReLU(inplace=True)
        self.drop2 = nn.Dropout(dropout)
        self.downsample = None
        if in_channels != out_channels:
            # 残差分支通道不一致时，用 1x1 卷积对齐通道数
            self.downsample = nn.Conv1d(in_channels, out_channels, kernel_size=1)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # x: [batch, channels, time]
        out = self.drop1(self.relu1(self.conv1(x)))
        out = self.drop2(self.relu2(self.conv2(out)))
        res = x if self.downsample is None else self.downsample(x)
        return out + res


class TcnClassifier(nn.Module):
    def __init__(
        self,
        input_channels: int,
        num_classes: int,
        hidden_channels: list[int],
        kernel_size: int = 3,
        dropout: float = 0.1,
    ):
        super().__init__()
        # 参数原则 / 实践经验：
        # - hidden_channels 控制每层宽度，常见做法是逐层相同或逐层递增。
        # - kernel_size 建议用 3/5/7（奇数），配合 padding 才能更好地保持长度。
        # - dilation 采用 2**i 逐层扩张，感受野随层数指数增长。
        # - dropout 一般在 0.1~0.3 之间，数据量小/过拟合时可适当增大。
        layers = []
        channels = [input_channels] + hidden_channels
        for i in range(len(hidden_channels)):
            layers.append(
                TemporalBlock(
                    channels[i],
                    channels[i + 1],
                    kernel_size=kernel_size,
                    dilation=2**i,  # 经典指数式空洞率，快速扩大感受野
                    dropout=dropout,
                )
            )
        self.tcn = nn.Sequential(*layers)
        # 对时间维做全局平均池化，得到序列级表示
        self.pool = nn.AdaptiveAvgPool1d(1)
        self.classifier = nn.Linear(hidden_channels[-1], num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # 输入张量形状约定: [N, C, T]
        # N: batch size, C: 通道数/特征维, T: 时间步
        x = self.tcn(x)
        x = self.pool(x).squeeze(-1)
        return self.classifier(x)
