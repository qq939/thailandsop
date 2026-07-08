import argparse
from pathlib import Path
import sys
import time

import torch
from torch import nn
from torch.utils.data import DataLoader

from datasets.uci_har import UciHarDataset
from models.tcn import TcnClassifier


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train a simple TCN on UCI HAR.")
    parser.add_argument(
        "--data-dir",
        default=str(Path(__file__).resolve().parent / "data" / "uci_har" / "UCI HAR Dataset"),
        help="Root directory containing UCI HAR Dataset.",
    )
    parser.add_argument("--epochs", type=int, default=20)
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--dropout", type=float, default=0.1)
    parser.add_argument("--kernel-size", type=int, default=3)
    parser.add_argument("--hidden", type=str, default="64,128,128")
    parser.add_argument("--device", type=str, default="auto", choices=["auto", "cpu", "cuda"])
    parser.add_argument("--num-workers", type=int, default=0)
    parser.add_argument(
        "--output-dir",
        default=str(Path(__file__).resolve().parent / "output"),
        help="Directory for checkpoints.",
    )
    return parser.parse_args()


def resolve_device(device_arg: str) -> torch.device:
    if device_arg == "cpu":
        return torch.device("cpu")
    if device_arg == "cuda":
        return torch.device("cuda")
    return torch.device("cuda" if torch.cuda.is_available() else "cpu")


def train_one_epoch(model, loader, optimizer, criterion, device) -> tuple[float, float]:
    model.train()
    running_loss = 0.0
    correct = 0
    total = 0
    for x, y in loader:
        x = x.to(device)
        y = y.to(device)
        optimizer.zero_grad(set_to_none=True)
        logits = model(x)
        loss = criterion(logits, y)
        loss.backward()
        optimizer.step()
        running_loss += loss.item() * y.size(0)
        pred = torch.argmax(logits, dim=1)
        correct += int((pred == y).sum().item())
        total += y.size(0)
    return running_loss / max(total, 1), correct / max(total, 1)


@torch.no_grad()
def evaluate(model, loader, criterion, device) -> tuple[float, float]:
    model.eval()
    running_loss = 0.0
    correct = 0
    total = 0
    for x, y in loader:
        x = x.to(device)
        y = y.to(device)
        logits = model(x)
        loss = criterion(logits, y)
        running_loss += loss.item() * y.size(0)
        pred = torch.argmax(logits, dim=1)
        correct += int((pred == y).sum().item())
        total += y.size(0)
    return running_loss / max(total, 1), correct / max(total, 1)


def main() -> int:
    args = parse_args()
    data_root = Path(args.data_dir).resolve()
    if not data_root.exists():
        print(f"Dataset not found: {data_root}")
        print("Run: python download_uci_har.py")
        return 1

    hidden = [int(x.strip()) for x in args.hidden.split(",") if x.strip()]
    device = resolve_device(args.device)
    if device.type == "cuda":
        torch.backends.cudnn.benchmark = True

    train_set = UciHarDataset(data_root, "train")
    test_set = UciHarDataset(data_root, "test")
    train_loader = DataLoader(
        train_set, batch_size=args.batch_size, shuffle=True, num_workers=args.num_workers, pin_memory=True
    )
    test_loader = DataLoader(
        test_set, batch_size=args.batch_size, shuffle=False, num_workers=args.num_workers, pin_memory=True
    )

    model = TcnClassifier(
        input_channels=9,
        num_classes=6,
        hidden_channels=hidden,
        kernel_size=args.kernel_size,
        dropout=args.dropout,
    ).to(device)

    criterion = nn.CrossEntropyLoss()
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.lr)

    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    best_acc = 0.0

    for epoch in range(1, args.epochs + 1):
        start = time.time()
        train_loss, train_acc = train_one_epoch(model, train_loader, optimizer, criterion, device)
        test_loss, test_acc = evaluate(model, test_loader, criterion, device)
        elapsed = time.time() - start
        print(
            f"Epoch {epoch:03d} | "
            f"train loss {train_loss:.4f} acc {train_acc:.3f} | "
            f"test loss {test_loss:.4f} acc {test_acc:.3f} | "
            f"{elapsed:.1f}s"
        )
        if test_acc > best_acc:
            best_acc = test_acc
            ckpt = output_dir / "tcn_uci_har_best.pt"
            torch.save({"model": model.state_dict(), "acc": best_acc}, ckpt)
            print(f"Saved: {ckpt}")

    print(f"Best test acc: {best_acc:.3f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
