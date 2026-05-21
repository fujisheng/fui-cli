#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FUI UI 资源单图后处理工具。

只允许处理 imagegen 已生成的位图：透明、裁边、缩放、校验和输出报告。
不要用这个脚本绘制最终美术，也不要用它修改 Unity prefab。
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import math

try:
    from PIL import Image, ImageFilter
except ImportError as exc:  # pragma: no cover - 环境缺依赖时给明确错误
    raise SystemExit("缺少 Pillow。请先安装 Pillow，或使用已包含 Pillow 的 Python 环境。") from exc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Post-process one generated UI bitmap for FUI-CLI assets."
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
        choices=("keep", "trim", "chroma", "chroma-trim", "chroma-soft", "chroma-soft-trim"),
        default="keep",
        help="透明处理模式",
    )
    parser.add_argument(
        "--chroma-key-color",
        default="",
        help="可选 chroma key 颜色，例如 #ff00ff。省略时从角落或边框采样。",
    )
    parser.add_argument(
        "--chroma-auto-key",
        choices=("corners", "border", "none"),
        default="corners",
        help="未指定 --chroma-key-color 时的 key 色采样方式。",
    )
    parser.add_argument(
        "--transparent-threshold",
        type=float,
        default=18,
        help="soft chroma 下完全透明的颜色距离阈值",
    )
    parser.add_argument(
        "--opaque-threshold",
        type=float,
        default=180,
        help="soft chroma 下完全不透明的颜色距离阈值",
    )
    parser.add_argument("--edge-contract", type=int, default=0, help="扣色后收缩 alpha 边缘像素数")
    parser.add_argument("--edge-feather", type=float, default=0, help="扣色后羽化 alpha 半径")
    parser.add_argument("--despill", action="store_true", help="降低 chroma key 边缘染色")
    parser.add_argument("--alpha-threshold", type=int, default=8, help="alpha 裁边阈值")
    parser.add_argument("--chroma-threshold", type=int, default=28, help="背景色抠除阈值")
    parser.add_argument(
        "--max-chroma-residue-ratio",
        type=float,
        default=-1,
        help="大于等于 0 时校验可见像素中的 chroma 残留比例，超限则失败",
    )
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

    if args.transparent_threshold < 0 or args.opaque_threshold < 0:
        raise SystemExit("--transparent-threshold/--opaque-threshold 不能为负数。")

    if args.transparent_threshold >= args.opaque_threshold:
        raise SystemExit("--transparent-threshold 必须小于 --opaque-threshold。")

    if args.edge_contract < 0:
        raise SystemExit("--edge-contract 不能为负数。")

    if args.edge_feather < 0:
        raise SystemExit("--edge-feather 不能为负数。")

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


def parse_hex_color(value: str) -> tuple[int, int, int] | None:
    normalized = value.strip()
    if not normalized:
        return None

    if normalized.startswith("#"):
        normalized = normalized[1:]

    if len(normalized) != 6:
        raise SystemExit("--chroma-key-color 必须是 #RRGGBB 格式。")

    try:
        return tuple(int(normalized[index : index + 2], 16) for index in (0, 2, 4))
    except ValueError as exc:
        raise SystemExit("--chroma-key-color 必须是 #RRGGBB 格式。") from exc


def average_color(colors: list[tuple[int, int, int]]) -> tuple[int, int, int]:
    count = max(1, len(colors))
    return (
        round(sum(color[0] for color in colors) / count),
        round(sum(color[1] for color in colors) / count),
        round(sum(color[2] for color in colors) / count),
    )


def image_data(image: Image.Image):
    if hasattr(image, "get_flattened_data"):
        return image.get_flattened_data()

    return image.getdata()


def border_colors(image: Image.Image, max_samples: int = 4000) -> list[tuple[int, int, int]]:
    width, height = image.size
    colors: list[tuple[int, int, int]] = []
    if width <= 0 or height <= 0:
        return colors

    step = max(1, math.ceil((width * 2 + height * 2) / max_samples))
    for x in range(0, width, step):
        for y in (0, height - 1):
            r, g, b, _ = image.getpixel((x, y))
            colors.append((r, g, b))

    for y in range(0, height, step):
        for x in (0, width - 1):
            r, g, b, _ = image.getpixel((x, y))
            colors.append((r, g, b))

    return colors


def resolve_key_colors(image: Image.Image, args: argparse.Namespace) -> list[tuple[int, int, int]]:
    explicit = parse_hex_color(args.chroma_key_color)
    if explicit is not None:
        return [explicit]

    if args.chroma_auto_key == "none":
        return corner_colors(image)

    if args.chroma_auto_key == "border":
        colors = border_colors(image)
        return [average_color(colors)] if colors else corner_colors(image)

    return corner_colors(image)


