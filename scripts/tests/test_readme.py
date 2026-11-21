"""Tests for README.md links, anchors, screenshots and required release sections."""

from __future__ import annotations

import os
import re
import struct
import unittest
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
README = ROOT / "README.md"


def github_slug(heading: str) -> str:
    """Compute the GitHub-flavored Markdown heading slug."""
    text = heading.strip().lower()
    text = re.sub(r"[^\w\s-]", "", text)
    text = re.sub(r"\s+", "-", text)
    return text


def collect_headings(markdown: str) -> set[str]:
    slugs: set[str] = set()
    in_fence = False
    for line in markdown.splitlines():
        if re.match(r"^\s*(`{3,}|~{3,})", line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        m = re.match(r"^#{1,6}\s+(.+?)\s*$", line)
        if m:
            slugs.add(github_slug(m.group(1)))
    return slugs


def _exists_case_sensitive(raw_relative: str, base_dir: Path) -> bool:
    """Verify each path component inside base_dir matches its exact on-disk casing."""
    normalized = os.path.normpath(raw_relative.replace("/", os.sep))
    candidate = Path(normalized)
    if candidate.is_absolute():
        return False
    cur = base_dir.resolve()
    for part in candidate.parts:
        if part == ".":
            continue
        if part == "..":
            cur = cur.parent
            continue
        try:
            entries = os.listdir(cur)
        except OSError:
            return False
        if part not in entries:
            return False
        cur = cur / part
    return cur.exists()


def check_readme_links(markdown: str, base_dir: Path) -> list[str]:
    """Return a list of broken relative file/image paths or unresolved anchors."""
    errors: list[str] = []
    headings = collect_headings(markdown)
    heading_cache: dict[Path, set[str]] = {}

    # Nested linked badges: [![alt](img_target)](link_target)
    badge_matches = re.findall(r"\[!\[[^\]]*\]\(([^)\s]+)\)\]\(([^)\s]+)\)", markdown)
    badge_targets = [t for pair in badge_matches for t in pair]
    # Standard Markdown links/images (with optional title and angle-bracketed destinations with spaces)
    md_targets = [
        a or b
        for a, b in re.findall(
            r'!?\[[^\]]*\]\(\s*(?:<([^>\r\n]+)>|([^)\s]+))(?:\s+(?:"[^"]*"|\'[^\']*\'|\([^)]*\)))?\s*\)',
            markdown,
        )
    ]
    # Reference-style Markdown link definitions: [label]: target or [label]: <target with spaces>
    ref_targets = [
        a or b
        for a, b in re.findall(
            r"^\s*\[[^\]]+\]:\s*(?:<([^>\r\n]+)>|(\S+))",
            markdown,
            flags=re.MULTILINE,
        )
    ]
    # HTML img/a attributes: src="..." / src='...' and href="..." / href='...'
    html_targets = [
        a or b
        for a, b in re.findall(r'''(?:src|href)\s*=\s*(?:"([^"]+)"|'([^']+)')''', markdown)
    ]

    workflow_url_re = re.compile(
        r"^https://github\.com/rizonesoft/Berth/actions/workflows/([^/?#]+?)(?:/badge\.svg)?(?:\?.*)?$"
    )

    for target in [*badge_targets, *md_targets, *ref_targets, *html_targets]:
        wf_match = workflow_url_re.match(target)
        if wf_match:
            if not _exists_case_sensitive(f".github/workflows/{wf_match.group(1)}", base_dir):
                errors.append(f"missing GitHub workflow referenced by badge/link: {target}")
            continue
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        if target.startswith("#"):
            anchor = target[1:]
            if anchor not in headings:
                errors.append(f"unresolved in-page anchor: {target}")
            continue
        path_part, _, fragment = target.partition("#")
        if not path_part:
            continue
        if not _exists_case_sensitive(path_part, base_dir):
            errors.append(f"missing relative path: {target}")
            continue
        resolved = (base_dir / path_part).resolve()
        if fragment and resolved.suffix.lower() == ".md":
            if resolved not in heading_cache:
                heading_cache[resolved] = collect_headings(resolved.read_text(encoding="utf-8"))
            if fragment not in heading_cache[resolved]:
                errors.append(f"unresolved markdown fragment: {target}")

    return errors


def validate_png_bytes(
    data: bytes,
    label: str = "<bytes>",
    expected_size: tuple[int, int] | None = None,
) -> tuple[int, int]:
    """Validate PNG signature, IHDR fields, chunk CRCs, bounded IDAT decompression, and IEND."""
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{label} is not a valid PNG")
    pos = 8
    width = height = 0
    bit_depth = color_type = 0
    idat_parts: list[bytes] = []
    saw_ihdr = False
    saw_iend = False
    while pos + 12 <= len(data):
        length = struct.unpack(">I", data[pos : pos + 4])[0]
        tag = data[pos + 4 : pos + 8]
        payload_start = pos + 8
        payload_end = payload_start + length
        crc_end = payload_end + 4
        if crc_end > len(data):
            raise ValueError(f"{label} has a truncated {tag!r} chunk")
        payload = data[payload_start:payload_end]
        expected_crc = struct.unpack(">I", data[payload_end:crc_end])[0]
        actual_crc = zlib.crc32(tag + payload) & 0xFFFFFFFF
        if actual_crc != expected_crc:
            raise ValueError(f"{label} failed CRC check on chunk {tag!r}")
        if tag == b"IHDR":
            if length != 13:
                raise ValueError(f"{label} has invalid IHDR length {length}")
            width, height, bit_depth, color_type, compression, filter_method, interlace = struct.unpack(
                ">IIBBBBB", payload
            )
            if compression != 0:
                raise ValueError(f"{label} has invalid PNG compression method {compression}")
            if filter_method != 0:
                raise ValueError(f"{label} has invalid PNG filter method {filter_method}")
            if interlace not in (0, 1):
                raise ValueError(f"{label} has invalid PNG interlace method {interlace}")
            if width <= 0 or height <= 0 or width > 4096 or height > 4096:
                raise ValueError(f"{label} has out-of-range dimensions {width}x{height}")
            if expected_size is not None and (width, height) != expected_size:
                raise ValueError(f"{label} has dimensions {width}x{height}, expected {expected_size[0]}x{expected_size[1]}")
            saw_ihdr = True
        elif tag == b"IDAT":
            if not saw_ihdr:
                raise ValueError(f"{label} has IDAT before IHDR")
            idat_parts.append(payload)
        elif tag == b"IEND":
            saw_iend = True
            pos = crc_end
            break
        pos = crc_end
    if not saw_ihdr or not saw_iend or not idat_parts:
        raise ValueError(f"{label} is missing required PNG chunks (IHDR/IDAT/IEND)")
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color_type)
    if channels is None:
        raise ValueError(f"{label} has invalid PNG colour type {color_type}")
    if bit_depth not in (8, 16):
        raise ValueError(f"{label} has unsupported PNG bit depth {bit_depth}")
    expected_len = height * (1 + width * channels * (bit_depth // 8))
    decompressor = zlib.decompressobj()
    raw = decompressor.decompress(b"".join(idat_parts), expected_len + 1)
    if len(raw) > expected_len or decompressor.unconsumed_tail:
        raise ValueError(f"{label} decompressed payload exceeds expected {expected_len} bytes")
    raw += decompressor.flush()
    if not decompressor.eof or len(raw) != expected_len:
        raise ValueError(f"{label} decoded to {len(raw)} bytes, expected {expected_len}")
    return width, height


def read_png_dimensions(path: Path, expected_size: tuple[int, int] | None = None) -> tuple[int, int]:
    return validate_png_bytes(path.read_bytes(), str(path), expected_size=expected_size)


class ReadmeTests(unittest.TestCase):
    def test_all_links_images_and_anchors_resolve(self) -> None:
        text = README.read_text(encoding="utf-8")
        errors = check_readme_links(text, ROOT)
        self.assertEqual(errors, [], f"Broken README references: {errors}")

    def test_misspelled_image_path_fails_check(self) -> None:
        broken = '<img src="docs/images/main-window-lght.png" alt="broken">\n'
        errors = check_readme_links(broken, ROOT)
        self.assertTrue(any("main-window-lght.png" in err for err in errors))

    def test_case_mismatch_and_reference_style_link_fail_check(self) -> None:
        case_broken = "[License](license)\n"
        case_errors = check_readme_links(case_broken, ROOT)
        self.assertTrue(any("license" in err for err in case_errors))

        ref_broken = "[doc][d]\n\n[d]: missing.md\n"
        ref_errors = check_readme_links(ref_broken, ROOT)
        self.assertTrue(any("missing.md" in err for err in ref_errors))

        titled_broken = '[guide](missing.md "Guide")\n'
        titled_errors = check_readme_links(titled_broken, ROOT)
        self.assertTrue(any("missing.md" in err for err in titled_errors))

        spaced_broken = "[guide](<docs/missing guide.md>)\n"
        spaced_errors = check_readme_links(spaced_broken, ROOT)
        self.assertEqual(len(spaced_errors), 1)
        self.assertIn("docs/missing guide.md", spaced_errors[0])

        spaced_valid = "[guide](<README.md>)\n"
        self.assertEqual(check_readme_links(spaced_valid, ROOT), [])

        single_quoted = "<img src='missing.png'>\n"
        sq_errors = check_readme_links(single_quoted, ROOT)
        self.assertEqual(len(sq_errors), 1)
        self.assertIn("missing.png", sq_errors[0])

    def test_truncated_invalid_color_and_oversized_png_fail_decoder_check(self) -> None:
        light_path = ROOT / "docs" / "images" / "main-window-light.png"
        with self.assertRaises(ValueError):
            validate_png_bytes(light_path.read_bytes()[:40], "truncated.png")

        def make_chunk(tag: bytes, payload: bytes) -> bytes:
            return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

        # Reserved colour type 7 must be rejected even with a valid CRC.
        bad_ihdr = struct.pack(">IIBBBBB", 2, 2, 8, 7, 0, 0, 0)
        bad_color_png = b"\x89PNG\r\n\x1a\n" + make_chunk(b"IHDR", bad_ihdr) + make_chunk(b"IDAT", zlib.compress(b"\x00" * 10)) + make_chunk(b"IEND", b"")
        with self.assertRaises(ValueError):
            validate_png_bytes(bad_color_png, "bad-color.png")

        # Non-zero compression method must be rejected.
        bad_comp_ihdr = struct.pack(">IIBBBBB", 2, 2, 8, 2, 1, 0, 0)
        bad_comp_png = b"\x89PNG\r\n\x1a\n" + make_chunk(b"IHDR", bad_comp_ihdr) + make_chunk(b"IDAT", zlib.compress(b"\x00" * 14)) + make_chunk(b"IEND", b"")
        with self.assertRaises(ValueError):
            validate_png_bytes(bad_comp_png, "bad-comp.png")

        # Huge declared dimensions must be rejected before IDAT decompression.
        huge_ihdr = struct.pack(">IIBBBBB", 32768, 32768, 8, 2, 0, 0, 0)
        huge_png = b"\x89PNG\r\n\x1a\n" + make_chunk(b"IHDR", huge_ihdr) + make_chunk(b"IDAT", zlib.compress(b"\x00" * 64)) + make_chunk(b"IEND", b"")
        with self.assertRaises(ValueError):
            validate_png_bytes(huge_png, "huge.png", expected_size=(980, 640))

        # Oversized decompressed payload must be rejected by the bounded decompressor.
        small_ihdr = struct.pack(">IIBBBBB", 2, 2, 8, 2, 0, 0, 0)
        bomb_idat = zlib.compress(b"\x00" * 65536, level=9)
        bomb_png = b"\x89PNG\r\n\x1a\n" + make_chunk(b"IHDR", small_ihdr) + make_chunk(b"IDAT", bomb_idat) + make_chunk(b"IEND", b"")
        with self.assertRaises(ValueError):
            validate_png_bytes(bomb_png, "oversized.png")

    def test_broken_linked_badge_and_missing_md_fragment_fail_check(self) -> None:
        broken_badge = "[![License](https://img.shields.io/example)](LICENCE-MISSING)\n"
        badge_errors = check_readme_links(broken_badge, ROOT)
        self.assertTrue(any("LICENCE-MISSING" in err for err in badge_errors))

        broken_frag = "[CLI](design/SPEC.md#missing-heading)\n"
        frag_errors = check_readme_links(broken_frag, ROOT)
        self.assertTrue(any("design/SPEC.md#missing-heading" in err for err in frag_errors))

        broken_wf = (
            "[![Build](https://github.com/rizonesoft/Berth/actions/workflows/missing.yml/badge.svg?branch=main)]"
            "(https://github.com/rizonesoft/Berth/actions/workflows/missing.yml)\n"
        )
        wf_errors = check_readme_links(broken_wf, ROOT)
        self.assertTrue(any("missing.yml" in err for err in wf_errors))

    def test_readme_screenshots_badge_sections_and_limits(self) -> None:
        text = README.read_text(encoding="utf-8")
        self.assertNotIn("not built", text.lower())
        self.assertIn("![Status: pre-release](https://img.shields.io/badge/status-pre--release-0F6CBD)", text)

        imgs = re.findall(r'<img\s+src="([^"]+)"', text)
        self.assertGreaterEqual(len(imgs), 3)
        self.assertEqual(imgs[0], "assets/icon/berth-icon-256.png")
        self.assertEqual(imgs[1], "docs/images/main-window-light.png")
        self.assertEqual(imgs[2], "docs/images/main-window-dark.png")

        # Verify each screenshot is paired with its own sample-data caption.
        self.assertRegex(text, r'main-window-light\.png"[^>]*>\s*\n\s*\n<sub>[^<]*sample data')
        self.assertRegex(text, r'main-window-dark\.png"[^>]*>\s*\n\s*\n<sub>[^<]*sample data')

        light_path = ROOT / "docs" / "images" / "main-window-light.png"
        dark_path = ROOT / "docs" / "images" / "main-window-dark.png"
        self.assertEqual(read_png_dimensions(light_path, expected_size=(980, 640)), (980, 640))
        self.assertEqual(read_png_dimensions(dark_path, expected_size=(980, 640)), (980, 640))
        self.assertNotEqual(light_path.read_bytes(), dark_path.read_bytes())

        for required_heading in (
            "## What works today?",
            "## How do I install it?",
            "## How do I use it?",
            "## What does it not do?",
            "## How do I remove it?",
            "## What does it look like?",
        ):
            self.assertIn(required_heading, text)

        for required_phrase in (
            "[Install](#how-do-i-install-it)",
            "[Use](#how-do-i-use-it)",
            "[Remove](#how-do-i-remove-it)",
            "sample data",
            "PowerShell 7",
            ".NET 11 SDK",
            "Checksum mismatch",
            "design/mockups/main-window-light.png",
            "design/mockups/main-window-dark.png",
            "Get-FileHash",
            "%APPDATA%\\Berth",
            "%LOCALAPPDATA%\\Berth",
            "Windows 11 version 24H2 or later",
            "`x64` only",
            "saving still works",
            "restoring is disabled",
            "Turn it off",
            "save layouts automatically",
            "more than one layout per monitor arrangement",
            "sync layouts between machines",
            "per-virtual-desktop layouts",
            "arrange icons into patterns",
            "import other tools' layouts",
            "no installer",
            "no telemetry or network access",
        ):
            self.assertIn(required_phrase, text)


if __name__ == "__main__":
    unittest.main()
