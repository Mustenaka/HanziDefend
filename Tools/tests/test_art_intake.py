from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import art_intake as intake


def request(
    asset_id: str = "zqi",
    source_name: str = "zhong_qi",
    spec: str = "3x1",
    corner: str | None = None,
) -> intake.AssetRequest:
    return intake.AssetRequest(
        id=asset_id,
        category="unit",
        source_name=source_name,
        hanzi="重骑",
        side="shared",
        spec=spec,
        corner=corner,
    )


def candidate(
    path: Path,
    asset_id: str,
    style: str,
    version: int,
    spec: str = "1x1",
) -> intake.Candidate:
    return intake.Candidate(
        path=path,
        parsed=intake.ParsedStem("unit", asset_id, spec, "idle"),
        style=style,
        version=version,
        width=1024,
        height=1024,
        mode="RGB",
        request_id=asset_id,
    )


def test_parser_anchors_multisegment_name_and_style_to_parent_directory(tmp_path: Path) -> None:
    parent = tmp_path / "unit_chong_che_idle_2x2_que_zuo_xia"
    parent.mkdir()
    path = parent / "unit_chong_che_idle_2x2_que_zuo_xia_dan_feng_mu_zhi_v03.png"

    parsed, style, version = intake.parse_candidate_path(path, [])

    assert parsed.category == "unit"
    assert parsed.name == "chong_che"
    assert parsed.spec == "2x2"
    assert parsed.corner == "que_zuo_xia"
    assert style == "dan_feng_mu_zhi"
    assert version == 3


@pytest.mark.parametrize(
    ("stem", "category", "name", "spec"),
    [
        ("cmd_liu_bei_ally_card", "cmd", "liu_bei", "card"),
        ("bld_cheng_enemy_4x2", "bld", "cheng", "4x2"),
        ("icon_rui_yi_buff", "icon", "rui_yi", "buff"),
        ("bg_zhan_chang_decal_03", "bg", "zhan_chang", "decal"),
        ("ui_card_bg_green", "ui", "card_bg_green", ""),
        ("fx_hit", "fx", "hit", ""),
    ],
)
def test_parser_reserves_every_art_category(
    stem: str, category: str, name: str, spec: str
) -> None:
    parsed = intake.parse_asset_stem(stem)
    assert (parsed.category, parsed.name, parsed.spec) == (category, name, spec)


def test_default_selection_uses_highest_version_across_styles(tmp_path: Path) -> None:
    choices = [
        candidate(tmp_path / "v01.png", "zqi", "bai_gang", 1),
        candidate(tmp_path / "v03.png", "zqi", "chi_tong", 3),
        candidate(tmp_path / "v02.png", "zqi", "xuan_tie", 2),
    ]
    config = intake.IntakeConfig((request(),), {})

    selected, issues = intake.choose_candidates(choices, config, None)

    assert not issues
    assert selected["zqi"].selector == "chi_tong_v03"


def test_explicit_selection_accepts_style_and_version_object(tmp_path: Path) -> None:
    choices = [
        candidate(tmp_path / "v01.png", "zqi", "bai_gang", 1),
        candidate(tmp_path / "v03.png", "zqi", "chi_tong", 3),
    ]
    config = intake.IntakeConfig((request(),), {"zqi": {"style": "bai_gang", "version": "v01"}})

    selected, issues = intake.choose_candidates(choices, config, None)

    assert not issues
    assert selected["zqi"].selector == "bai_gang_v01"


def test_ratio_requires_exact_shape_and_two_x_short_edge() -> None:
    intake.validate_ratio(1024, 512, (512, 256), "valid")
    with pytest.raises(intake.IntakeError, match="does not exactly match"):
        intake.validate_ratio(1024, 513, (512, 256), "bad-ratio")
    with pytest.raises(intake.IntakeError, match="below the required 2x"):
        intake.validate_ratio(1022, 511, (512, 256), "too-small")


