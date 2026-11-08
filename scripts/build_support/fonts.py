import hashlib
import json
import re
import struct
import tempfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path

from build_support.paths import ROOT

FONT_DIR = ROOT / "src" / "Stelliberty.Desktop" / "Assets" / "fonts"
GOOGLE_FONT_CSS = "https://fonts.googleapis.com/css2?family=Google+Sans"
GOOGLE_SANS = "GoogleSans-Regular.ttf"
NOTO_SANS_SC = "NotoSansSC-VF.ttf"
NOTO_SANS_SC_STATIC = "NotoSansSC-Thin.ttf"
STATIC_FONT_FORMAT_VERSION = 1


@dataclass(frozen=True)
class FontAsset:
    name: str
    url: str
    min_bytes: int


FONT_ASSETS = [
    FontAsset(GOOGLE_SANS, GOOGLE_FONT_CSS, 32 * 1024),
    FontAsset(
        NOTO_SANS_SC,
        "https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/Variable/TTF/Subset/NotoSansSC-VF.ttf",
        1024 * 1024,
    ),
]


def ensure_app_fonts() -> list[Path]:
    paths: list[Path] = []
    FONT_DIR.mkdir(parents=True, exist_ok=True)

    for asset in FONT_ASSETS:
        path = FONT_DIR / asset.name
        if not is_valid_font(path, asset.min_bytes):
            payload = read_font_bytes(asset)
            if len(payload) < asset.min_bytes:
                raise RuntimeError(f"Downloaded font asset is unexpectedly small: {asset.name}")
            path.write_bytes(payload)
            if not is_valid_font(path, asset.min_bytes):
                raise RuntimeError(f"Downloaded font asset is invalid: {asset.name}")
        paths.append(ensure_static_font(path) if asset.name == NOTO_SANS_SC else path)

    return paths


def is_valid_font(path: Path, min_bytes: int) -> bool:
    if not path.is_file() or path.stat().st_size < min_bytes:
        return False

    payload = path.read_bytes()
    if payload[:4] not in (b"\x00\x01\x00\x00", b"OTTO"):
        return False
    table_count = struct.unpack_from(">H", payload, 4)[0]
    if table_count == 0 or 12 + 16 * table_count > len(payload):
        return False
    for index in range(table_count):
        tag, checksum, offset, length = struct.unpack_from(">4sIII", payload, 12 + 16 * index)
        if offset + length > len(payload):
            return False
        table = payload[offset:offset + length]
        if tag == b"head":
            table = table[:8] + b"\0" * 4 + table[12:]
        table += b"\0" * (-len(table) % 4)
        if sum(value[0] for value in struct.iter_unpack(">I", table)) & 0xFFFFFFFF != checksum:
            return False
    return True


def ensure_static_font(source: Path) -> Path:
    target = source.with_name(NOTO_SANS_SC_STATIC)
    manifest_path = target.with_suffix(".cache")
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    if is_valid_font(target, 1024 * 1024) and manifest_path.is_file():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            manifest = None
        if isinstance(manifest, dict) and manifest.get("format_version") == STATIC_FONT_FORMAT_VERSION \
                and manifest.get("source_sha256") == source_hash \
                and manifest.get("output_sha256") == hashlib.sha256(target.read_bytes()).hexdigest():
            return target

    try:
        import fontTools
        from fontTools.ttLib import TTFont
        from fontTools.varLib.instancer import OverlapMode, instantiateVariableFont
    except ImportError as exception:
        raise RuntimeError("Static font generation requires fontTools: python -m pip install -r scripts/requirements-build.txt") from exception

    with TTFont(source, recalcTimestamp=False) as font:
        if "fvar" not in font:
            raise RuntimeError(f"Expected a variable font source: {source}")
        axes = {axis.axisTag: axis.defaultValue for axis in font["fvar"].axes}
        # Avalonia 12.1.4 使用默认轴实例，保留现有轮廓和模拟字重。
        instantiateVariableFont(font, axes, inplace=True, overlap=OverlapMode.KEEP_AND_DONT_SET_FLAGS)
        with tempfile.NamedTemporaryFile(dir=target.parent, prefix=target.name + ".", suffix=".tmp", delete=False) as output:
            font.save(output)
            temporary_path = Path(output.name)

    if not is_valid_font(temporary_path, 1024 * 1024):
        raise RuntimeError(f"Generated font is incomplete: {temporary_path}")
    output_hash = hashlib.sha256(temporary_path.read_bytes()).hexdigest()
    temporary_path.replace(target)
    manifest = {"format_version": STATIC_FONT_FORMAT_VERSION, "source_sha256": source_hash,
                "output_sha256": output_hash, "axes": axes, "fonttools_version": fontTools.__version__}
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=target.parent,
                                     prefix=manifest_path.name + ".", suffix=".tmp", delete=False) as output:
        json.dump(manifest, output, indent=2)
        temporary_path = Path(output.name)
    temporary_path.replace(manifest_path)
    return target


def read_font_bytes(asset: FontAsset) -> bytes:
    if asset.name == GOOGLE_SANS:
        return read_bytes(read_google_font_url(asset.url))

    return read_bytes(asset.url)


def read_google_font_url(css_url: str) -> str:
    css = read_bytes(css_url).decode("utf-8")
    match = re.search(r"url\((https://fonts\.gstatic\.com/[^)]+\.ttf)\)", css)
    if match is None:
        raise RuntimeError("Unable to resolve the Google Sans font URL")

    return match.group(1)


def read_bytes(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "app-build"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()