def apply_chroma_key(image: Image.Image, args: argparse.Namespace) -> Image.Image:
    colors = resolve_key_colors(image, args)
    pixels = []

    for r, g, b, a in image_data(image):
        current = (r, g, b)
        if min(color_distance(current, sample) for sample in colors) <= args.chroma_threshold:
            pixels.append((r, g, b, 0))
            continue

        pixels.append((r, g, b, a))

    result = Image.new("RGBA", image.size)
    result.putdata(pixels)
    return result


def apply_soft_chroma_key(image: Image.Image, args: argparse.Namespace) -> Image.Image:
    key = average_color(resolve_key_colors(image, args))
    pixels = []
    ramp = max(1.0, args.opaque_threshold - args.transparent_threshold)

    for r, g, b, a in image_data(image):
        distance = color_distance((r, g, b), key)
        if distance <= args.transparent_threshold:
            pixels.append((r, g, b, 0))
            continue

        if distance >= args.opaque_threshold:
            alpha_factor = 1.0
        else:
            alpha_factor = (distance - args.transparent_threshold) / ramp

        next_alpha = round(a * alpha_factor)
        if args.despill and next_alpha > 0:
            edge = max(0.0, 1.0 - alpha_factor)
            r, g, b = despill_color((r, g, b), key, edge)

        pixels.append((r, g, b, next_alpha))

    result = Image.new("RGBA", image.size)
    result.putdata(pixels)
    result = contract_alpha(result, args.edge_contract)
    if args.edge_feather > 0:
        alpha = result.getchannel("A").filter(ImageFilter.GaussianBlur(args.edge_feather))
        result.putalpha(alpha)
    return result


def despill_color(color: tuple[int, int, int], key: tuple[int, int, int], edge: float) -> tuple[int, int, int]:
    values = list(color)
    key_index = max(range(3), key=lambda index: key[index])
    other_indexes = [index for index in range(3) if index != key_index]
    target = max(values[index] for index in other_indexes)
    if values[key_index] > target:
        values[key_index] = round(values[key_index] * (1.0 - edge * 0.65) + target * edge * 0.65)
    return tuple(max(0, min(255, value)) for value in values)


def contract_alpha(image: Image.Image, amount: int) -> Image.Image:
    if amount <= 0:
        return image

    alpha = image.getchannel("A")
    for _ in range(amount):
        alpha = alpha.filter(ImageFilter.MinFilter(3))

    result = image.copy()
    result.putalpha(alpha)
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
    visible = sum(1 for value in image_data(alpha) if value > threshold)
    total = image.width * image.height
    return round(visible / total, 6) if total > 0 else 0.0


def chroma_residue(image: Image.Image, args: argparse.Namespace) -> dict:
    key = average_color(resolve_key_colors(image, args))
    residue_count = 0
    visible_count = 0
    for r, g, b, a in image_data(image):
        if a <= args.alpha_threshold:
            continue

        visible_count += 1
        if color_distance((r, g, b), key) <= args.chroma_threshold:
            residue_count += 1

    ratio = round(residue_count / visible_count, 6) if visible_count > 0 else 0
    ok = args.max_chroma_residue_ratio < 0 or ratio <= args.max_chroma_residue_ratio
    return {
        "keyColor": f"#{key[0]:02x}{key[1]:02x}{key[2]:02x}",
        "count": residue_count,
        "visibleCount": visible_count,
        "ratio": ratio,
        "maxRatio": None if args.max_chroma_residue_ratio < 0 else args.max_chroma_residue_ratio,
        "ok": ok,
    }


def report_for(
    source: Path,
    output: Path,
    original: Image.Image,
    processed: Image.Image,
    args: argparse.Namespace,
) -> dict:
    bbox = alpha_bbox(processed, args.alpha_threshold)
    data = {
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
    if args.alpha_mode.startswith("chroma") or args.max_chroma_residue_ratio >= 0:
        data["chromaResidue"] = chroma_residue(processed, args)

    return data


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
        processed = apply_chroma_key(processed, args)

    if args.alpha_mode in ("chroma-soft", "chroma-soft-trim"):
        processed = apply_soft_chroma_key(processed, args)

    if args.alpha_mode in ("trim", "chroma-trim", "chroma-soft-trim"):
        processed = trim_alpha(processed, args.alpha_threshold, args.padding)

    processed = resize_image(processed, args.width, args.height, args.fit)
    data = report_for(source, output, original, processed, args)
    residue = data.get("chromaResidue")
    if residue and not residue["ok"]:
        raise SystemExit(
            "chroma 残留超限："
            f"{residue['ratio']} > {residue['maxRatio']}，key={residue['keyColor']}"
        )

    if not args.dry_run:
        output.parent.mkdir(parents=True, exist_ok=True)
        processed.save(output)

    if args.report:
        write_json(Path(args.report), data)

    print(json.dumps(data, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