def test_non_png_bad_name_and_pure_transparent_are_clear_errors(tmp_path: Path) -> None:
    prefix = "unit_zu_idle_1x1"
    parent = tmp_path / prefix
    parent.mkdir()
    text_path = parent / f"{prefix}_dan_feng_v01.jpg"
    text_path.write_text("not an image", encoding="utf-8")
    with pytest.raises(intake.IntakeError, match="Only PNG"):
        intake.inspect_candidate(text_path, [])

    bad_name = parent / f"{prefix}_DanFeng_v01.png"
    Image.new("RGB", (512, 512), "white").save(bad_name)
    with pytest.raises(intake.IntakeError, match="lowercase ASCII"):
        intake.inspect_candidate(bad_name, [])

    transparent = parent / f"{prefix}_dan_feng_v01.png"
    Image.new("RGBA", (512, 512), (0, 0, 0, 0)).save(transparent)
    with pytest.raises(intake.IntakeError, match="pure-transparent"):
        intake.inspect_candidate(transparent, [])


def test_manifest_source_path_maps_chinese_named_precut_rgba(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(intake, "PROJECT_ROOT", tmp_path)
    inbox = tmp_path / "Docs" / "Art" / "_inbox" / "候选处理"
    inbox.mkdir(parents=True)
    source = inbox / "基地5.png"
    image = Image.new("RGBA", (1254, 1254), (0, 0, 0, 0))
    image.paste((120, 60, 30, 255), (200, 150, 1054, 1150))
    image.save(source)
    manifest = tmp_path / "manifest.json"
    manifest.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "assets": [
                    {
                        "id": "bld_ying",
                        "category": "bld",
                        "sourceName": "ying",
                        "hanzi": "营",
                        "side": "ally",
                        "spec": "4x2",
                        "sourcePath": "Docs/Art/_inbox/候选处理/基地5.png",
                    }
                ],
                "selections": {},
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )

    config = intake.load_config(manifest)
    candidates, issues = intake.scan_candidates(inbox, config.assets, None)

    assert not issues
    assert config.assets[0].source_path == source.resolve()
    assert len(candidates) == 1
    assert candidates[0].request_id == "bld_ying"
    assert candidates[0].selector == "source_v01"
    assert candidates[0].precut


def test_precut_rgba_skips_rembg_and_canvas_ratio_but_keeps_target_size(
    tmp_path: Path,
) -> None:
    source = tmp_path / "基地5.png"
    image = Image.new("RGBA", (1254, 1254), (0, 0, 0, 0))
    image.paste((80, 120, 160, 255), (250, 100, 1000, 1150))
    image.save(source)
    asset = intake.AssetRequest(
        "bld_ying",
        "bld",
        "ying",
        "营",
        "ally",
        "4x2",
        source_path=source,
    )
    inspected = intake.inspect_candidate(source, [asset], mapped_request=asset)

    class NoRembg:
        def alpha(self, _image: Image.Image) -> np.ndarray:
            raise AssertionError("pre-cut RGBA must not invoke rembg")

    output, metrics = intake.process_candidate(
        inspected, NoRembg(), add_rim=True, request=asset
    )

    assert output.size == (1024, 512)
    assert metrics.selectedChannel == "precut"
    assert output.getchannel("A").getbbox() is not None


def test_precut_still_requires_two_x_target_short_edge() -> None:
    intake.validate_precut_size(1254, 1254, (1024, 512), "valid-square-canvas")
    with pytest.raises(intake.IntakeError, match="below the required 2x"):
        intake.validate_precut_size(1000, 1000, (1024, 512), "too-small")


