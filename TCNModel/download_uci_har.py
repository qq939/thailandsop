import argparse
from pathlib import Path
import sys
import urllib.request
import zipfile


DATASET_URL = (
    "https://archive.ics.uci.edu/ml/machine-learning-databases/00240/"
    "UCI%20HAR%20Dataset.zip"
)


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(".download")
    if tmp.exists():
        tmp.unlink()
    print(f"Downloading: {url}")
    urllib.request.urlretrieve(url, tmp)  # nosec - trusted public dataset
    tmp.replace(dest)


def extract(zip_path: Path, out_dir: Path) -> Path:
    with zipfile.ZipFile(zip_path, "r") as zf:
        zf.extractall(out_dir)
    return out_dir / "UCI HAR Dataset"


def main() -> int:
    parser = argparse.ArgumentParser(description="Download UCI HAR dataset.")
    parser.add_argument(
        "--out-dir",
        default=str(Path(__file__).resolve().parent / "data" / "uci_har"),
        help="Output directory for dataset files.",
    )
    parser.add_argument("--force", action="store_true", help="Redownload dataset.")
    args = parser.parse_args()

    out_dir = Path(args.out_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    dataset_dir = out_dir / "UCI HAR Dataset"
    zip_path = out_dir / "uci_har.zip"

    if dataset_dir.exists() and not args.force:
        print(f"Dataset already exists: {dataset_dir}")
        return 0

    if zip_path.exists():
        zip_path.unlink()

    download(DATASET_URL, zip_path)
    extracted_dir = extract(zip_path, out_dir)
    print(f"Done. Dataset extracted to: {extracted_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
