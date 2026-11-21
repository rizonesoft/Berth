"""Tests for README.md links, anchors, screenshots and required release sections."""

from __future__ import annotations

import re
import struct
import unittest
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


def check_readme_links(markdown: str, base_dir: Path) -> list[str]:
    """Return a list of broken relative file/image paths or unresolved in-page anchors."""
    errors: list[str] = []
    headings = collect_headings(markdown)

    # Markdown links/images: [text](target) and ![alt](target)
    md_targets = re.findall(r"!?\[[^\]]*\]\(([^)\s]+)\)", markdown)
    # HTML img/a attributes: src="..." and href="..."
    html_targets = re.findall(r'(?:src|href)="([^"]+)"', markdown)

    for target in [*md_targets, *html_targets]:
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        if target.startswith("#"):
            anchor = target[1:]
            if anchor not in headings:
                errors.append(f"unresolved in-page anchor: {target}")
            continue
        path_part = target.split("#", 1)[0]
        if not path_part:
            continue
        resolved = (base_dir / path_part).resolve()
        if not resolved.exists():
            errors.append(f"missing relative path: {target}")

    return errors


def read_png_dimensions(path: Path) -> tuple[int, int]:
    data = path.read_bytes()
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path} is not a valid PNG")
    return struct.unpack(">II", data[16:24])


class ReadmeTests(unittest.TestCase):
    def test_all_links_images_and_anchors_resolve(self) -> None:
        text = README.read_text(encoding="utf-8")
        errors = check_readme_links(text, ROOT)
        self.assertEqual(errors, [], f"Broken README references: {errors}")

    def test_misspelled_image_path_fails_check(self) -> None:
        broken = '<img src="docs/images/main-window-lght.png" alt="broken">\n'
        errors = check_readme_links(broken, ROOT)
        self.assertTrue(any("main-window-lght.png" in err for err in errors))

    def test_readme_screenshots_badge_sections_and_limits(self) -> None:
        text = README.read_text(encoding="utf-8")
        self.assertNotIn("not built", text.lower())
        self.assertIn("![Status: pre-release](https://img.shields.io/badge/status-pre--release-0F6CBD)", text)

        imgs = re.findall(r'<img\s+src="([^"]+)"', text)
        self.assertGreaterEqual(len(imgs), 3)
        self.assertEqual(imgs[0], "assets/icon/berth-icon-256.png")
        self.assertEqual(imgs[1], "docs/images/main-window-light.png")
        self.assertEqual(imgs[2], "docs/images/main-window-dark.png")

        light_path = ROOT / "docs" / "images" / "main-window-light.png"
        dark_path = ROOT / "docs" / "images" / "main-window-dark.png"
        self.assertEqual(read_png_dimensions(light_path), (980, 640))
        self.assertEqual(read_png_dimensions(dark_path), (980, 640))
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
            "design/mockups/main-window-light.png",
            "design/mockups/main-window-dark.png",
            "Get-FileHash",
            "%APPDATA%\\Berth",
            "%LOCALAPPDATA%\\Berth",
            "Windows 11 version 24H2 or later",
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