def test_precut_l_shape_is_gated_on_source_then_output_corner_is_forced_clear(
    tmp_path: Path,
) -> None:
    source = tmp_path / "冲车.png"
    rgba = np.zeros((1024, 1024, 4), dtype=np.uint8)
    rgba[80:500, 80:500] = (160, 60, 30, 255)
    rgba[80:500, 524:944] = (160, 60, 30, 255)
    rgba[524:944, 524:944] = (160, 60, 30, 255)
    rgba[524:564, 80:120] = (160, 60, 30, 255)  # 0.61% source residue
    Image.fromarray(rgba, "RGBA").save(source)
    asset = intake.AssetRequest(
        "chc",
        "unit",
        "chong_che",
        "冲车",
        "shared",
        "2x2",
        "que_zuo_xia",
        source_path=source,
    )
    inspected = intake.inspect_candidate(source, [asset], mapped_request=asset)

    output, metrics = intake.process_candidate(
        inspected, object(), add_rim=True, request=asset
    )

    assert metrics.cornerCoverage == pytest.approx(1600 / (512 * 512))
    alpha = np.asarray(output.getchannel("A"))
    assert np.count_nonzero(alpha[256:, :256]) == 0
    assert np.count_nonzero(alpha[:256, 256:]) > 0


def test_l_corner_over_eight_percent_fails_and_small_residue_is_cleared() -> None:
    rgba = np.zeros((100, 100, 4), dtype=np.uint8)
    rgba[:30, 50:80, 3] = 255  # 900 / 2500 = 36%
    with pytest.raises(intake.CandidateRejected, match="maximum is 8%"):
        intake.validate_and_clear_l_corner(rgba, "que_you_shang")

    rgba[:] = 0
    rgba[:10, 50:60, :] = 255  # 100 / 2500 = 4%
    cleared, coverage = intake.validate_and_clear_l_corner(rgba, "que_you_shang")
    assert coverage == pytest.approx(0.04)
    assert np.count_nonzero(cleared[:50, 50:, 3]) == 0


def test_flood_fill_handles_near_white_connected_background() -> None:
    rgb = np.full((80, 80, 3), 253, dtype=np.uint8)
    rgb[20:65, 25:55] = (40, 80, 120)

    alpha = intake.flood_fill_alpha(rgb)

    assert alpha[0, 0] == 0
    assert alpha[40, 40] >= 247


def test_clean_alpha_fills_holes() -> None:
    alpha = np.zeros((50, 50), dtype=np.uint8)
    alpha[5:45, 5:45] = 255
    alpha[20:30, 20:30] = 0

    cleaned, _, holes = intake.clean_alpha(alpha)

    assert holes == 1
    assert cleaned[25, 25] == 255


def test_decontaminate_replaces_white_semtransparent_edge() -> None:
    rgb = np.zeros((5, 5, 3), dtype=np.uint8)
    rgb[:] = 255
    rgb[2, 2] = (20, 60, 100)
    alpha = np.zeros((5, 5), dtype=np.uint8)
    alpha[2, 2] = 255
    alpha[2, 1] = 128

    clean = intake.decontaminate(rgb, alpha)

    assert tuple(clean[2, 1]) == (20, 60, 100)
    assert tuple(clean[0, 0]) == (0, 0, 0)


def test_unit_alignment_leaves_eight_percent_bottom_space() -> None:
    image = Image.new("RGBA", (100, 100), (0, 0, 0, 0))
    image.paste(Image.new("RGBA", (30, 70), (255, 0, 0, 255)), (35, 15))

    fitted = intake.fit_to_canvas(image, (256, 256), bottom_align=True)
    alpha = np.asarray(fitted.getchannel("A"))
    occupied_rows = np.flatnonzero(np.any(alpha > 0, axis=1))

    expected_margin = round(256 * intake.BOTTOM_MARGIN_FRACTION)
    assert occupied_rows[-1] == 256 - expected_margin - 1


def test_placeholder_has_faction_color_text_and_transparency() -> None:
    placeholder = intake.placeholder_image(
        intake.AssetRequest("cmd_lv", "cmd", "lv_bu", "吕", "enemy", "card")
    )

    assert placeholder.size == (512, 768)
    alpha = np.asarray(placeholder.getchannel("A"))
    assert alpha.max() == 255
    assert alpha.min() == 0


