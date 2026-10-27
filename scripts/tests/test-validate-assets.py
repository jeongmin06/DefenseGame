#!/usr/bin/env python3
"""Fixture-based contract tests for validate-assets.py."""

from __future__ import annotations

import binascii
import struct
import subprocess
import tempfile
import unittest
import zlib
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[2]
VALIDATOR = ROOT_DIR / "scripts" / "validate-assets.py"


def png_chunk(chunk_type: bytes, data: bytes) -> bytes:
    crc = binascii.crc32(chunk_type + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", crc)


def valid_png() -> bytes:
    scanline = b"\x00\xff\x80\x40\xff"
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 6, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(scanline))
        + png_chunk(b"IEND", b"")
    )


class AssetValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.project_root = Path(self.temp_dir.name) / "project"
        (self.project_root / "assets").mkdir(parents=True)
        (self.project_root / "scenes").mkdir()
        (self.project_root / "project.godot").write_text("[application]\n", encoding="utf-8")
        (self.project_root / "assets" / "valid.png").write_bytes(valid_png())
        (self.project_root / "assets" / "valid.png.import").write_text(
            '[remap]\npath="res://.godot/imported/valid.png-deadbeef.ctex"\n'
            '[deps]\nsource_file="res://assets/valid.png"\n'
            'dest_files=["res://.godot/imported/valid.png-deadbeef.ctex"]\n',
            encoding="utf-8",
        )
        (self.project_root / "scenes" / "main.tscn").write_text(
            '[gd_scene load_steps=2 format=3]\n'
            '[ext_resource type="Texture2D" path="res://assets/valid.png" id="1"]\n',
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def run_validator(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["python3", str(VALIDATOR), "--project-root", str(self.project_root)],
            check=False,
            capture_output=True,
            text=True,
        )

    def test_valid_fixture_accepts_generated_import_cache_reference(self) -> None:
        result = self.run_validator()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("PNG 1개", result.stdout)

    def test_rejects_corrupt_png_crc(self) -> None:
        corrupt = bytearray(valid_png())
        corrupt[-5] ^= 0x01
        (self.project_root / "assets" / "valid.png").write_bytes(corrupt)

        result = self.run_validator()

        self.assertEqual(result.returncode, 1)
        self.assertIn("CRC", result.stderr)

    def test_rejects_unreadable_png_image_data_with_valid_crc(self) -> None:
        payload = (
            b"\x89PNG\r\n\x1a\n"
            + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 6, 0, 0, 0))
            + png_chunk(b"IDAT", b"not-zlib-data")
            + png_chunk(b"IEND", b"")
        )
        (self.project_root / "assets" / "valid.png").write_bytes(payload)

        result = self.run_validator()

        self.assertEqual(result.returncode, 1)
        self.assertIn("IDAT 압축 데이터", result.stderr)

    def test_rejects_missing_scene_reference(self) -> None:
        (self.project_root / "scenes" / "main.tscn").write_text(
            '[gd_scene load_steps=2 format=3]\n'
            '[ext_resource type="Texture2D" path="res://assets/missing.png" id="1"]\n',
            encoding="utf-8",
        )

        result = self.run_validator()

        self.assertEqual(result.returncode, 1)
        self.assertIn("res://assets/missing.png", result.stderr)

    def test_rejects_missing_import_source(self) -> None:
        (self.project_root / "assets" / "valid.png").unlink()

        result = self.run_validator()

        self.assertEqual(result.returncode, 1)
        self.assertIn("source_file", (self.project_root / "assets" / "valid.png.import").read_text())
        self.assertIn("res://assets/valid.png", result.stderr)

    def test_rejects_generated_cache_reference_outside_import_metadata(self) -> None:
        (self.project_root / "scenes" / "main.tscn").write_text(
            '[gd_scene load_steps=2 format=3]\n'
            '[ext_resource type="Texture2D" '
            'path="res://.godot/imported/valid.png-deadbeef.ctex" id="1"]\n',
            encoding="utf-8",
        )

        result = self.run_validator()

        self.assertEqual(result.returncode, 1)
        self.assertIn("res://.godot/imported/valid.png-deadbeef.ctex", result.stderr)

    def test_rejects_path_traversal_disguised_as_generated_cache(self) -> None:
        (self.project_root / "assets" / "valid.png.import").write_text(
            '[remap]\npath="res://.godot/imported/../../outside.ctex"\n'
            '[deps]\nsource_file="res://assets/valid.png"\n',
            encoding="utf-8",
        )

        result = self.run_validator()

        self.assertEqual(result.returncode, 1)
        self.assertIn("프로젝트 밖", result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
