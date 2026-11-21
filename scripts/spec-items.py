#!/usr/bin/env python3
"""Parse design/SPEC.md §2 to §19 items and verify docs/acceptance/2026-spec-sweep.md."""

from __future__ import annotations

import argparse
import hashlib
import re
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SPEC = ROOT / "design" / "SPEC.md"
DEFAULT_SWEEP = ROOT / "docs" / "acceptance" / "2026-spec-sweep.md"


@dataclass(frozen=True)
class SpecItem:
    ref: str
    section: int
    kind: str
    line: int
    text: str
    digest: str


def _digest(text: str) -> str:
    normalized = re.sub(r"\s+", " ", text.strip())
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()[:8]


def extract_spec_items(spec_text: str, min_section: int = 2, max_section: int = 19) -> list[SpecItem]:
    """Extract every bullet, table data row, normative paragraph, and code schema block in §2..§19."""
    items: list[SpecItem] = []
    current_section: int | None = None
    section_counts: dict[int, int] = {}
    lines = spec_text.splitlines()
    i = 0
    in_fence = False
    fence_start_line = 0
    fence_lines: list[str] = []
    table_seen_sep = False

    def add_item(kind: str, line_no: int, text: str) -> None:
        assert current_section is not None
        idx = section_counts.get(current_section, 0) + 1
        section_counts[current_section] = idx
        items.append(
            SpecItem(
                ref=f"§{current_section}.{idx}",
                section=current_section,
                kind=kind,
                line=line_no,
                text=text.strip(),
                digest=_digest(text),
            )
        )

    while i < len(lines):
        raw = lines[i]
        stripped = raw.strip()
        line_no = i + 1

        if in_fence:
            fence_lines.append(raw)
            if stripped.startswith("```"):
                in_fence = False
                if current_section is not None and min_section <= current_section <= max_section:
                    add_item("schema", fence_start_line, "\n".join(fence_lines))
                fence_lines = []
            i += 1
            continue

        h2 = re.match(r"^##\s+(\d+)\.\s+(.+?)\s*$", stripped)
        if h2:
            current_section = int(h2.group(1))
            table_seen_sep = False
            i += 1
            continue

        if current_section is None or not (min_section <= current_section <= max_section):
            i += 1
            continue

        if not stripped:
            table_seen_sep = False
            i += 1
            continue

        if stripped.startswith("```"):
            in_fence = True
            fence_start_line = line_no
            fence_lines = [raw]
            table_seen_sep = False
            i += 1
            continue

        if stripped.startswith("|"):
            cells = [c.strip() for c in stripped.strip("|").split("|")]
            if all(re.fullmatch(r":?-{2,}:?", c) for c in cells if c):
                table_seen_sep = True
            elif table_seen_sep:
                add_item("table-row", line_no, stripped)
            i += 1
            continue

        table_seen_sep = False
        if stripped.startswith("- "):
            add_item("bullet", line_no, stripped[2:].strip())
            i += 1
            continue

        if stripped.startswith("#"):
            i += 1
            continue

        # Normative prose paragraph
        add_item("prose", line_no, stripped)
        i += 1

    return items


def _collect_existing_test_names(repo_root: Path) -> dict[str, set[str]]:
    """Map repo-relative POSIX path -> set of test/function/symbol names found in that file."""
    cache: dict[str, set[str]] = {}
    for base, pattern in ((repo_root / "tests", "*.cs"), (repo_root / "scripts" / "tests", "test_*.py")):
        if not base.exists():
            continue
        for p in base.rglob(pattern):
            rel = p.relative_to(repo_root).as_posix()
            text = p.read_text(encoding="utf-8", errors="ignore")
            if pattern.endswith(".cs"):
                names = set(re.findall(r"\b(?:public|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\]?,\s]+\s+(\w+)\s*\(", text))
            else:
                names = set(re.findall(r"def\s+(test_\w+)\s*\(", text))
            cache[rel] = names
    return cache