def test_placeholder_faction_palettes_are_warm_for_ally_and_cold_for_enemy() -> None:
    ally = intake.placeholder_image(
        intake.AssetRequest("zu", "unit", "zu", "卒", "shared", "1x1")
    )
    enemy = intake.placeholder_image(
        intake.AssetRequest("e_lang", "unit", "lang", "狼", "enemy", "1x1")
    )

    ally_pixels = np.asarray(ally)
    enemy_pixels = np.asarray(enemy)
    ally_base, ally_accent = intake.PLACEHOLDER_PALETTES["ally"]
    enemy_base, enemy_accent = intake.PLACEHOLDER_PALETTES["enemy"]

    assert np.any(np.all(ally_pixels == ally_base, axis=2))
    assert np.any(np.all(ally_pixels == ally_accent, axis=2))
    assert not np.any(np.all(ally_pixels == enemy_base, axis=2))
    assert np.any(np.all(enemy_pixels == enemy_base, axis=2))
    assert np.any(np.all(enemy_pixels == enemy_accent, axis=2))
    assert not np.any(np.all(enemy_pixels == ally_base, axis=2))


def test_every_shared_pool_enemy_output_uses_cold_palette(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(intake, "DEFAULT_ART_ROOT", tmp_path)
    config = intake.load_config(intake.DEFAULT_MANIFEST)
    shared_units = [
        request
        for request in config.assets
        if request.category == "unit" and request.side == "shared"
    ]
    assert len(shared_units) == 14

    ally_base = intake.PLACEHOLDER_PALETTES["ally"][0]
    enemy_base = intake.PLACEHOLDER_PALETTES["enemy"][0]
    for shared in shared_units:
        primary = intake.placeholder_image(shared)
        paths, actions = intake.write_outputs(
            shared, primary, force=True, dry_run=False, placeholder=True
        )

        assert actions == ["written", "written"]
        assert len(paths) == 2
        ally_pixels = np.asarray(
            Image.open(tmp_path / "Units" / shared.id / "idle.png").convert("RGBA")
        )
        enemy_pixels = np.asarray(
            Image.open(tmp_path / "Units" / f"{shared.id}_enemy" / "idle.png").convert("RGBA")
        )
        assert np.any(np.all(ally_pixels == ally_base, axis=2)), shared.id
        assert np.any(np.all(enemy_pixels == enemy_base, axis=2)), shared.id
        assert not np.any(np.all(enemy_pixels == ally_base, axis=2)), shared.id


def test_all_unit_placeholder_pixels_and_multiglyph_labels_stay_inside_block() -> None:
    config = intake.load_config(intake.DEFAULT_MANIFEST)
    units = [request for request in config.assets if request.category == "unit"]

    assert len(units) == 17
    for unit in units:
        placeholder = intake.placeholder_image(unit)
        assert placeholder.size == intake.target_size_for(unit), unit.id
        pixels = np.asarray(placeholder)
        _, rect, _ = intake._placeholder_geometry(unit)
        left, top, right, bottom = rect
        outside = np.ones(pixels.shape[:2], dtype=bool)
        outside[top : bottom + 1, left : right + 1] = False
        assert not np.any(pixels[..., 3][outside]), unit.id

        if len(unit.hanzi) <= 1:
            continue
        text_fill = np.all(pixels[..., :3] == (244, 225, 174), axis=2) & (pixels[..., 3] > 0)
        boxes = intake._placeholder_text_boxes(unit, rect)
        assert len(boxes) == len(unit.hanzi), unit.id
        for glyph, box in boxes:
            x0, y0, x1, y1 = box
            assert np.count_nonzero(text_fill[y0 : y1 + 1, x0 : x1 + 1]) > 8, (
                unit.id,
                glyph,
            )

        allowed = np.zeros(pixels.shape[:2], dtype=bool)
        for _, box in boxes:
            x0, y0, x1, y1 = box
            allowed[y0 : y1 + 1, x0 : x1 + 1] = True
        assert not np.any(text_fill & ~allowed), unit.id


def test_l_shaped_placeholder_clears_declared_corner_without_source_validation() -> None:
    placeholder = intake.placeholder_image(
        intake.AssetRequest(
            "nuc", "unit", "nu_che", "弩车", "shared", "2x2", "que_you_shang"
        )
    )

    alpha = np.asarray(placeholder.getchannel("A"))
    half_y, half_x = alpha.shape[0] // 2, alpha.shape[1] // 2
    assert np.count_nonzero(alpha[0:half_y, half_x:]) == 0
    assert np.count_nonzero(alpha[half_y:, 0:half_x]) > 0


def test_manifest_rejects_unknown_selection_and_bad_target_size(tmp_path: Path) -> None:
    path = tmp_path / "manifest.json"
    path.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "assets": [
                    {
                        "id": "custom_ui",
                        "category": "ui",
                        "sourceName": "custom",
                        "hanzi": "测",
                        "side": "neutral",
                        "spec": "",
                        "targetSize": [0, 128],
                    }
                ],
                "selections": {"missing": "v01"},
            }
        ),
        encoding="utf-8",
    )

    with pytest.raises(intake.IntakeError, match="targetSize"):
        intake.load_config(path)


