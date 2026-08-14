#!/usr/bin/env python3
"""Lightweight solid-background removal for HanziDefend candidate PNGs.

This tool deliberately uses Pillow only. It processes one image at a time and
turns every near-white or near-black pixel into alpha according to an explicit
mode. No flood fill, dilation, model, or neighborhood simulation is involved.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Sequence

from PIL import Image, ImageChops


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = PROJECT_ROOT / "Docs" / "Art" / "_inbox" / "候选"
DEFAULT_OUTPUT = PROJECT_ROOT / "Docs" / "Art" / "_inbox" / "候选处理"
DEFAULT_CATEGORY = "角色图"


class WhiteToAlphaError(Exception):
    """A clear, user-actionable conversion error."""


@dataclass(frozen=True)
class ConversionResult:
    source: str
    output: str
    size: str
    transparentPixels: int
    softEdgePixels: int
    opaquePixels: int
    elapsedSeconds: float
    action: str


def remove_white_background(
    source: Image.Image,
    white_threshold: int = 253,
) -> Image.Image:
    """Make every pixel whose RGB channels meet the threshold transparent."""

    if not 0 <= white_threshold <= 255:
        raise WhiteToAlphaError("white-threshold must be between 0 and 255.")

    rgb = source.convert("RGB")  # Source alpha is intentionally ignored per the art spec.
    red, green, blue = rgb.split()
    minimum_channel = ImageChops.darker(ImageChops.darker(red, green), blue)
    white_mask = minimum_channel.point(
        [255 if value >= white_threshold else 0 for value in range(256)]
    )
    alpha = ImageChops.invert(white_mask)
    clean_channels: list[Image.Image] = []
    for channel in (red, green, blue):
        clean = channel.copy()
        clean.paste(0, mask=white_mask)
        clean_channels.append(clean)
    return Image.merge("RGBA", (*clean_channels, alpha))


def remove_black_background(
    source: Image.Image,
    black_threshold: int = 2,
) -> Image.Image:
    """Make every pixel whose RGB channels are at most the threshold transparent."""

    if not 0 <= black_threshold <= 255:
        raise WhiteToAlphaError("black-threshold must be between 0 and 255.")

    rgb = source.convert("RGB")
    red, green, blue = rgb.split()
    maximum_channel = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    black_mask = maximum_channel.point(
        [255 if value <= black_threshold else 0 for value in range(256)]
    )
    alpha = ImageChops.invert(black_mask)
    clean_channels: list[Image.Image] = []
    for channel in (red, green, blue):
        clean = channel.copy()
        clean.paste(0, mask=black_mask)
        clean_channels.append(clean)
    return Image.merge("RGBA", (*clean_channels, alpha))


def _save_atomic(image: Image.Image, output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=output.parent, suffix=".png", delete=False) as handle:
        temporary = Path(handle.name)
    try:
        image.save(temporary, format="PNG", optimize=True)
        for attempt in range(3):
            try:
                os.replace(temporary, output)
                break
            except PermissionError:
                if attempt == 2:
                    raise
                time.sleep(0.2 * (attempt + 1))
    finally:
        temporary.unlink(missing_ok=True)


def convert_file(
    source: Path,
    output: Path,
    *,
    background: str,
    white_threshold: int,
    black_threshold: int,
    force: bool,
    dry_run: bool,
) -> ConversionResult:
    started = time.perf_counter()
    if output.exists() and not force:
        action = "kept-existing"
        with Image.open(output) as existing:
            existing.load()
            rgba = existing.convert("RGBA")
    else:
        try:
            with Image.open(source) as opened:
                if opened.format != "PNG":
                    raise WhiteToAlphaError(
                        f"'{source.name}' has PNG extension but content format is {opened.format}."
                    )
                opened.load()
                if background == "white":
                    rgba = remove_white_background(
                        opened,
                        white_threshold=white_threshold,
                    )
                elif background == "black":
                    rgba = remove_black_background(
                        opened,
                        black_threshold=black_threshold,
                    )
                else:
                    raise WhiteToAlphaError(f"Unsupported background mode '{background}'.")
        except WhiteToAlphaError:
            raise
        except Exception as exc:
            raise WhiteToAlphaError(f"Cannot process '{source}': {exc}") from exc
        action = "dry-run" if dry_run else "written"
        if not dry_run:
            try:
                _save_atomic(rgba, output)
            except OSError as exc:
                raise WhiteToAlphaError(f"Cannot write '{output}': {exc}") from exc

    histogram = rgba.getchannel("A").histogram()
    return ConversionResult(
        source=str(source),
        output=str(output),
        size=f"{rgba.width}x{rgba.height}",
        transparentPixels=histogram[0],
        softEdgePixels=sum(histogram[1:255]),
        opaquePixels=histogram[255],
        elapsedSeconds=round(time.perf_counter() - started, 3),
        action=action,
    )


def run(args: argparse.Namespace) -> int:
    source_root = args.input.resolve()
    output_root = (
        args.output.resolve()
        if args.direct_output
        else (args.output / args.category).resolve()
    )
    if not source_root.is_dir():
        raise WhiteToAlphaError(f"Input directory does not exist: '{source_root}'.")
    if source_root == output_root or source_root in output_root.parents:
        raise WhiteToAlphaError("Output directory must not be the input directory or inside it.")

    sources = sorted(path for path in source_root.rglob("*") if path.is_file())
    non_png = [path for path in sources if path.suffix.lower() != ".png"]
    if non_png:
        names = ", ".join(str(path.relative_to(source_root)) for path in non_png[:5])
        raise WhiteToAlphaError(f"Only PNG inputs are supported; found: {names}")
    if not sources:
        raise WhiteToAlphaError(f"No PNG files found below '{source_root}'.")

    results: list[ConversionResult] = []
    errors: list[str] = []
    for index, source in enumerate(sources, start=1):
        relative = source.relative_to(source_root)
        output = output_root / relative
        print(f"[{index}/{len(sources)}] {relative}", flush=True)
        try:
            result = convert_file(
                source,
                output,
                background=args.background,
                white_threshold=args.white_threshold,
                black_threshold=args.black_threshold,
                force=args.force,
                dry_run=args.dry_run,
            )
            results.append(result)
            print(
                f"  {result.action}: {result.size}, transparent={result.transparentPixels}, "
                f"soft={result.softEdgePixels}, {result.elapsedSeconds:.3f}s",
                flush=True,
            )
        except WhiteToAlphaError as exc:
            message = f"{relative}: {exc}"
            errors.append(message)
            print(f"  ERROR: {message}", file=sys.stderr, flush=True)

    summary = {
        "input": str(source_root),
        "output": str(output_root),
        "files": len(sources),
        "written": sum(item.action == "written" for item in results),
        "keptExisting": sum(item.action == "kept-existing" for item in results),
        "dryRun": args.dry_run,
        "errors": len(errors),
        "results": [asdict(item) for item in results],
        "errorMessages": errors,
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2), flush=True)
    return 1 if errors else 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Replace every near-white or near-black pixel with alpha using Pillow only."
    )
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--category",
        default=DEFAULT_CATEGORY,
        help="Category folder below --output (default: 角色图).",
    )
    parser.add_argument(
        "--direct-output",
        action="store_true",
        help="Write input-relative categories directly below --output.",
    )
    parser.add_argument(
        "--background",
        choices=("white", "black"),
        default="white",
        help="Solid background color to remove (default: white).",
    )
    parser.add_argument(
        "--white-threshold",
        type=int,
        default=253,
        help="Treat pixels whose R, G, and B are all at least this value as white.",
    )
    parser.add_argument(
        "--black-threshold",
        type=int,
        default=2,
        help="Treat pixels whose R, G, and B are all at most this value as black.",
    )
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    try:
        return run(build_parser().parse_args(argv))
    except WhiteToAlphaError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
