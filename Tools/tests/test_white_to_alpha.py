from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import white_to_alpha as converter


def test_global_white_removal_includes_enclosed_regions() -> None:
    source = Image.new("RGB", (64, 64), "white")
    draw = ImageDraw.Draw(source)
    draw.rectangle((8, 8, 55, 55), outline=(20, 30, 40), width=5)

    result = converter.remove_white_background(source)

    assert result.getpixel((0, 0))[3] == 0
    assert result.getpixel((32, 32))[3] == 0
    assert result.getpixel((8, 8))[3] == 255


def test_threshold_is_direct_rgb_value_and_source_alpha_is_ignored() -> None:
    source = Image.new("RGBA", (3, 1))
    source.putdata(
        [
            (253, 253, 253, 255),
            (252, 252, 252, 255),
            (0, 0, 0, 0),
        ]
    )

    result = converter.remove_white_background(source, white_threshold=253)

    assert [result.getpixel((x, 0))[3] for x in range(3)] == [0, 255, 255]


def test_nonwhite_pixels_are_byte_for_byte_unchanged() -> None:
    source = Image.new("RGB", (3, 1))
    source.putdata([(255, 255, 255), (252, 252, 252), (12, 34, 56)])

    result = converter.remove_white_background(source)

    assert result.getpixel((0, 0)) == (0, 0, 0, 0)
    assert result.getpixel((1, 0)) == (252, 252, 252, 255)
    assert result.getpixel((2, 0)) == (12, 34, 56, 255)


def test_global_black_removal_uses_direct_rgb_threshold() -> None:
    source = Image.new("RGB", (4, 1))
    source.putdata([(0, 0, 0), (2, 2, 2), (3, 3, 3), (0, 4, 1)])

    result = converter.remove_black_background(source, black_threshold=2)

    assert result.getpixel((0, 0)) == (0, 0, 0, 0)
    assert result.getpixel((1, 0)) == (0, 0, 0, 0)
    assert result.getpixel((2, 0)) == (3, 3, 3, 255)
    assert result.getpixel((3, 0)) == (0, 4, 1, 255)
