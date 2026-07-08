from pathlib import Path
from typing import Tuple

import numpy as np
import torch
from torch.utils.data import Dataset


SIGNAL_NAMES = [
    "body_acc_x",
    "body_acc_y",
    "body_acc_z",
    "body_gyro_x",
    "body_gyro_y",
    "body_gyro_z",
    "total_acc_x",
    "total_acc_y",
    "total_acc_z",
]


def _load_signals(split_dir: Path, split: str) -> np.ndarray:
    signals = []
    inertial_dir = split_dir / "Inertial Signals"
    for name in SIGNAL_NAMES:
        path = inertial_dir / f"{name}_{split}.txt"
        data = np.loadtxt(path, dtype=np.float32)
        signals.append(data)
    return np.stack(signals, axis=1)


def load_split(root: Path, split: str) -> Tuple[np.ndarray, np.ndarray]:
    split_dir = root / split
    x = _load_signals(split_dir, split)
    y = np.loadtxt(split_dir / f"y_{split}.txt", dtype=np.int64) - 1
    return x, y


class UciHarDataset(Dataset):
    def __init__(self, root: Path, split: str):
        self.root = Path(root)
        self.split = split
        self.x, self.y = load_split(self.root, split)

    def __len__(self) -> int:
        return int(self.x.shape[0])

    def __getitem__(self, idx: int):
        x = torch.from_numpy(self.x[idx])
        y = torch.tensor(int(self.y[idx]), dtype=torch.long)
        return x, y
