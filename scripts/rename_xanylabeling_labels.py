#!/usr/bin/env python
"""Rename labels in X-AnyLabeling/LabelMe JSON annotation files."""

from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path


DEFAULT_MAPPING = {
    "A": "内盒",
    "B": "圆片",
    "路由器": "产品",
    "充电器": "充电器",
    "盒子": "外盒",
    "保修卡": "保修卡",
}


def rename_labels(root: Path, mapping: dict[str, str], dry_run: bool) -> tuple[int, Counter[str], Counter[str]]:
    files_changed = 0
    before = Counter()
    after = Counter()

    for json_path in sorted(root.rglob("*.json")):
        with json_path.open("r", encoding="utf-8") as f:
            data = json.load(f)

        changed = False
        for shape in data.get("shapes", []):
            label = shape.get("label")
            if not isinstance(label, str):
                continue

            before[label] += 1
            new_label = mapping.get(label, label)
            after[new_label] += 1
            if new_label != label:
                shape["label"] = new_label
                changed = True

        if changed:
            files_changed += 1
            if not dry_run:
                with json_path.open("w", encoding="utf-8") as f:
                    json.dump(data, f, ensure_ascii=False, indent=2)
                    f.write("\n")

    return files_changed, before, after


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path, help="Annotation directory to scan recursively.")
    parser.add_argument("--dry-run", action="store_true", help="Show the rename result without writing files.")
    args = parser.parse_args()

    if not args.root.exists():
        raise FileNotFoundError(args.root)

    files_changed, before, after = rename_labels(args.root, DEFAULT_MAPPING, args.dry_run)
    print(f"Scanned: {args.root}")
    print(f"Files changed: {files_changed}")
    print("Before:")
    for label, count in sorted(before.items()):
        print(f"  {label}: {count}")
    print("After:")
    for label, count in sorted(after.items()):
        print(f"  {label}: {count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
