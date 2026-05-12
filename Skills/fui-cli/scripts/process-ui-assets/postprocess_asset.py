#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FUI UI 资源单图后处理工具。

只允许处理 imagegen 已生成的位图：透明、裁边、缩放、校验和输出报告。
不要用这个脚本绘制最终美术，也不要用它修改 Unity prefab。
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Iterable

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover - 环境缺依赖时给明确错误
    raise SystemExit("缺少 Pillow。请先安装 Pillow，或使用已包含 Pillow 的 Python 环境。") from exc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Post-process one generated UI bitmap for FUI WebToUgui assets."
    )
    parser.add_argument("--source", required=True, help="输入 PNG 路径")
    parser.add_argument("--output", required=True, help="输出 PNG 路径")
    parser.add_argument("--width", type=int, default=0, help="目标宽度")
    parser.add_argument("--height", type=int, default=0, help="目标高度")
    parser.add_argument(
        "--fit",
        choices=("stretch", "contain", "cover", "none"),
        default="stretch",
        help="缩放模式",
    )
    parser.add_argument(
        "--alpha-mode",
        choices=("keep", "trim", "chroma", "chroma-trim"),
        default="keep",
        help="透明处理模式",
    )
    parser.add_argument("--alpha-threshold", type=int, default=8, help="alpha 裁边阈值")
    parser.add_argument("--chroma-threshold", type=int, default=28, help="背景色抠除阈值")
    parser.add_argument("--padding", type=int, default=0, help="裁边后补透明边距")
    parser.add_argument("--report", default="", help="可选 JSON 报告路径")
    parser.add_argument("--dry-run", action="store_true", help="只计算报告，不写文件")
    return parser.parse_args()


def validate_args(args: argparse.Namespace) -> None:
    if args.width < 0 or args.height < 0:
        raise SystemExit("--width/--height 不能为负数。")

    if (args.width == 0) != (args.height == 0):
        raise SystemExit("--width 和 --height 必须同时提供，或同时省略。")

    if args.alpha_threshold < 0 or args.alpha_threshold > 255:
        raise SystemExit("--alpha-threshold 必须在 0-255 范围内。")

    if args.chroma_threshold < 0:
        raise SystemExit("--chroma-threshold 不能为负数。")

    if args.padding < 0:
        raise SystemExit("--padding 不能为负数。")


def color_distance(left: tuple[int, int, int], right: tuple[int, int, int]) -> float:
    return math.sqrt(
        (left[0] - right[0]) ** 2
        + (left[1] - right[1]) ** 2
        + (left[2] - right[2]) ** 2
    )


def corner_colors(image: Image.Image) -> list[tuple[int, int, int]]:
    width, height = image.size
    samples = [
        image.getpixel((0, 0)),
        image.getpixel((width - 1, 0)),
        image.getpixel((0, height - 1)),
        image.getpixel((width - 1, height - 1)),
    ]
    return [(r, g, b) for r, g, b, _ in samples]


def apply_chroma_key(image: Image.Image, threshold: int) -> Image.Image:
    colors = corner_colors(image)
    pixels = []

    for r, g, b, a in image.getdata():
        current = (r, g, b)
        if min(color_distance(current, sample) for sample in colors) <= threshold:
            pixels.append((r, g, b, 0))
            continue

        pixels.append((r, g, b, a))

    result = Image.new("RGBA", image.size)
    result.putdata(pixels)
    return result


def alpha_bbox(image: Image.Image, threshold: int) -> tuple[int, int, int, int] | None:
    alpha = image.getchannel("A")
    mask = alpha.point(lambda value: 255 if value > threshold else 0)
    return mask.getbbox()


def trim_alpha(image: Image.Image, threshold: int, padding: int) -> Image.Image:
    bbox = alpha_bbox(image, threshold)
    if bbox is None:
        return image

    cropped = image.crop(bbox)
    if padding == 0:
        return cropped

    padded = Image.new(
        "RGBA",
        (cropped.width + padding * 2, cropped.height + padding * 2),
        (0, 0, 0, 0),
    )
    padded.alpha_composite(cropped, (padding, padding))
    return padded


def resize_image(image: Image.Image, width: int, height: int, fit: str) -> Image.Image:
    if width <= 0 or height <= 0 or fit == "none":
        return image

    if fit == "stretch":
        return image.resize((width, height), Image.Resampling.LANCZOS)

    scale = max(width / image.width, height / image.height) if fit == "cover" else min(
        width / image.width,
        height / image.height,
    )
    scaled_width = max(1, round(image.width * scale))
    scaled_height = max(1, round(image.height * scale))
    resized = image.resize((scaled_width, scaled_height), Image.Resampling.LANCZOS)

    if fit == "contain":
        canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        canvas.alpha_composite(resized, ((width - scaled_width) // 2, (height - scaled_height) // 2))
        return canvas

    left = max(0, (scaled_width - width) // 2)
    top = max(0, (scaled_height - height) // 2)
    return resized.crop((left, top, left + width, top + height))


def alpha_coverage(image: Image.Image, threshold: int) -> float:
    alpha = image.getchannel("A")
    visible = sum(1 for value in alpha.getdata() if value > threshold)
    total = image.width * image.height
    return round(visible / total, 6) if total > 0 else 0.0


def report_for(
    source: Path,
    output: Path,
    original: Image.Image,
    processed: Image.Image,
    args: argparse.Namespace,
) -> dict:
    bbox = alpha_bbox(processed, args.alpha_threshold)
    return {
        "source": str(source),
        "output": str(output),
        "dryRun": bool(args.dry_run),
        "fit": args.fit,
        "alphaMode": args.alpha_mode,
        "originalSize": {"width": original.width, "height": original.height},
        "outputSize": {"width": processed.width, "height": processed.height},
        "alphaBounds": None
        if bbox is None
        else {"x": bbox[0], "y": bbox[1], "width": bbox[2] - bbox[0], "height": bbox[3] - bbox[1]},
        "alphaCoverage": alpha_coverage(processed, args.alpha_threshold),
    }


def write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> int:
    args = parse_args()
    validate_args(args)

    source = Path(args.source)
    output = Path(args.output)
    if not source.is_file():
        raise SystemExit(f"输入文件不存在：{source}")

    original = Image.open(source).convert("RGBA")
    processed = original.copy()

    if args.alpha_mode in ("chroma", "chroma-trim"):
        processed = apply_chroma_key(processed, args.chroma_threshold)

    if args.alpha_mode in ("trim", "chroma-trim"):
        processed = trim_alpha(processed, args.alpha_threshold, args.padding)

    processed = resize_image(processed, args.width, args.height, args.fit)
    data = report_for(source, output, original, processed, args)

    if not args.dry_run:
        output.parent.mkdir(parents=True, exist_ok=True)
        processed.save(output)

    if args.report:
        write_json(Path(args.report), data)

    print(json.dumps(data, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
