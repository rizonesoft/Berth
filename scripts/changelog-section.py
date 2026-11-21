#!/usr/bin/env python3
"""Extract a single version's release notes section from CHANGELOG.md.

Supports both undated (`## 0.1.0`, `## [0.1.0]`) and dated (`## 0.1.0 - 2026-11-21`,
`## [0.1.0] - 2026-11-21`) Keep a Changelog level-2 headings. Exits non-zero if the
requested version heading is missing or its body is empty.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CHANGELOG = ROOT / "CHANGELOG.md"


def extract_section(text: str, version: str) -> str:
    """Return the trimmed body of the level-2 section for ``version``."""
    clean_version = version.strip()
    if not clean_version:
        raise ValueError("version must not be empty")

    # Match `## 0.1.0`, `## [0.1.0]`, `## 0.1.0 - 2026-11-21`, `## [0.1.0] - 2026-11-21`, etc.
    escaped = re.escape(clean_version)
    heading_re = re.compile(
        rf"^##\s+(?:\[{escaped}\]|{escaped})(?:\s+-\s+\S.*)?\s*$"
    )
    any_h2_re = re.compile(r"^##\s+")

    lines = text.splitlines()
    start_idx: int | None = None
    for idx, line in enumerate(lines):
        if heading_re.match(line):
            start_idx = idx + 1
            break

    if start_idx is None:
        raise KeyError(f"version {clean_version!r} not found in changelog")

    body_lines: list[str] = []
    fence_char: str | None = None
    fence_len = 0
    fence_re = re.compile(r"^\s*(`{3,}|~{3,})")

    for line in lines[start_idx:]:
        m = fence_re.match(line)
        if m:
            marker = m.group(1)
            if fence_char is None:
                fence_char = marker[0]
                fence_len = len(marker)
            elif marker[0] == fence_char and len(marker) >= fence_len and line.strip() == marker:
                fence_char = None
                fence_len = 0
        elif fence_char is None and any_h2_re.match(line):
            break
        body_lines.append(line)

    body = "\n".join(body_lines).strip()
    if not body:
        raise ValueError(f"version {clean_version!r} section is empty")
    return body + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Print a version's section from CHANGELOG.md.")
    parser.add_argument("version", help="Semantic version to extract, e.g. 0.1.0 (without leading v)")
    parser.add_argument(
        "--changelog",
        type=Path,
        default=DEFAULT_CHANGELOG,
        help="Path to CHANGELOG.md (default: repository root CHANGELOG.md)",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=None,
        help="Optional output file path (UTF-8); prints to stdout when omitted",
    )
    args = parser.parse_args(argv)

    try:
        content = args.changelog.read_text(encoding="utf-8")
        section = extract_section(content, args.version)
    except (OSError, KeyError, ValueError) as exc:
        print(f"changelog-section: {exc}", file=sys.stderr)
        return 1

    if args.out is not None:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(section, encoding="utf-8", newline="\n")
    else:
        sys.stdout.write(section)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
