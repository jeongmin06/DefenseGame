#!/usr/bin/env python3
"""Validate committed Godot assets without requiring the Godot editor."""

from __future__ import annotations

import argparse
import binascii
import re
import struct
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
REFERENCE_PATTERN = re.compile(r"[\"'](res://[^\"']+)[\"']")
TEXT_RESOURCE_SUFFIXES = {".tscn", ".tres", ".import"}
IGNORED_DIRECTORIES = {".git", ".godot", "bin", "build", "export", "obj"}


@dataclass(frozen=True)
class ValidationError:
    path: Path
    message: str


def iter_project_files(project_root: Path):
    for path in sorted(project_root.rglob("*")):
        if not path.is_file():
            continue
        if any(part in IGNORED_DIRECTORIES for part in path.relative_to(project_root).parts[:-1]):
            continue
        yield path


def validate_png(path: Path) -> list[ValidationError]:
    errors: list[ValidationError] = []

    try:
        payload = path.read_bytes()
    except OSError as exc:
        return [ValidationError(path, f"파일을 읽을 수 없습니다: {exc}")]

    if not payload.startswith(PNG_SIGNATURE):
        return [ValidationError(path, "PNG 시그니처가 올바르지 않습니다")]

    offset = len(PNG_SIGNATURE)
    chunk_index = 0
    saw_ihdr = False
    saw_idat = False
    saw_iend = False
    idat_payloads: list[bytes] = []

    while offset < len(payload):
        if len(payload) - offset < 12:
            errors.append(ValidationError(path, "PNG 청크 헤더가 잘렸습니다"))
            break

        length = struct.unpack(">I", payload[offset : offset + 4])[0]
        chunk_type = payload[offset + 4 : offset + 8]
        chunk_end = offset + 12 + length
        if chunk_end > len(payload):
            errors.append(ValidationError(path, "PNG 청크 데이터가 잘렸습니다"))
            break

        chunk_data = payload[offset + 8 : offset + 8 + length]
        expected_crc = struct.unpack(">I", payload[offset + 8 + length : chunk_end])[0]
        actual_crc = binascii.crc32(chunk_type + chunk_data) & 0xFFFFFFFF
        chunk_name = chunk_type.decode("ascii", errors="replace")
        if expected_crc != actual_crc:
            errors.append(ValidationError(path, f"{chunk_name} 청크 CRC가 올바르지 않습니다"))

        if chunk_index == 0 and chunk_type != b"IHDR":
            errors.append(ValidationError(path, "첫 PNG 청크가 IHDR이 아닙니다"))

        if chunk_type == b"IHDR":
            if saw_ihdr:
                errors.append(ValidationError(path, "IHDR 청크가 중복되었습니다"))
            saw_ihdr = True
            if length != 13:
                errors.append(ValidationError(path, "IHDR 청크 길이가 13바이트가 아닙니다"))
            else:
                width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(
                    ">IIBBBBB", chunk_data
                )
                if width == 0 or height == 0:
                    errors.append(ValidationError(path, "PNG 너비와 높이는 0보다 커야 합니다"))
                valid_depths = {
                    0: {1, 2, 4, 8, 16},
                    2: {8, 16},
                    3: {1, 2, 4, 8},
                    4: {8, 16},
                    6: {8, 16},
                }
                if color_type not in valid_depths or bit_depth not in valid_depths[color_type]:
                    errors.append(ValidationError(path, "PNG 색상 유형과 비트 깊이 조합이 올바르지 않습니다"))
                if compression != 0 or filtering != 0 or interlace not in {0, 1}:
                    errors.append(ValidationError(path, "지원되지 않는 PNG IHDR 설정입니다"))
        elif chunk_type == b"IDAT":
            saw_idat = True
            idat_payloads.append(chunk_data)
        elif chunk_type == b"IEND":
            saw_iend = True
            if length != 0:
                errors.append(ValidationError(path, "IEND 청크는 비어 있어야 합니다"))
            if chunk_end != len(payload):
                errors.append(ValidationError(path, "IEND 뒤에 불필요한 데이터가 있습니다"))
            break

        chunk_index += 1
        offset = chunk_end

    if not saw_ihdr:
        errors.append(ValidationError(path, "IHDR 청크가 없습니다"))
    if not saw_idat:
        errors.append(ValidationError(path, "IDAT 청크가 없습니다"))
    else:
        try:
            zlib.decompress(b"".join(idat_payloads))
        except zlib.error as exc:
            errors.append(ValidationError(path, f"IDAT 압축 데이터를 읽을 수 없습니다: {exc}"))
    if not saw_iend:
        errors.append(ValidationError(path, "IEND 청크가 없습니다"))

    return errors


def resolve_resource_path(project_root: Path, reference: str) -> tuple[Path | None, str | None]:
    relative_text = reference.removeprefix("res://").split("::", maxsplit=1)[0]
    relative_path = PurePosixPath(relative_text)
    if not relative_text or relative_path.is_absolute() or ".." in relative_path.parts:
        return None, "프로젝트 밖을 가리키거나 비어 있는 경로입니다"
    return project_root.joinpath(*relative_path.parts), None


def validate_resource_references(path: Path, project_root: Path) -> tuple[list[ValidationError], int]:
    try:
        content = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as exc:
        return [ValidationError(path, f"UTF-8 텍스트로 읽을 수 없습니다: {exc}")], 0

    errors: list[ValidationError] = []
    reference_count = 0
    for match in REFERENCE_PATTERN.finditer(content):
        reference = match.group(1)
        reference_count += 1

        target, path_error = resolve_resource_path(project_root, reference)
        if path_error is not None:
            errors.append(ValidationError(path, f"{reference}: {path_error}"))
            continue

        # .import cache artifacts are generated by Godot and intentionally not committed.
        if path.suffix == ".import" and reference.startswith("res://.godot/imported/"):
            continue

        if target is not None and not target.is_file():
            errors.append(ValidationError(path, f"참조 대상이 없습니다: {reference}"))

    return errors, reference_count


def validate_project(project_root: Path) -> tuple[list[ValidationError], int, int]:
    errors: list[ValidationError] = []
    png_count = 0
    reference_count = 0

    for path in iter_project_files(project_root):
        if path.suffix.lower() == ".png":
            png_count += 1
            errors.extend(validate_png(path))
        if path.suffix in TEXT_RESOURCE_SUFFIXES:
            reference_errors, file_reference_count = validate_resource_references(path, project_root)
            errors.extend(reference_errors)
            reference_count += file_reference_count

    return errors, png_count, reference_count


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Godot PNG와 res:// 참조를 정적으로 검증합니다.")
    parser.add_argument(
        "--project-root",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "godot-client",
        help="project.godot이 있는 Godot 프로젝트 루트",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project_root = args.project_root.resolve()
    if not (project_root / "project.godot").is_file():
        print(f"오류: Godot 프로젝트를 찾을 수 없습니다: {project_root}", file=sys.stderr)
        return 2

    errors, png_count, reference_count = validate_project(project_root)
    if errors:
        print(f"에셋 정적 검사 실패 ({len(errors)}건):", file=sys.stderr)
        for error in errors:
            relative_path = error.path.relative_to(project_root)
            print(f"- {relative_path}: {error.message}", file=sys.stderr)
        return 1

    print(f"에셋 정적 검사 통과: PNG {png_count}개, res:// 참조 {reference_count}개")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