def test_production_manifest_covers_wo_a1_batch_and_explicit_selections() -> None:
    config = intake.load_config(intake.DEFAULT_MANIFEST)
    counts = {
        category: sum(asset.category == category for asset in config.assets)
        for category in ("unit", "cmd", "bld", "icon", "fx")
    }

    assert len(config.assets) == 68
    assert counts == {"unit": 17, "cmd": 2, "bld": 2, "icon": 20, "fx": 27}
    assert config.selections == {
        "dun": "cheng_men_v05",
        "e_lang": "dan_feng_v03",
        "e_liu": "xuan_qi_v03",
        "zqi": "bai_gang_v03",
    }
    mapped = [asset for asset in config.assets if asset.source_path is not None]
    assert len(mapped) == 49
    assert all(asset.source_path.is_file() for asset in mapped)


def test_placeholder_review_records_keep_each_requests_output(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    art_root = tmp_path / "Assets" / "Art"
    review_root = tmp_path / "review"
    inbox = tmp_path / "empty-inbox"
    inbox.mkdir()
    manifest = tmp_path / "manifest.json"
    manifest.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "assets": [
                    {
                        "id": "cmd_bei",
                        "category": "cmd",
                        "sourceName": "liu_bei",
                        "hanzi": "备",
                        "side": "ally",
                        "spec": "card",
                    },
                    {
                        "id": "cmd_lv",
                        "category": "cmd",
                        "sourceName": "lv_bu",
                        "hanzi": "吕",
                        "side": "enemy",
                        "spec": "card",
                    },
                ],
                "selections": {},
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )
    monkeypatch.setattr(intake, "DEFAULT_ART_ROOT", art_root)
    args = argparse.Namespace(
        manifest=manifest,
        only=[],
        placeholders_only=True,
        inbox=inbox,
        no_rim=False,
        force=True,
        dry_run=False,
        review=review_root,
    )

    assert intake.run(args) == 0

    report = json.loads(
        (review_root / f"{intake.date.today().isoformat()}.json").read_text(
            encoding="utf-8"
        )
    )
    placeholders = {record["id"]: record["output"] for record in report["records"]}
    assert "Commanders/cmd_bei/card.png" in placeholders["cmd_bei"]
    assert "Commanders/cmd_lv/card.png" in placeholders["cmd_lv"]


def test_write_png_respects_dry_run_and_force(tmp_path: Path) -> None:
    path = tmp_path / "sprite.png"
    first = Image.new("RGBA", (2, 2), (255, 0, 0, 255))
    second = Image.new("RGBA", (2, 2), (0, 255, 0, 255))

    assert intake.write_png(path, first, force=False, dry_run=True) == "dry-run"
    assert not path.exists()
    assert intake.write_png(path, first, force=False, dry_run=False) == "written"
    assert intake.write_png(path, second, force=False, dry_run=False) == "kept-existing"
    assert Image.open(path).getpixel((0, 0))[:3] == (255, 0, 0)
    assert intake.write_png(path, second, force=True, dry_run=False) == "written"
    assert Image.open(path).getpixel((0, 0))[:3] == (0, 255, 0)
