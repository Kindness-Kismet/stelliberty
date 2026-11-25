import hashlib
import io
import lzma
import re
import zipfile
import zlib


MAX_BLOB_BYTES = 32 * 1024 * 1024
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024
URL = re.compile(r"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s\"'<>]+")
PATH = re.compile(
    r"(?P<file_uri>\bfile://[^\r\n\0\"'<>|]+)"
    r"|(?P<windows_absolute>(?<!\w)[A-Za-z]:[\\/]+[^\r\n\0\"'<>|]*)"
    r"|(?P<network_path>(?<![\w/\\])(?:\\{2,}|/{2})[\w.-]+[\\/]+[^\r\n\0\"'<>|]+)"
    r"|(?P<unix_absolute>(?<![\w/\\:])/(?!/)[\w.-]+(?:/[^\s\0\"'<>`|,;)}\]]+)+)"
)


def safe_location(value, replacement):
    if PATH.search(value) or any(ord(char) < 32 for char in value):
        return replacement
    return value


def text_matches(text, member="", binary=False):
    hits = []
    for number, line in enumerate(text.splitlines(), 1):
        visible = URL.sub(lambda match: match.group() if match.group().lower().startswith("file://") else " " * len(match.group()), line)
        for match in PATH.finditer(visible):
            # 指纹仅用于内存去重与父版本比较。
            fingerprint = hashlib.sha256(match.group().rstrip().encode("utf-8")).hexdigest()
            hit = {"kind": match.lastgroup, "line": None if binary else number, "fingerprint": fingerprint}
            if member:
                hit["member"] = member
            hits.append(hit)
    return hits


def decode_text(raw):
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        encoding = "utf-16"
    elif raw.startswith(b"\xef\xbb\xbf"):
        encoding = "utf-8-sig"
    elif len(raw) >= 8 and len(raw) % 2 == 0 and raw[1::2].count(0) > len(raw) // 4:
        encoding = "utf-16-le"
    elif len(raw) >= 8 and len(raw) % 2 == 0 and raw[0::2].count(0) > len(raw) // 4:
        encoding = "utf-16-be"
    else:
        encoding = "utf-8"
    try:
        text = raw.decode(encoding)
    except UnicodeError:
        return raw.decode("utf-8", "replace"), True
    binary = any(ord(char) < 32 and char not in "\t\r\n\f" for char in text)
    return text, binary


def scan_text(raw, member=""):
    text, binary = decode_text(raw)
    hits = text_matches(text, member, binary)
    if binary:
        for encoding in ("utf-16-le", "utf-16-be"):
            hits.extend(text_matches(raw.decode(encoding, "ignore"), member, True))
    return hits, binary


def scan_bytes(raw):
    result = {"hits": [], "gaps": []}
    stream = io.BytesIO(raw)
    if not zipfile.is_zipfile(stream):
        result["hits"], binary = scan_text(raw)
        if raw.startswith((b"PK\x03\x04", b"PK\x05\x06", b"PK\x07\x08")):
            result["gaps"].append({"reason": "invalid_zip"})
        elif binary:
            result["gaps"].append({"reason": "binary_requires_review"})
        return result

    try:
        opened_archive = zipfile.ZipFile(stream)
    except (zipfile.BadZipFile, OSError, UnicodeError):
        result["gaps"].append({"reason": "invalid_zip"})
        return result
    total = 0
    with opened_archive as archive:
        hits, _ = scan_text(archive.comment, "<archive-comment>")
        result["hits"].extend(hits)
        for index, entry in enumerate(archive.infolist(), 1):
            member = safe_location(entry.filename, f"<redacted-member-{index}>")
            result["hits"].extend(text_matches(entry.filename, member))
            hits, _ = scan_text(entry.comment + entry.extra, member)
            result["hits"].extend(hits)
            if entry.is_dir():
                continue
            total += entry.file_size
            if entry.file_size > MAX_BLOB_BYTES or total > MAX_ARCHIVE_BYTES:
                result["gaps"].append({"member": member, "reason": "archive_size_limit"})
                continue
            if entry.flag_bits & 1:
                result["gaps"].append({"member": member, "reason": "encrypted_zip_member"})
                continue
            try:
                content = archive.read(entry)
            except (zipfile.BadZipFile, NotImplementedError, RuntimeError, OSError, EOFError, zlib.error, lzma.LZMAError):
                result["gaps"].append({"member": member, "reason": "unreadable_zip_member"})
                continue
            hits, binary = scan_text(content, member)
            result["hits"].extend(hits)
            if zipfile.is_zipfile(io.BytesIO(content)):
                result["gaps"].append({"member": member, "reason": "nested_archive_requires_review"})
            elif content.startswith((b"PK\x03\x04", b"PK\x05\x06", b"PK\x07\x08")):
                result["gaps"].append({"member": member, "reason": "invalid_zip_member"})
            elif binary:
                result["gaps"].append({"member": member, "reason": "binary_member_requires_review"})
    return result
