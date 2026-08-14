#!/usr/bin/env python3
"""Validate, cut out, normalize, and intake HanziDefend art candidates.

The selection/placeholder contract lives in ``Tools/art_intake_manifest.json``.
All model imports are lazy so filename and ratio tests do not require rembg.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import math
import os
import re
import sys
import tempfile
import time
from dataclasses import asdict, dataclass, field
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Any, Sequence

import numpy as np
from PIL import Image, ImageDraw, ImageFont

# Candidate cut-out uses scipy, while placeholder generation only needs NumPy/Pillow.
# Keep the heavy binary dependency lazy so --inbox can point at an empty directory on
# artist machines whose intake environment is not installed yet.
ndimage: Any = None


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INBOX = PROJECT_ROOT / "Docs" / "Art" / "_inbox" / "候选处理" / "角色图"
DEFAULT_REVIEW = PROJECT_ROOT / "Docs" / "Art" / "_review"
DEFAULT_ART_ROOT = PROJECT_ROOT / "Assets" / "Art"
DEFAULT_MANIFEST = Path(__file__).with_name("art_intake_manifest.json")

FILENAME_RE = re.compile(r"^[a-z0-9_]+\.png$")
GRID_RE = re.compile(r"^(?P<w>\d+)x(?P<h>\d+)$")

ALLOWED_CATEGORIES = {"unit", "cmd", "bld", "icon", "bg", "ui", "fx"}
ALLOWED_CORNERS = {"que_you_shang", "que_zuo_xia"}
ALLOWED_SIDES = {"shared", "ally", "enemy", "neutral"}
L_CORNER_MAX_COVERAGE = 0.08
ALPHA_DIFFERENCE_WARNING = 0.12
BOTTOM_MARGIN_FRACTION = 0.08
RIM_WIDTH = 7
PLACEHOLDER_PALETTES = {
    # M1-04 faction rule: allies are warm (vermilion + gamboge), enemies are
    # cold (indigo + dark purple). ``shared`` is an ownership label, not a
    # presentation colour, so its primary output deliberately resolves to ally.
    "ally": ((178, 58, 45, 255), (230, 180, 55, 255)),
    "enemy": ((42, 62, 122, 255), (100, 49, 119, 255)),
    "neutral": ((95, 91, 82, 255), (176, 165, 135, 255)),
}
_DLL_DIRECTORY_HANDLES: list[Any] = []
_DLL_LIBRARY_HANDLES: list[Any] = []


class IntakeError(Exception):
    """A user-actionable validation or processing failure."""


class CandidateRejected(IntakeError):
    """A candidate failed a quality gate and must fall back to a placeholder."""


def require_ndimage() -> Any:
    global ndimage
    if ndimage is not None:
        return ndimage
    try:
        from scipy import ndimage as scipy_ndimage
    except (ImportError, OSError) as exc:
        raise IntakeError(
            "Candidate processing needs a compatible scipy installation; "
            "placeholder-only runs do not."
        ) from exc
    ndimage = scipy_ndimage
    return ndimage


def _prepare_windows_cuda_dlls() -> None:
    """Load pip-installed CUDA libraries before ONNX Runtime sees system DLLs.

    Several artist workstations also have a system-wide CUDA/cuDNN installation.
    Loading the environment-local libraries by absolute path prevents an older
    system cuDNN from winning Windows' DLL search and failing halfway through
    birefnet inference.
    """

    if os.name != "nt" or _DLL_LIBRARY_HANDLES:
        return

    nvidia_root = Path(sys.prefix) / "Lib" / "site-packages" / "nvidia"
    if not nvidia_root.is_dir():
        raise IntakeError(
            "pip-installed NVIDIA runtime libraries were not found in the active "
            "environment. Install Tools/requirements-art-intake.txt in the dedicated "
            "conda environment."
        )

    package_order = (
        "cuda_runtime",
        "nvjitlink",
        "cuda_nvrtc",
        "cublas",
        "cufft",
        "curand",
        "cudnn",
    )
    package_directories = [
        (package, nvidia_root / package / "bin") for package in package_order
    ]
    package_directories = [
        (package, directory)
        for package, directory in package_directories
        if directory.is_dir()
    ]
    if not package_directories:
        raise IntakeError(f"No NVIDIA DLL directories were found below '{nvidia_root}'.")

    for _, dll_directory in package_directories:
        _DLL_DIRECTORY_HANDLES.append(os.add_dll_directory(str(dll_directory)))
    os.environ["PATH"] = os.pathsep.join(
        [
            *(str(directory) for _, directory in package_directories),
            os.environ.get("PATH", ""),
        ]
    )

    preferred_names = {
        "cublas": ("cublasLt64_12.dll", "cublas64_12.dll", "nvblas64_12.dll"),
        "cuda_nvrtc": (
            "nvrtc-builtins64_129.dll",
            "nvrtc64_120_0.dll",
        ),
        "cudnn": (
            "cudnn64_9.dll",
            "cudnn_ops64_9.dll",
            "cudnn_graph64_9.dll",
            "cudnn_engines_precompiled64_9.dll",
            "cudnn_engines_tensor_ir64_9.dll",
            "cudnn_engines_runtime_compiled64_9.dll",
            "cudnn_heuristic64_9.dll",
            "cudnn_adv64_9.dll",
            "cudnn_cnn64_9.dll",
            "cudnn_ext64_9.dll",
        ),
    }

    for package, directory in package_directories:
        dll_names = preferred_names.get(package)
        if dll_names is None:
            dll_paths = sorted(directory.glob("*.dll"))
        else:
            dll_paths = [directory / name for name in dll_names if (directory / name).is_file()]
        for dll_path in dll_paths:
            try:
                _DLL_LIBRARY_HANDLES.append(ctypes.WinDLL(str(dll_path)))
            except OSError as exc:  # pragma: no cover - workstation native runtime
                raise IntakeError(
                    f"Could not load environment-local CUDA library '{dll_path.name}': {exc}"
                ) from exc


@dataclass(frozen=True)
class AssetRequest:
    id: str
    category: str
    source_name: str
    hanzi: str
    side: str
    spec: str
    corner: str | None = None
    index: str | None = None
    target_size: tuple[int, int] | None = None
    source_path: Path | None = None
    candidate_prefix: str | None = None

    @property
    def expected_prefix(self) -> str:
        if self.category == "unit":
            suffix = f"unit_{self.source_name}_idle_{self.spec}"
            return f"{suffix}_{self.corner}" if self.corner else suffix
        if self.category == "cmd":
            return f"cmd_{self.source_name}_{self.side}_{self.spec}"
        if self.category == "bld":
            return f"bld_{self.source_name}_{self.side}_{self.spec}"
        if self.category == "icon":
            return f"icon_{self.source_name}_{self.spec}"
        if self.category == "bg":
            suffix = f"bg_{self.source_name}_{self.spec}"
            return f"{suffix}_{self.index}" if self.index else suffix
        return f"{self.category}_{self.source_name}"

    @property
    def scan_prefix(self) -> str:
        """Candidate naming may differ from the runtime/output category.

        This is used for the processed Lu Bu unit portrait that intentionally feeds the
        commander card without copying or renaming the user's inbox source.
        """
        return self.candidate_prefix or self.expected_prefix


@dataclass(frozen=True)
class ParsedStem:
    category: str
    name: str
    spec: str
    state: str | None = None
    side: str | None = None
    corner: str | None = None
    index: str | None = None


@dataclass(frozen=True)
class Candidate:
    path: Path
    parsed: ParsedStem
    style: str
    version: int
    width: int
    height: int
    mode: str
    request_id: str | None
    precut: bool = False

    @property
    def selector(self) -> str:
        return f"{self.style}_v{self.version:02d}"


@dataclass
class Issue:
    severity: str
    code: str
    message: str
    path: str | None = None


@dataclass
class QualityMetrics:
    selectedChannel: str
    alphaDifference: float
    rembgScore: float
    floodScore: float
    foregroundCoverage: float
    borderCoverage: float
    componentsRemoved: int
    holesFilled: int
    cornerCoverage: float | None = None
    rembgProvider: str | None = None


@dataclass
class ReviewRecord:
    id: str
    source: str | None
    selector: str | None
    selected: bool
    status: str
    originalSize: str | None
    output: str | None
    placeholder: bool = False
    metrics: QualityMetrics | None = None
    messages: list[str] = field(default_factory=list)
    preview: Image.Image | None = field(default=None, repr=False, compare=False)

    def serializable(self) -> dict[str, Any]:
        result = asdict(self)
        result.pop("preview", None)
        return result


@dataclass(frozen=True)
class IntakeConfig:
    assets: tuple[AssetRequest, ...]
    selections: dict[str, Any]


class RembgRunner:
    def __init__(self) -> None:
        self._session: Any = None
        self.provider = "uninitialized"

    def ensure_session(self) -> None:
        if self._session is not None:
            return
        _prepare_windows_cuda_dlls()
        try:
            import onnxruntime as ort
        except Exception as exc:  # pragma: no cover - depends on workstation DLLs
            raise IntakeError(
                "onnxruntime-gpu could not load. Install Tools/requirements-art-intake.txt "
                f"in a clean Python environment. Original error: {exc}"
            ) from exc

        providers = ort.get_available_providers()
        if "CUDAExecutionProvider" not in providers:
            raise IntakeError(
                "CUDAExecutionProvider is unavailable; GPU rembg is required for this pipeline. "
                f"Available providers: {providers}"
            )

        try:
            from rembg import new_session

            session_options = ort.SessionOptions()
            self._session = new_session(
                "birefnet-general",
                sess_opts=session_options,
                providers=["CUDAExecutionProvider"],
            )
            self._session.inner_session.disable_fallback()
        except Exception as exc:  # pragma: no cover - model/runtime dependent
            raise IntakeError(f"Could not initialize rembg birefnet-general on CUDA: {exc}") from exc
        active_providers = self._session.inner_session.get_providers()
        if not active_providers or active_providers[0] != "CUDAExecutionProvider":
            raise IntakeError(
                "rembg session is not CUDA-first; active providers: "
                f"{active_providers}"
            )
        self.provider = "CUDAExecutionProvider"

    def alpha(self, rgb_image: Image.Image) -> np.ndarray:
        self.ensure_session()
        try:
            from rembg import remove

            result = remove(rgb_image, session=self._session, only_mask=True)
        except Exception as exc:  # pragma: no cover - model/runtime dependent
            raise IntakeError(f"rembg inference failed: {exc}") from exc
        if not isinstance(result, Image.Image):
            result = Image.fromarray(np.asarray(result))
        return np.asarray(result.convert("L"), dtype=np.uint8)


def normalize_rel(path: Path) -> str:
    try:
        return path.resolve().relative_to(PROJECT_ROOT.resolve()).as_posix()
    except ValueError:
        return path.resolve().as_posix()


def load_config(path: Path) -> IntakeConfig:
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise IntakeError(f"Cannot read selection manifest '{path}': {exc}") from exc
    if raw.get("schemaVersion") != 1:
        raise IntakeError("Selection manifest schemaVersion must be 1.")
    if not isinstance(raw.get("assets"), list) or not isinstance(raw.get("selections", {}), dict):
        raise IntakeError("Selection manifest requires an assets array and selections object.")

    assets: list[AssetRequest] = []
    seen_ids: set[str] = set()
    seen_prefixes: set[str] = set()
    seen_source_paths: set[Path] = set()
    for index, item in enumerate(raw["assets"]):
        context = f"assets[{index}]"
        if not isinstance(item, dict):
            raise IntakeError(f"{context} must be an object.")
        required = ("id", "category", "sourceName", "hanzi", "side")
        missing = [key for key in required if not isinstance(item.get(key), str) or not item[key]]
        if missing:
            raise IntakeError(f"{context} is missing non-empty fields: {', '.join(missing)}")
        category = item["category"]
        if not isinstance(item.get("spec"), str):
            raise IntakeError(f"{context}.spec must be a string (empty for ui/fx).")
        if category not in {"ui", "fx"} and not item["spec"]:
            raise IntakeError(f"{context}.spec cannot be empty for category '{category}'.")
        side = item["side"]
        corner = item.get("corner")
        asset_index = item.get("index")
        if category not in ALLOWED_CATEGORIES:
            raise IntakeError(f"{context}.category '{category}' is unsupported.")
        if side not in ALLOWED_SIDES:
            raise IntakeError(f"{context}.side '{side}' is unsupported.")
        if corner is not None and corner not in ALLOWED_CORNERS:
            raise IntakeError(f"{context}.corner '{corner}' is unsupported.")
        if asset_index is not None and not re.fullmatch(r"\d{2}", str(asset_index)):
            raise IntakeError(f"{context}.index must be a two-digit string.")
        target_size: tuple[int, int] | None = None
        if "targetSize" in item:
            value = item["targetSize"]
            if (
                not isinstance(value, list)
                or len(value) != 2
                or not all(isinstance(number, int) and number > 0 for number in value)
            ):
                raise IntakeError(f"{context}.targetSize must be [positiveWidth, positiveHeight].")
            target_size = (value[0], value[1])
        source_path: Path | None = None
        if "sourcePath" in item:
            value = item["sourcePath"]
            if not isinstance(value, str) or not value.strip():
                raise IntakeError(f"{context}.sourcePath must be a non-empty string.")
            source_path = Path(value)
            if not source_path.is_absolute():
                source_path = PROJECT_ROOT / source_path
            source_path = source_path.resolve()
            if source_path.suffix.lower() != ".png":
                raise IntakeError(f"{context}.sourcePath must point to a PNG file.")
        candidate_prefix: str | None = None
        if "candidatePrefix" in item:
            value = item["candidatePrefix"]
            if not isinstance(value, str) or not value.strip():
                raise IntakeError(f"{context}.candidatePrefix must be a non-empty string.")
            candidate_prefix = value
            parse_asset_stem(candidate_prefix)

        request = AssetRequest(
            id=item["id"],
            category=category,
            source_name=item["sourceName"],
            hanzi=item["hanzi"],
            side=side,
            spec=item["spec"],
            corner=corner,
            index=str(asset_index) if asset_index is not None else None,
            target_size=target_size,
            source_path=source_path,
            candidate_prefix=candidate_prefix,
        )
        if request.id in seen_ids:
            raise IntakeError(f"Selection manifest has duplicate id '{request.id}'.")
        if request.scan_prefix in seen_prefixes:
            raise IntakeError(f"Selection manifest has duplicate source prefix '{request.scan_prefix}'.")
        if request.source_path is not None and request.source_path in seen_source_paths:
            raise IntakeError(
                "Selection manifest maps more than one asset to sourcePath "
                f"'{normalize_rel(request.source_path)}'."
            )
        parse_asset_stem(request.expected_prefix)
        target_size_for(request)
        seen_ids.add(request.id)
        seen_prefixes.add(request.scan_prefix)
        if request.source_path is not None:
            seen_source_paths.add(request.source_path)
        assets.append(request)

    unknown_selections = set(raw.get("selections", {})) - seen_ids
    if unknown_selections:
        raise IntakeError(
            "Selections reference unknown ids: " + ", ".join(sorted(unknown_selections))
        )
    return IntakeConfig(tuple(assets), dict(raw.get("selections", {})))


def parse_asset_stem(stem: str) -> ParsedStem:
    if not re.fullmatch(r"[a-z0-9_]+", stem):
        raise IntakeError(f"Asset stem '{stem}' must use lowercase ASCII, digits, and underscores.")
    if stem.startswith("unit_"):
        match = re.fullmatch(
            r"unit_(?P<name>[a-z0-9_]+)_idle_(?P<spec>\d+x\d+)"
            r"(?:_(?P<corner>que_(?:you_shang|zuo_xia)))?",
            stem,
        )
        if not match:
            raise IntakeError(f"Unit stem '{stem}' must end with idle_NxM[_que_*].")
        return ParsedStem(
            "unit", match["name"], match["spec"], "idle", corner=match["corner"]
        )
    if stem.startswith("cmd_"):
        match = re.fullmatch(
            r"cmd_(?P<name>[a-z0-9_]+)_(?P<side>ally|enemy)_(?P<spec>card|avatar)", stem
        )
        if not match:
            raise IntakeError(f"Commander stem '{stem}' must end with side_card|avatar.")
        return ParsedStem("cmd", match["name"], match["spec"], side=match["side"])
    if stem.startswith("bld_"):
        match = re.fullmatch(
            r"bld_(?P<name>[a-z0-9_]+)_(?P<side>ally|enemy)_(?P<spec>\d+x\d+)", stem
        )
        if not match:
            raise IntakeError(f"Building stem '{stem}' must end with side_NxM.")
        return ParsedStem("bld", match["name"], match["spec"], side=match["side"])
    if stem.startswith("icon_"):
        match = re.fullmatch(r"icon_(?P<name>[a-z0-9_]+)_(?P<spec>buff|skill|res)", stem)
        if not match:
            raise IntakeError(f"Icon stem '{stem}' must end with buff|skill|res.")
        return ParsedStem("icon", match["name"], match["spec"])
    if stem.startswith("bg_"):
        match = re.fullmatch(
            r"bg_(?P<name>[a-z0-9_]+)_(?P<kind>tile|decal|full)(?:_(?P<index>\d{2}))?",
            stem,
        )
        if not match:
            raise IntakeError(f"Background stem '{stem}' must end with tile|decal|full[_NN].")
        if match["kind"] != "decal" and match["index"]:
            raise IntakeError(f"Only bg decal may carry an NN index: '{stem}'.")
        return ParsedStem("bg", match["name"], match["kind"], index=match["index"])
    for category in ("ui", "fx"):
        prefix = category + "_"
        if stem.startswith(prefix) and len(stem) > len(prefix):
            return ParsedStem(category, stem[len(prefix) :], "")
    raise IntakeError(f"Unknown art category in '{stem}'.")


def parse_candidate_path(path: Path, requests: Sequence[AssetRequest]) -> tuple[ParsedStem, str, int]:
    if path.suffix.lower() != ".png":
        raise IntakeError(f"Only PNG candidates are accepted: '{path.name}'.")
    if path.suffix != ".png" or not FILENAME_RE.fullmatch(path.name):
        raise IntakeError(
            f"Filename '{path.name}' must use lowercase ASCII/digits/underscores and end in .png."
        )

    stem = path.stem
    parent_prefix = path.parent.name
    possible_prefixes: list[str] = []
    if stem.startswith(parent_prefix + "_"):
        possible_prefixes.append(parent_prefix)
    possible_prefixes.extend(
        request.scan_prefix
        for request in requests
        if stem.startswith(request.scan_prefix + "_")
    )
    possible_prefixes = sorted(set(possible_prefixes), key=len, reverse=True)
    if not possible_prefixes:
        raise IntakeError(
            f"'{path.name}' must start with its asset-directory stem or a configured asset prefix."
        )

    prefix = possible_prefixes[0]
    suffix = stem[len(prefix) + 1 :]
    match = re.fullmatch(r"(?P<style>[a-z0-9_]+)_v(?P<version>\d{2})", suffix)
    if not match:
        raise IntakeError(f"'{path.name}' must end with _<style>_vNN.png.")
    style = match["style"]
    if style.startswith("_") or style.endswith("_") or "__" in style:
        raise IntakeError(f"Style '{style}' in '{path.name}' contains an empty segment.")
    return parse_asset_stem(prefix), style, int(match["version"])


def target_size_for(value: AssetRequest | ParsedStem) -> tuple[int, int]:
    if isinstance(value, AssetRequest) and value.target_size is not None:
        return value.target_size
    category = value.category
    spec = value.spec
    name = value.source_name if isinstance(value, AssetRequest) else value.name
    if category == "unit":
        match = GRID_RE.fullmatch(spec)
        if not match:
            raise IntakeError(f"Unit spec '{spec}' is not NxM.")
        return int(match["w"]) * 256, int(match["h"]) * 256
    if category == "cmd":
        if spec == "card":
            return 512, 768
        if spec == "avatar":
            return 256, 256
    if category == "bld":
        match = GRID_RE.fullmatch(spec)
        if not match:
            raise IntakeError(f"Building spec '{spec}' is not NxM.")
        return int(match["w"]) * 256, int(match["h"]) * 256
    if category == "icon":
        return 128, 128
    if category == "bg":
        if spec == "tile":
            return 512, 512
        if spec == "full":
            return 1080, 1920
        if spec == "decal":
            raise IntakeError(f"Background decal '{name}' requires targetSize in the selection manifest.")
    if category == "ui":
        if name.startswith("card_bg_"):
            return 512, 768
        if name.startswith("btn_"):
            return 256, 96
        if name.startswith("grid_cell_"):
            return 128, 128
        if name == "panel_frame":
            return 512, 512
        if name.startswith("hpbar_fill_"):
            return 256, 32
        raise IntakeError(f"UI asset '{name}' requires targetSize in the selection manifest.")
    if category == "fx":
        known = {"hit": (256, 256), "explosion": (512, 512), "muzzle": (128, 128)}
        if name in known:
            return known[name]
        raise IntakeError(f"FX asset '{name}' requires targetSize in the selection manifest.")
    raise IntakeError(f"No target-size rule for {category}/{name}/{spec}.")


def validate_ratio(width: int, height: int, target: tuple[int, int], label: str) -> None:
    target_width, target_height = target
    if width * target_height != height * target_width:
        raise IntakeError(
            f"{label}: ratio {width}:{height} does not exactly match target "
            f"{target_width}:{target_height}."
        )
    if min(width, height) < 2 * min(target_width, target_height):
        raise IntakeError(
            f"{label}: short edge {min(width, height)}px is below the required 2x target "
            f"short edge ({2 * min(target_width, target_height)}px)."
        )


def validate_precut_size(width: int, height: int, target: tuple[int, int], label: str) -> None:
    """Validate resolution without imposing a canvas ratio on an alpha cut-out.

    A pre-cut source's transparent canvas is no longer meaningful: the visible alpha
    bounds are cropped and fitted later. Retain the same 2x short-edge quality gate.
    """

    required = 2 * min(target)
    if min(width, height) < required:
        raise IntakeError(
            f"{label}: short edge {min(width, height)}px is below the required 2x target "
            f"short edge ({required}px)."
        )


def inspect_candidate(
    path: Path,
    requests: Sequence[AssetRequest],
    mapped_request: AssetRequest | None = None,
) -> Candidate:
    if mapped_request is None:
        parsed, style, version = parse_candidate_path(path, requests)
    else:
        if path.suffix.lower() != ".png":
            raise IntakeError(f"Only PNG candidates are accepted: '{path.name}'.")
        parsed = parse_asset_stem(mapped_request.expected_prefix)
        style, version = "source", 1
    try:
        with Image.open(path) as source:
            if source.format != "PNG":
                raise IntakeError(f"'{path.name}' has extension .png but content format is {source.format}.")
            if source.mode not in {"RGB", "RGBA"}:
                raise IntakeError(
                    f"'{path.name}' mode is {source.mode}; expected RGB or RGBA."
                )
            precut = False
            if source.mode == "RGBA":
                alpha = np.asarray(source.getchannel("A"), dtype=np.uint8)
                if int(alpha.max()) == 0:
                    raise IntakeError(f"'{path.name}' is a pure-transparent image.")
                precut = int(alpha.min()) != 255
            width, height = source.size
            mode = source.mode
    except IntakeError:
        raise
    except Exception as exc:
        raise IntakeError(f"Cannot decode PNG '{path.name}': {exc}") from exc
    matching = (
        [mapped_request]
        if mapped_request is not None
        else [
            request
            for request in requests
            if request.scan_prefix == path.parent.name
            or request.scan_prefix == stem_without_style(path.stem, style, version)
        ]
    )
    if len(matching) > 1:
        raise IntakeError(f"'{path.name}' matches multiple manifest asset prefixes.")
    target = target_size_for(matching[0] if matching else parsed)
    if precut:
        validate_precut_size(width, height, target, path.name)
    else:
        validate_ratio(width, height, target, path.name)
    return Candidate(
        path,
        parsed,
        style,
        version,
        width,
        height,
        mode,
        matching[0].id if matching else None,
        precut,
    )


def stem_without_style(stem: str, style: str, version: int) -> str:
    suffix = f"_{style}_v{version:02d}"
    return stem[: -len(suffix)] if stem.endswith(suffix) else stem


def scan_candidates(
    inbox: Path, requests: Sequence[AssetRequest], only_ids: set[str] | None
) -> tuple[list[Candidate], list[Issue]]:
    candidates: list[Candidate] = []
    issues: list[Issue] = []
    if not inbox.is_dir():
        raise IntakeError(f"Candidate inbox does not exist: '{inbox}'.")
    allowed_prefixes = {
        request.scan_prefix for request in requests if only_ids is None or request.id in only_ids
    }
    explicit_paths = {
        request.source_path: request
        for request in requests
        if request.source_path is not None
    }
    explicit_directories = {path.parent.resolve() for path in explicit_paths}
    for path, request in sorted(explicit_paths.items(), key=lambda item: str(item[0])):
        if only_ids is not None and request.id not in only_ids:
            continue
        if not path.is_file():
            issues.append(
                Issue(
                    "error",
                    "source_missing",
                    f"Configured sourcePath for '{request.id}' does not exist.",
                    normalize_rel(path),
                )
            )
            continue
        try:
            candidates.append(inspect_candidate(path, requests, mapped_request=request))
        except IntakeError as exc:
            issues.append(Issue("error", "candidate_validation", str(exc), normalize_rel(path)))
    for path in sorted(item for item in inbox.rglob("*") if item.is_file()):
        if path.resolve() in explicit_paths:
            continue
        if only_ids is None and path.parent.resolve() in explicit_directories:
            issues.append(
                Issue(
                    "warning",
                    "source_unmapped",
                    "File is in a sourcePath-managed directory but is not selected by the manifest; ignored.",
                    normalize_rel(path),
                )
            )
            continue
        if only_ids is not None and path.parent.name not in allowed_prefixes:
            continue
        try:
            candidate = inspect_candidate(path, requests)
            if only_ids is not None and candidate.request_id not in only_ids:
                continue
            candidates.append(candidate)
        except IntakeError as exc:
            issues.append(Issue("error", "candidate_validation", str(exc), normalize_rel(path)))
    return candidates, issues


def resolve_only(values: Sequence[str], requests: Sequence[AssetRequest]) -> set[str] | None:
    if not values:
        return None
    resolved: set[str] = set()
    for value in values:
        matches = [
            request.id
            for request in requests
            if value in {request.id, request.source_name, request.expected_prefix, request.scan_prefix}
        ]
        if not matches:
            raise IntakeError(
                f"--only '{value}' does not match an id, sourceName, or asset prefix in the manifest."
            )
        if len(matches) > 1:
            raise IntakeError(f"--only '{value}' is ambiguous; use one of: {', '.join(matches)}")
        resolved.add(matches[0])
    return resolved


def choose_candidates(
    candidates: Sequence[Candidate], config: IntakeConfig, only_ids: set[str] | None
) -> tuple[dict[str, Candidate], list[Issue]]:
    selected: dict[str, Candidate] = {}
    issues: list[Issue] = []
    by_id: dict[str, list[Candidate]] = {}
    for candidate in candidates:
        if candidate.request_id:
            by_id.setdefault(candidate.request_id, []).append(candidate)
    for request in config.assets:
        if only_ids is not None and request.id not in only_ids:
            continue
        options = sorted(by_id.get(request.id, []), key=lambda item: (item.version, item.style, item.path.name))
        selector = config.selections.get(request.id)
        if not options:
            if selector is not None:
                issues.append(
                    Issue(
                        "error",
                        "selection_missing",
                        f"Selection for '{request.id}' is '{selector}', but no valid candidates exist.",
                    )
                )
            continue
        if selector is None:
            selected[request.id] = options[-1]
            continue
        matches = [candidate for candidate in options if selector_matches(selector, candidate)]
        if len(matches) != 1:
            available = ", ".join(candidate.selector for candidate in options)
            issues.append(
                Issue(
                    "error",
                    "selection_invalid",
                    f"Selection '{selector}' for '{request.id}' matched {len(matches)} candidates; "
                    f"available: {available}.",
                )
            )
            continue
        selected[request.id] = matches[0]
    return selected, issues


def selector_matches(selector: Any, candidate: Candidate) -> bool:
    if isinstance(selector, str):
        return selector in {candidate.selector, candidate.path.name, candidate.path.stem}
    if isinstance(selector, dict):
        style = selector.get("style")
        version = selector.get("version")
        if isinstance(version, str) and version.startswith("v"):
            version = version[1:]
        try:
            version_number = int(version)
        except (TypeError, ValueError):
            return False
        return style == candidate.style and version_number == candidate.version
    return False


def flood_fill_alpha(rgb: np.ndarray, tolerance: float = 34.0) -> np.ndarray:
    image_ops = require_ndimage()
    height, width, _ = rgb.shape
    sample_radius = max(1, min(height, width) // 100)
    samples = np.concatenate(
        [
            rgb[:sample_radius, :sample_radius].reshape(-1, 3),
            rgb[:sample_radius, -sample_radius:].reshape(-1, 3),
            rgb[-sample_radius:, :sample_radius].reshape(-1, 3),
            rgb[-sample_radius:, -sample_radius:].reshape(-1, 3),
        ],
        axis=0,
    ).astype(np.float32)
    corner_color = np.median(samples, axis=0)
    distance = np.linalg.norm(rgb.astype(np.float32) - corner_color, axis=2)
    brightness = rgb.min(axis=2)
    candidate_background = (distance <= tolerance) | (brightness >= 250)
    seeds = np.zeros((height, width), dtype=bool)
    seeds[0, :] = candidate_background[0, :]
    seeds[-1, :] = candidate_background[-1, :]
    seeds[:, 0] = candidate_background[:, 0]
    seeds[:, -1] = candidate_background[:, -1]
    background = image_ops.binary_propagation(seeds, mask=candidate_background)
    alpha = np.where(background, 0, 255).astype(np.float32)
    alpha = image_ops.gaussian_filter(alpha, sigma=0.65)
    return np.clip(np.rint(alpha), 0, 255).astype(np.uint8)


def clean_alpha(alpha: np.ndarray) -> tuple[np.ndarray, int, int]:
    image_ops = require_ndimage()
    cleaned = alpha.copy()
    cleaned[cleaned <= 8] = 0
    cleaned[cleaned >= 247] = 255
    binary = cleaned >= 24
    labels, count = image_ops.label(binary)
    removed = 0
    if count:
        sizes = np.bincount(labels.ravel())
        minimum = max(12, int(binary.size * 0.00002))
        small_labels = np.flatnonzero((sizes < minimum) & (np.arange(sizes.size) != 0))
        if small_labels.size:
            small_mask = np.isin(labels, small_labels)
            cleaned[small_mask] = 0
            removed = int(small_labels.size)
            binary[small_mask] = False
    filled = image_ops.binary_fill_holes(binary)
    holes = filled & ~binary
    hole_labels, holes_filled = image_ops.label(holes)
    if holes_filled:
        cleaned[holes] = 255
    return cleaned, removed, int(holes_filled)


def alpha_score(rgb: np.ndarray, alpha: np.ndarray) -> float:
    image_ops = require_ndimage()
    foreground = alpha >= 24
    coverage = float(foreground.mean())
    border = np.concatenate((foreground[0], foreground[-1], foreground[:, 0], foreground[:, -1]))
    border_coverage = float(border.mean())
    color_distance = np.linalg.norm(255.0 - rgb.astype(np.float32), axis=2)
    likely_object = color_distance >= 45.0
    likely_background = color_distance <= 18.0
    false_negative = float((likely_object & ~foreground).mean())
    false_positive = float((likely_background & foreground).mean())
    labels, components = image_ops.label(foreground)
    component_penalty = 0.0
    if components > 1:
        sizes = np.sort(np.bincount(labels.ravel())[1:])
        component_penalty = float(sizes[:-1].sum() / foreground.size)
    coverage_penalty = max(0.0, 0.004 - coverage) * 50.0 + max(0.0, coverage - 0.92) * 10.0
    return (
        false_negative * 2.5
        + false_positive * 2.0
        + border_coverage * 6.0
        + component_penalty * 0.25
        + coverage_penalty
    )


def decontaminate(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    image_ops = require_ndimage()
    opaque = alpha >= 245
    if not opaque.any():
        raise IntakeError("Segmentation produced no opaque foreground pixels.")
    non_opaque = ~opaque
    indices = image_ops.distance_transform_edt(
        non_opaque, return_distances=False, return_indices=True
    )
    nearest = rgb[indices[0], indices[1]]
    output = rgb.copy()
    soft = (alpha > 0) & ~opaque
    output[soft] = nearest[soft]
    output[alpha == 0] = 0
    return output


def validate_and_clear_l_corner(
    rgba: np.ndarray, corner: str | None
) -> tuple[np.ndarray, float | None]:
    if corner is None:
        return rgba, None
    height, width = rgba.shape[:2]
    half_y, half_x = height // 2, width // 2
    if corner == "que_you_shang":
        ys, xs = slice(0, half_y), slice(half_x, width)
    elif corner == "que_zuo_xia":
        ys, xs = slice(half_y, height), slice(0, half_x)
    else:
        raise IntakeError(f"Unsupported L-corner '{corner}'.")
    quadrant_alpha = rgba[ys, xs, 3]
    coverage = float(np.count_nonzero(quadrant_alpha > 16) / quadrant_alpha.size)
    if coverage > L_CORNER_MAX_COVERAGE:
        raise CandidateRejected(
            f"L-corner {corner} foreground coverage is {coverage:.2%}; maximum is "
            f"{L_CORNER_MAX_COVERAGE:.0%}."
        )
    result = rgba.copy()
    result[ys, xs] = 0
    return result, coverage


def clear_l_corner(rgba: np.ndarray, corner: str | None) -> np.ndarray:
    """Clear a manifest-declared placeholder corner without treating it as source art."""
    if corner is None:
        return rgba
    height, width = rgba.shape[:2]
    half_y, half_x = height // 2, width // 2
    result = rgba.copy()
    if corner == "que_you_shang":
        result[0:half_y, half_x:width] = 0
    elif corner == "que_zuo_xia":
        result[half_y:height, 0:half_x] = 0
    else:
        raise IntakeError(f"Unsupported L-corner '{corner}'.")
    return result


def select_alpha(
    rgb: np.ndarray, rembg_alpha: np.ndarray, flood_alpha: np.ndarray
) -> tuple[np.ndarray, QualityMetrics]:
    rembg_clean, rem_removed, rem_holes = clean_alpha(rembg_alpha)
    flood_clean, flood_removed, flood_holes = clean_alpha(flood_alpha)
    difference = float(np.abs(rembg_clean.astype(np.int16) - flood_clean.astype(np.int16)).mean() / 255.0)
    rembg_score = alpha_score(rgb, rembg_clean)
    flood_score = alpha_score(rgb, flood_clean)
    if rembg_score <= flood_score:
        chosen, channel = rembg_clean, "rembg"
        removed, holes = rem_removed, rem_holes
    else:
        chosen, channel = flood_clean, "flood-fill"
        removed, holes = flood_removed, flood_holes
    foreground = chosen >= 24
    border = np.concatenate((foreground[0], foreground[-1], foreground[:, 0], foreground[:, -1]))
    metrics = QualityMetrics(
        selectedChannel=channel,
        alphaDifference=difference,
        rembgScore=rembg_score,
        floodScore=flood_score,
        foregroundCoverage=float(foreground.mean()),
        borderCoverage=float(border.mean()),
        componentsRemoved=removed,
        holesFilled=holes,
    )
    return chosen, metrics


def premultiplied_resize(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.float32)
    alpha = rgba[..., 3:4] / 255.0
    premultiplied = np.concatenate((rgba[..., :3] * alpha, rgba[..., 3:4]), axis=2)
    resized_channels = [
        np.asarray(
            Image.fromarray(np.clip(channel, 0, 255).astype(np.uint8)).resize(size, Image.Resampling.LANCZOS),
            dtype=np.float32,
        )
        for channel in np.moveaxis(premultiplied, 2, 0)
    ]
    resized = np.stack(resized_channels, axis=2)
    resized_alpha = resized[..., 3:4] / 255.0
    rgb = np.divide(
        resized[..., :3], resized_alpha, out=np.zeros_like(resized[..., :3]), where=resized_alpha > 1e-4
    )
    output = np.concatenate((rgb, resized[..., 3:4]), axis=2)
    return Image.fromarray(np.clip(np.rint(output), 0, 255).astype(np.uint8), "RGBA")


def fit_to_canvas(
    rgba: Image.Image, target: tuple[int, int], bottom_align: bool
) -> Image.Image:
    alpha = rgba.getchannel("A")
    bounds = alpha.getbbox()
    if bounds is None:
        raise IntakeError("Segmentation produced a pure-transparent result.")
    cropped = rgba.crop(bounds)
    target_width, target_height = target
    bottom_margin = int(round(target_height * BOTTOM_MARGIN_FRACTION)) if bottom_align else 0
    available_height = max(1, target_height - bottom_margin)
    scale = min(target_width / cropped.width, available_height / cropped.height)
    resized_size = (
        max(1, min(target_width, int(round(cropped.width * scale)))),
        max(1, min(available_height, int(round(cropped.height * scale)))),
    )
    resized = premultiplied_resize(cropped, resized_size)
    canvas = Image.new("RGBA", target, (0, 0, 0, 0))
    x = (target_width - resized.width) // 2
    y = target_height - bottom_margin - resized.height if bottom_align else (target_height - resized.height) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas


def add_white_rim(image: Image.Image, width: int = RIM_WIDTH) -> Image.Image:
    image_ops = require_ndimage()
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8)
    alpha = rgba[..., 3]
    subject = alpha > 8
    dilated = image_ops.binary_dilation(subject, iterations=width)
    rim = dilated & ~subject
    output = rgba.copy()
    output[rim, :3] = 255
    output[rim, 3] = 255
    return Image.fromarray(output, "RGBA")


def process_candidate(
    candidate: Candidate,
    rembg_runner: RembgRunner,
    add_rim: bool,
    request: AssetRequest | None = None,
) -> tuple[Image.Image, QualityMetrics]:
    if candidate.precut:
        with Image.open(candidate.path) as source:
            rgba_array = np.asarray(source.convert("RGBA"), dtype=np.uint8)
        alpha = rgba_array[..., 3]
        foreground = alpha >= 24
        border = np.concatenate(
            (foreground[0], foreground[-1], foreground[:, 0], foreground[:, -1])
        )
        metrics = QualityMetrics(
            selectedChannel="precut",
            alphaDifference=0.0,
            rembgScore=0.0,
            floodScore=0.0,
            foregroundCoverage=float(foreground.mean()),
            borderCoverage=float(border.mean()),
            componentsRemoved=0,
            holesFilled=0,
        )
        rgba_array, coverage = validate_and_clear_l_corner(
            rgba_array, candidate.parsed.corner
        )
        metrics.cornerCoverage = coverage
        target = target_size_for(request or candidate.parsed)
        image = fit_to_canvas(
            Image.fromarray(rgba_array, "RGBA"),
            target,
            candidate.parsed.category == "unit",
        )
        # The strict 8% gate is evaluated in source-canvas coordinates above.
        # Cropping and re-centering an asymmetric L can move valid occupied cells
        # across the output's quadrant boundary, so the normalized output enforces
        # the declared empty quadrant without falsely re-rejecting the source.
        final_array = clear_l_corner(
            np.asarray(image, dtype=np.uint8), candidate.parsed.corner
        )
        return Image.fromarray(final_array, "RGBA"), metrics

    require_ndimage()
    with Image.open(candidate.path) as source:
        rgb_image = source.convert("RGB")
    rgb = np.asarray(rgb_image, dtype=np.uint8)
    rembg_alpha = rembg_runner.alpha(rgb_image)
    if rembg_alpha.shape != rgb.shape[:2]:
        rembg_alpha = np.asarray(
            Image.fromarray(rembg_alpha).resize((candidate.width, candidate.height), Image.Resampling.BILINEAR),
            dtype=np.uint8,
        )
    flood_alpha = flood_fill_alpha(rgb)
    alpha, metrics = select_alpha(rgb, rembg_alpha, flood_alpha)
    clean_rgb = decontaminate(rgb, alpha)
    rgba_array = np.dstack((clean_rgb, alpha))
    rgba_array, coverage = validate_and_clear_l_corner(rgba_array, candidate.parsed.corner)
    metrics.cornerCoverage = coverage
    metrics.rembgProvider = rembg_runner.provider
    target = target_size_for(request or candidate.parsed)
    image = fit_to_canvas(
        Image.fromarray(rgba_array, "RGBA"), target, candidate.parsed.category == "unit"
    )
    if add_rim:
        image = add_white_rim(image)
    final_array = clear_l_corner(
        np.asarray(image, dtype=np.uint8), candidate.parsed.corner
    )
    return Image.fromarray(final_array, "RGBA"), metrics


def find_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / "msyh.ttc",
        Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / "simhei.ttf",
        Path("/System/Library/Fonts/PingFang.ttc"),
        Path("/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc"),
        Path("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),
    ]
    for path in candidates:
        if path.is_file():
            return ImageFont.truetype(str(path), size=size)
    return ImageFont.load_default()


def _placeholder_side(request: AssetRequest, visual_side: str | None) -> str:
    side = visual_side or request.side
    if side == "shared":
        return "ally"
    if side not in PLACEHOLDER_PALETTES:
        raise IntakeError(f"Unsupported placeholder side '{side}'.")
    return side


def _placeholder_geometry(request: AssetRequest) -> tuple[tuple[int, int], tuple[int, int, int, int], int]:
    size = target_size_for(request)
    width, height = size
    margin = max(4, int(min(width, height) * 0.045))
    bottom_margin = int(round(height * BOTTOM_MARGIN_FRACTION)) if request.category == "unit" else margin
    rect = (margin, margin, width - margin - 1, height - bottom_margin - 1)
    radius = max(6, int(min(width, height) * 0.10))
    return size, rect, radius


def _placeholder_text_boxes(
    request: AssetRequest, rect: tuple[int, int, int, int]
) -> list[tuple[str, tuple[int, int, int, int]]]:
    """Assign at most one glyph to each occupied grid cell.

    This makes the M1-04 footprint contract visible in the placeholder itself
    and, importantly, keeps multi-glyph labels out of neighbouring sprites. The
    third glyph in a regular 2x2 footprint uses the full lower row; L-shaped
    units use only their declared occupied quadrants.
    """

    text = request.hanzi
    if request.category != "unit" or not GRID_RE.fullmatch(request.spec) or len(text) <= 1:
        return [(text, rect)]

    match = GRID_RE.fullmatch(request.spec)
    assert match is not None
    grid_width = int(match["w"])
    grid_height = int(match["h"])
    left, top, right, bottom = rect
    width = right - left + 1
    height = bottom - top + 1

    def cell(x: int, y: int, span_x: int = 1, span_y: int = 1) -> tuple[int, int, int, int]:
        x0 = left + round(width * x / grid_width)
        y0 = top + round(height * y / grid_height)
        x1 = left + round(width * (x + span_x) / grid_width) - 1
        y1 = top + round(height * (y + span_y) / grid_height) - 1
        return x0, y0, x1, y1

    if request.corner == "que_you_shang":
        boxes = [cell(0, 0), cell(0, 1, 2)]
    elif request.corner == "que_zuo_xia":
        boxes = [cell(0, 0, 2), cell(1, 1)]
    elif grid_width == 2 and grid_height == 2 and len(text) == 3:
        boxes = [cell(0, 0), cell(1, 0), cell(0, 1, 2)]
    else:
        boxes = [cell(index % grid_width, index // grid_width) for index in range(grid_width * grid_height)]
    return list(zip(text, boxes, strict=False))


def _draw_fitted_hanzi(
    layer: Image.Image,
    text: str,
    box: tuple[int, int, int, int],
) -> None:
    draw = ImageDraw.Draw(layer)
    left, top, right, bottom = box
    box_width = right - left + 1
    box_height = bottom - top + 1
    padding = max(4, round(min(box_width, box_height) * 0.12))
    available_width = max(1, box_width - padding * 2)
    available_height = max(1, box_height - padding * 2)
    maximum = max(12, min(available_width, available_height))

    font: ImageFont.FreeTypeFont | ImageFont.ImageFont = find_font(maximum)
    stroke = max(1, maximum // 18)
    text_box = draw.textbbox((0, 0), text, font=font, stroke_width=stroke)
    for font_size in range(maximum, 11, -1):
        candidate = find_font(font_size)
        candidate_stroke = max(1, font_size // 18)
        candidate_box = draw.textbbox((0, 0), text, font=candidate, stroke_width=candidate_stroke)
        if (
            candidate_box[2] - candidate_box[0] <= available_width
            and candidate_box[3] - candidate_box[1] <= available_height
        ):
            font = candidate
            stroke = candidate_stroke
            text_box = candidate_box
            break

    text_width = text_box[2] - text_box[0]
    text_height = text_box[3] - text_box[1]
    x = left + (box_width - text_width) / 2 - text_box[0]
    y = top + (box_height - text_height) / 2 - text_box[1]
    draw.text(
        (x, y),
        text,
        font=font,
        fill=(244, 225, 174, 255),
        stroke_width=stroke,
        stroke_fill=(0, 0, 0, 255),
    )


def placeholder_image(request: AssetRequest, visual_side: str | None = None) -> Image.Image:
    size, rect, radius = _placeholder_geometry(request)
    width, height = size
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    base, accent = PLACEHOLDER_PALETTES[_placeholder_side(request, visual_side)]
    margin = rect[0]
    draw.rounded_rectangle(rect, radius=radius, fill=base, outline=(16, 16, 16, 255), width=max(3, margin // 4))
    inset = max(5, margin // 2)
    draw.rounded_rectangle(
        (rect[0] + inset, rect[1] + inset, rect[2] - inset, rect[3] - inset),
        radius=max(3, radius - inset),
        outline=accent,
        width=max(2, inset // 3),
    )
    text_layer = Image.new("RGBA", size, (0, 0, 0, 0))
    for glyph, box in _placeholder_text_boxes(request, rect):
        _draw_fitted_hanzi(text_layer, glyph, box)

    # The rectangle alpha is the hard clipping boundary. Even if a platform
    # font reports unusual CJK bearings, no glyph pixel can escape the block.
    block_alpha = np.asarray(canvas.getchannel("A"), dtype=np.uint8)
    text_array = np.asarray(text_layer, dtype=np.uint8).copy()
    text_array[..., 3] = np.minimum(text_array[..., 3], block_alpha)
    canvas.alpha_composite(Image.fromarray(text_array, "RGBA"))
    if request.corner:
        array = clear_l_corner(np.asarray(canvas, dtype=np.uint8), request.corner)
        canvas = Image.fromarray(array, "RGBA")
    return canvas


def hue_shift(image: Image.Image, degrees: float = 28.0) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8)
    hsv = np.asarray(Image.fromarray(rgba[..., :3], "RGB").convert("HSV"), dtype=np.uint8).copy()
    shift = int(round(degrees / 360.0 * 256.0))
    hsv[..., 0] = (hsv[..., 0].astype(np.uint16) + shift) % 256
    rgb = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"), dtype=np.uint8)
    return Image.fromarray(np.dstack((rgb, rgba[..., 3])), "RGBA")


def commander_avatar(card: Image.Image) -> Image.Image:
    side = min(card.width, card.height)
    left = (card.width - side) // 2
    crop = card.crop((left, 0, left + side, side))
    return premultiplied_resize(crop, (256, 256))


def output_paths(request: AssetRequest) -> list[tuple[Path, str]]:
    root = DEFAULT_ART_ROOT
    if request.category == "unit":
        result = [(root / "Units" / request.id / "idle.png", "primary")]
        if request.side == "shared":
            result.append((root / "Units" / f"{request.id}_enemy" / "idle.png", "enemy"))
        return result
    if request.category == "cmd":
        return [
            (root / "Commanders" / request.id / "card.png", "primary"),
            (root / "Commanders" / request.id / "avatar.png", "avatar"),
        ]
    if request.category == "bld":
        return [(root / "Buildings" / f"{request.id}.png", "primary")]
    if request.category == "icon":
        return [(root / "Icons" / f"{request.id}.png", "primary")]
    if request.category == "bg":
        index = f"_{request.index}" if request.index else ""
        return [
            (root / "Background" / f"{request.source_name}_{request.spec}{index}.png", "primary")
        ]
    if request.category == "ui":
        return [(root / "UI" / f"{request.source_name}.png", "primary")]
    if request.category == "fx":
        return [(root / "FX" / f"{request.source_name}.png", "primary")]
    raise IntakeError(f"No output route for category '{request.category}'.")


def write_png(path: Path, image: Image.Image, force: bool, dry_run: bool) -> str:
    if dry_run:
        return "dry-run"
    if path.exists() and not force:
        return "kept-existing"
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, suffix=".png", delete=False) as handle:
        temporary = Path(handle.name)
    try:
        image.save(temporary, format="PNG", optimize=True)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
    return "written"


def write_outputs(
    request: AssetRequest,
    image: Image.Image,
    force: bool,
    dry_run: bool,
    placeholder: bool = False,
) -> tuple[list[str], list[str]]:
    paths: list[str] = []
    actions: list[str] = []
    for path, variant in output_paths(request):
        variant_image = image
        if variant == "enemy":
            variant_image = (
                placeholder_image(request, visual_side="enemy")
                if placeholder
                else hue_shift(image)
            )
        elif variant == "avatar":
            variant_image = commander_avatar(image)
        action = write_png(path, variant_image, force, dry_run)
        paths.append(normalize_rel(path))
        actions.append(action)
    return paths, actions


def make_thumbnail(image: Image.Image, size: int = 64) -> Image.Image:
    checker = Image.new("RGB", (size, size), (224, 224, 224))
    draw = ImageDraw.Draw(checker)
    tile = 8
    for y in range(0, size, tile):
        for x in range(0, size, tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(244, 244, 244))
    preview = image.copy()
    preview.thumbnail((size, size), Image.Resampling.LANCZOS)
    x = (size - preview.width) // 2
    y = (size - preview.height) // 2
    checker.paste(preview, (x, y), preview)
    return checker


def render_contact_sheet(records: Sequence[ReviewRecord], output: Path) -> None:
    columns = 5
    cell_width, cell_height = 190, 126
    rows = max(1, math.ceil(len(records) / columns))
    sheet = Image.new("RGB", (columns * cell_width, rows * cell_height + 42), (34, 36, 40))
    draw = ImageDraw.Draw(sheet)
    title_font = find_font(18)
    body_font = find_font(13)
    draw.text((14, 10), f"HanziDefend art intake · {date.today().isoformat()}", font=title_font, fill="white")
    status_colors = {
        "ok": (54, 112, 71),
        "warning": (161, 106, 28),
        "error": (164, 44, 44),
        "placeholder": (63, 82, 125),
        "unselected": (72, 72, 76),
    }
    for index, record in enumerate(records):
        column, row = index % columns, index // columns
        x, y = column * cell_width, 42 + row * cell_height
        background = status_colors.get(record.status, (72, 72, 76))
        draw.rectangle((x + 2, y + 2, x + cell_width - 3, y + cell_height - 3), fill=background)
        if record.preview is not None:
            thumb = make_thumbnail(record.preview)
            sheet.paste(thumb, (x + 8, y + 29))
        label = record.id[:25]
        draw.text((x + 8, y + 7), label, font=body_font, fill="white")
        dimension = record.originalSize or "placeholder"
        draw.text((x + 79, y + 34), dimension, font=body_font, fill="white")
        selector = (record.selector or record.status)[:18]
        draw.text((x + 79, y + 55), selector, font=body_font, fill="white")
        channel = record.metrics.selectedChannel if record.metrics else "generated"
        draw.text((x + 79, y + 76), channel[:18], font=body_font, fill="white")
        if record.metrics:
            draw.text(
                (x + 79, y + 97),
                f"Δα {record.metrics.alphaDifference:.1%}",
                font=body_font,
                fill="white",
            )
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, format="PNG", optimize=True)


def write_review(
    records: Sequence[ReviewRecord], issues: Sequence[Issue], review_dir: Path, dry_run: bool
) -> tuple[Path | None, Path | None]:
    if dry_run:
        return None, None
    stamp = date.today().isoformat()
    json_path = review_dir / f"{stamp}.json"
    sheet_path = review_dir / f"{stamp}.png"
    report = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "summary": {
            "records": len(records),
            "errors": sum(issue.severity == "error" for issue in issues),
            "warnings": sum(issue.severity == "warning" for issue in issues),
            "placeholders": sum(record.placeholder for record in records),
        },
        "issues": [asdict(issue) for issue in issues],
        "records": [record.serializable() for record in records],
    }
    review_dir.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    render_contact_sheet(records, sheet_path)
    return json_path, sheet_path


def run(args: argparse.Namespace) -> int:
    config = load_config(args.manifest.resolve())
    only_ids = resolve_only(args.only, config.assets)
    requests = [request for request in config.assets if only_ids is None or request.id in only_ids]
    if args.placeholders_only:
        candidates: list[Candidate] = []
        issues: list[Issue] = []
        selected: dict[str, Candidate] = {}
    else:
        candidates, issues = scan_candidates(args.inbox.resolve(), config.assets, only_ids)
        selected, selection_issues = choose_candidates(candidates, config, only_ids)
        issues.extend(selection_issues)
    runner = RembgRunner()
    records: list[ReviewRecord] = []
    processed: dict[Path, tuple[Image.Image, QualityMetrics]] = {}
    request_by_id = {request.id: request for request in requests}

    for candidate_index, candidate in enumerate(candidates, start=1):
        is_selected = selected.get(candidate.request_id or "") == candidate
        started_at = time.perf_counter()
        print(
            f"[{candidate_index}/{len(candidates)}] processing {normalize_rel(candidate.path)}",
            flush=True,
        )
        try:
            image, metrics = process_candidate(
                candidate,
                runner,
                add_rim=not args.no_rim,
                request=request_by_id.get(candidate.request_id or ""),
            )
            processed[candidate.path] = (image, metrics)
            messages: list[str] = []
            status = "ok" if is_selected else "unselected"
            if metrics.alphaDifference > ALPHA_DIFFERENCE_WARNING:
                message = (
                    f"rembg/flood-fill alpha difference {metrics.alphaDifference:.2%} exceeds "
                    f"{ALPHA_DIFFERENCE_WARNING:.0%}."
                )
                messages.append(message)
                issues.append(Issue("warning", "alpha_difference", message, normalize_rel(candidate.path)))
                status = "warning"
            if candidate.request_id is None:
                message = "Valid but unmapped/deprecated candidate; it will not be imported."
                messages.append(message)
                issues.append(Issue("warning", "unmapped_candidate", message, normalize_rel(candidate.path)))
                status = "warning"
            records.append(
                ReviewRecord(
                    id=candidate.request_id or candidate.parsed.name,
                    source=normalize_rel(candidate.path),
                    selector=candidate.selector,
                    selected=is_selected,
                    status=status,
                    originalSize=f"{candidate.width}×{candidate.height} {candidate.mode}",
                    output=None,
                    metrics=metrics,
                    messages=messages,
                    preview=image,
                )
            )
            print(
                f"[{candidate_index}/{len(candidates)}] done in "
                f"{time.perf_counter() - started_at:.1f}s ({metrics.selectedChannel})",
                flush=True,
            )
        except IntakeError as exc:
            message = str(exc)
            rejected = isinstance(exc, CandidateRejected)
            severity = "warning" if rejected else "error"
            code = "quality_rejected" if rejected else "processing_failed"
            issues.append(Issue(severity, code, message, normalize_rel(candidate.path)))
            records.append(
                ReviewRecord(
                    id=candidate.request_id or candidate.parsed.name,
                    source=normalize_rel(candidate.path),
                    selector=candidate.selector,
                    selected=is_selected,
                    status="warning" if rejected else "error",
                    originalSize=f"{candidate.width}×{candidate.height} {candidate.mode}",
                    output=None,
                    messages=[message],
                )
            )
            print(
                f"[{candidate_index}/{len(candidates)}] failed in "
                f"{time.perf_counter() - started_at:.1f}s: {message}",
                flush=True,
            )

    for request in requests:
        candidate = selected.get(request.id)
        image: Image.Image
        placeholder = False
        status = "ok"
        messages: list[str] = []
        metrics: QualityMetrics | None = None
        if candidate is not None and candidate.path in processed:
            image, metrics = processed[candidate.path]
        else:
            image = placeholder_image(request)
            placeholder = True
            status = "placeholder"
            if candidate is not None:
                messages.append("Selected candidate failed quality checks; placeholder emitted.")
            else:
                messages.append("No selected candidate; placeholder emitted.")
        paths, actions = write_outputs(
            request, image, args.force, args.dry_run, placeholder=placeholder
        )
        output_text = ", ".join(paths)
        messages.append("output actions: " + ", ".join(actions))
        if candidate is not None:
            candidate_source = normalize_rel(candidate.path)
            for record in records:
                if record.source == candidate_source and record.selected:
                    record.output = output_text
                    if not placeholder:
                        record.messages.extend(messages)
                    break
        if placeholder:
            records.append(
                ReviewRecord(
                    id=request.id,
                    source=None,
                    selector=None,
                    selected=True,
                    status=status,
                    originalSize=None,
                    output=output_text,
                    placeholder=True,
                    messages=messages,
                    preview=image,
                )
            )

    report_path, sheet_path = write_review(records, issues, args.review.resolve(), args.dry_run)
    summary = {
        "candidateFiles": len(candidates)
        + sum(
            issue.code in {"candidate_validation", "source_missing", "source_unmapped"}
            for issue in issues
        ),
        "validCandidates": len(candidates),
        "selectedAssets": len(selected),
        "requestedAssets": len(requests),
        "placeholders": sum(record.placeholder for record in records),
        "warnings": sum(issue.severity == "warning" for issue in issues),
        "errors": sum(issue.severity == "error" for issue in issues),
        "reviewReport": normalize_rel(report_path) if report_path else None,
        "contactSheet": normalize_rel(sheet_path) if sheet_path else None,
        "dryRun": args.dry_run,
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    for issue in issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        location = f" [{issue.path}]" if issue.path else ""
        print(f"{issue.severity.upper()} {issue.code}{location}: {issue.message}", file=stream)
    return 1 if summary["errors"] else 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Turn white-background HanziDefend candidates into Unity-ready transparent sprites."
    )
    parser.add_argument(
        "--only",
        action="append",
        default=[],
        metavar="ASSET",
        help="Process one manifest id/sourceName/prefix; may be repeated.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Run all validation and inference without writing files.")
    parser.add_argument("--force", action="store_true", help="Overwrite existing art outputs.")
    parser.add_argument(
        "--placeholders-only",
        action="store_true",
        help="Ignore candidate art and regenerate every requested asset as a faction placeholder.",
    )
    parser.add_argument("--no-rim", action="store_true", help="Disable the 7px procedural white rim.")
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST, help=argparse.SUPPRESS)
    parser.add_argument("--inbox", type=Path, default=DEFAULT_INBOX, help=argparse.SUPPRESS)
    parser.add_argument("--review", type=Path, default=DEFAULT_REVIEW, help=argparse.SUPPRESS)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return run(args)
    except IntakeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("ERROR: interrupted.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