def check_sweep(spec_text: str, sweep_text: str, repo_root: Path = ROOT) -> list[str]:
    """Validate that sweep_text covers every §2..§19 SPEC item with matching digest, real proof, and valid status."""
    errors: list[str] = []
    expected_items = extract_spec_items(spec_text)
    expected_by_ref = {item.ref: item for item in expected_items}
    test_index = _collect_existing_test_names(repo_root)

    seen: dict[str, tuple[str, str, str]] = {}
    in_sweep_table = False
    for line_no, raw in enumerate(sweep_text.splitlines(), start=1):
        stripped = raw.strip()
        if not stripped.startswith("|"):
            if in_sweep_table and stripped.startswith("## "):
                in_sweep_table = False
            continue
        cells = [c.strip() for c in stripped.strip("|").split("|")]
        if len(cells) != 4:
            continue
        c0, c1, c2, c3 = cells
        if c0.lower() == "ref" or all(set(c) <= {"-", ":", " "} for c in cells):
            in_sweep_table = True
            continue
        if not in_sweep_table:
            continue

        if len(raw) > 120:
            errors.append(f"sweep line {line_no} exceeds 120 characters ({len(raw)})")

        m = re.fullmatch(r"(§\d+\.\d+)\s+\[([0-9a-f]{8})\]", c0)
        if not m:
            errors.append(f"sweep line {line_no} has invalid Ref cell (expected '§N.M [8-hex]'): {c0!r}")
            continue
        ref, digest = m.group(1), m.group(2)
        if ref in seen:
            errors.append(f"duplicate sweep row for {ref} at line {line_no}")
            continue
        seen[ref] = (digest, c2, c3)

        if ref not in expected_by_ref:
            errors.append(f"stale/unknown SPEC ref in sweep at line {line_no}: {ref}")
            continue
        if expected_by_ref[ref].digest != digest:
            errors.append(
                f"SPEC item {ref} changed since sweep was written: expected digest {expected_by_ref[ref].digest}, got {digest}"
            )

        status = c3.strip()
        if status == "yes":
            if not c2.strip():
                errors.append(f"sweep row {ref} is 'yes' but has empty Proof")
            else:
                # Verify every cited file path (tests/... or scripts/...) exists and any named test exists in it.
                for file_ref in re.findall(r"`((?:tests|scripts|design|docs|src)/[^`]+)`", c2):
                    file_path, _, symbol = file_ref.partition("::")
                    full = repo_root / file_path
                    if not full.exists():
                        errors.append(f"sweep row {ref} cites missing file: {file_path}")
                    elif symbol and file_path in test_index and symbol not in test_index[file_path]:
                        errors.append(f"sweep row {ref} cites missing test {symbol} in {file_path}")
        elif re.fullmatch(r"deferred \(D99 T01 §[1-4]\)", status):
            if not c2.strip():
                errors.append(f"sweep row {ref} is deferred but has empty Proof/Owner note")
        else:
            errors.append(f"sweep row {ref} has invalid status {status!r} (must be 'yes' or 'deferred (D99 T01 §N)')")

    for item in expected_items:
        if item.ref not in seen:
            errors.append(f"missing sweep row for {item.ref} (line {item.line}, {item.kind}, [{item.digest}]): {item.text[:60]}")

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="List SPEC §2..§19 items or check docs/acceptance/2026-spec-sweep.md.")
    parser.add_argument("--spec", type=Path, default=DEFAULT_SPEC, help="Path to design/SPEC.md")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--list", action="store_true", help="List every SPEC §2..§19 item with its ref and digest")
    group.add_argument("--check", type=Path, metavar="SWEEP_MD", help="Validate a sweep Markdown table against SPEC")
    args = parser.parse_args(argv)

    spec_text = args.spec.read_text(encoding="utf-8")
    if args.list:
        items = extract_spec_items(spec_text)
        for item in items:
            preview = re.sub(r"\s+", " ", item.text)[:72]
            print(f"{item.ref} [{item.digest}] ({item.kind} L{item.line}): {preview}")
        print(f"total: {len(items)}")
        return 0

    sweep_text = args.check.read_text(encoding="utf-8")
    errors = check_sweep(spec_text, sweep_text, ROOT)
    if errors:
        for err in errors:
            print(f"spec-items: {err}", file=sys.stderr)
        return 1
    items = extract_spec_items(spec_text)
    print(f"spec-items: ok ({len(items)} items verified)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
