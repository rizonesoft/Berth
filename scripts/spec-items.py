#!/usr/bin/env python3
"""Parse design/SPEC.md §1 to §20 items and verify docs/acceptance/2026-spec-sweep.md."""

from __future__ import annotations

import argparse
import hashlib
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SPEC = ROOT / "design" / "SPEC.md"
DEFAULT_SWEEP = ROOT / "docs" / "acceptance" / "2026-spec-sweep.md"
OPERATOR_TODO = ROOT / "todo" / "99-manual" / "TODO-01-operator-steps.md"
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)

# Deferred SPEC items must map to an operator section in todo/99-manual/TODO-01-operator-steps.md
# whose body actually addresses the deferred topic (preventing assignment to an unrelated section).
DEFERRED_TOPIC_KEYWORDS: dict[str, tuple[str, ...]] = {
    "§9.5": ("auto arrange", "berth_test_allow_autoarrange"),
    "§12.8": ("warning notification", "could not restore"),
    "§16.2": ("dock", "undock"),
}

DEFERRED_OBSERVATION_DOCS: dict[str, str] = {
    "§9.5": "docs/acceptance/2026-autoarrange-roundtrip.md",
    "§12.8": "docs/acceptance/2026-autoarrange-roundtrip.md",
    "§16.2": "docs/acceptance/2026-dock-walkthrough.md",
}


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


def _strip_html_comments(text: str) -> tuple[str, bool]:
    """Remove Markdown/HTML comments (<!-- ... -->) in one linear pass; returns (cleaned, had_unclosed)."""
    out: list[str] = []
    pos = 0
    length = len(text)
    had_unclosed = False
    while pos < length:
        start = text.find("<!--", pos)
        if start == -1:
            out.append(text[pos:])
            break
        out.append(text[pos:start])
        end = text.find("-->", start + 4)
        if end == -1:
            had_unclosed = True
            out.append("\n" * text[start:].count("\n"))
            break
        out.append("\n" * text[start : end + 3].count("\n"))
        pos = end + 3
    return "".join(out), had_unclosed


def extract_spec_items(spec_text: str, min_section: int = 1, max_section: int = 20) -> list[SpecItem]:
    """Extract every bullet, table data row, normative paragraph, and code schema block in §1..§20."""
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

        add_item("prose", line_no, stripped)
        i += 1

    return items


def _collect_existing_test_names(repo_root: Path) -> dict[str, set[str]]:
    """Map repo-relative POSIX path -> set of actual test method names ([Fact]/[Theory] in C#, test_* in Python)."""
    cache: dict[str, set[str]] = {}
    cs_test_re = re.compile(
        r"\[(?:Fact|Theory)[^\]]*\](?:\s*\[[^\]]*\])*\s*(?:public|internal)\s+(?:static\s+)?(?:async\s+)?(?:Task|void)\s+(\w+)\s*\("
    )
    for base, pattern in ((repo_root / "tests", "*.cs"), (repo_root / "scripts" / "tests", "test_*.py")):
        if not base.exists():
            continue
        for p in base.rglob(pattern):
            rel = p.relative_to(repo_root).as_posix()
            text = p.read_text(encoding="utf-8", errors="ignore")
            if pattern.endswith(".cs"):
                names = set(cs_test_re.findall(text))
            else:
                names = set(re.findall(r"def\s+(test_\w+)\s*\(", text))
            cache[rel] = names
    return cache


def _parse_operator_sections(repo_root: Path, operator_todo_text: str | None = None) -> dict[int, tuple[str, bool, bool]]:
    """Return {section_number: (lowercase_body, observation_open, section_open)} for todo/99-manual/TODO-01-operator-steps.md."""
    if operator_todo_text is None:
        path = repo_root / "todo" / "99-manual" / "TODO-01-operator-steps.md"
        if not path.exists():
            return {}
        operator_todo_text = path.read_text(encoding="utf-8")
    sections: dict[int, list[str]] = {}
    cur: int | None = None
    for line in operator_todo_text.splitlines():
        m = re.match(r"^##\s+(\d+)\.\s+", line.strip())
        if m:
            cur = int(m.group(1))
            sections[cur] = [line]
        elif cur is not None:
            if line.strip().startswith("## "):
                cur = None
            else:
                sections[cur].append(line)
    result: dict[int, tuple[str, bool, bool]] = {}
    for k, lines in sections.items():
        body = "\n".join(lines)
        open_items = [l.strip() for l in lines if l.strip().startswith("- [ ] ")]
        section_open = bool(open_items)
        observation_open = any(
            not item.startswith("- [ ] Commit:") and not item.startswith("- [ ] Update row")
            for item in open_items
        )
        result[k] = (body.lower(), observation_open, section_open)
    return result


def _parse_proof_catalog(sweep_text: str) -> dict[str, list[str]]:
    """Parse '## Proof catalog' entries: `- `P-id`: `path::TestName`, ...`."""
    catalog: dict[str, list[str]] = {}
    in_catalog = False
    for raw in sweep_text.splitlines():
        stripped = raw.strip()
        if stripped == "## Proof catalog":
            in_catalog = True
            continue
        if in_catalog and stripped.startswith("## "):
            break
        if not in_catalog or not stripped.startswith("- "):
            continue
        m = re.match(r"^-\s+`(P[0-9A-Za-z._-]+)`:\s*(.+)$", stripped)
        if m:
            pid = m.group(1)
            citations = re.findall(r"`((?:tests|scripts)/[^`]+::[^`]+)`", m.group(2))
            catalog[pid] = citations
    return catalog


def _validate_citations(
    ref: str,
    proof_cell: str,
    catalog: dict[str, list[str]],
    test_index: dict[str, set[str]],
    repo_root: Path,
) -> list[str]:
    errors: list[str] = []
    citations = re.findall(r"`((?:tests|scripts)/[^`]+::[^`]+)`", proof_cell)
    for pid in re.findall(r"`(P[0-9A-Za-z._-]+)`", proof_cell):
        if pid not in catalog:
            errors.append(f"sweep row {ref} references unknown proof catalog entry: {pid}")
        else:
            citations.extend(catalog[pid])

    if not citations:
        errors.append(f"sweep row {ref} must cite at least one `file::TestMethod` proof (got {proof_cell!r})")
        return errors

    for cit in citations:
        file_path, _, symbol = cit.partition("::")
        full = repo_root / file_path
        if not full.exists():
            errors.append(f"sweep row {ref} cites missing file: {file_path}")
        elif not symbol:
            errors.append(f"sweep row {ref} cites {file_path} without a test method name")
        elif file_path not in test_index or symbol not in test_index[file_path]:
            errors.append(f"sweep row {ref} cites missing test {symbol} in {file_path}")
    return errors


def _check_published_receipt(cleaned_sweep: str, repo_root: Path) -> list[str]:
    """Verify the sweep header names a passing -Published commit that covers all src/, tests/, and scripts/ changes."""
    errors: list[str] = []
    commit_m = re.search(r"^\s*-\s+\*\*Tested commit:\*\*\s+`([0-9a-f]{40})`\s*$", cleaned_sweep, re.MULTILINE)
    result_m = re.search(r"^\s*-\s+\*\*Gate result:\*\*\s+`passed`(?:\s|$|\()", cleaned_sweep, re.MULTILINE)
    cmd_m = re.search(r"^\s*-\s+\*\*Gate command:\*\*\s+`pwsh scripts/check-all\.ps1 -Published`\s*$", cleaned_sweep, re.MULTILINE)
    if not cmd_m:
        errors.append("sweep header is missing `- **Gate command:** `pwsh scripts/check-all.ps1 -Published``")
    if not result_m:
        errors.append("sweep header is missing `- **Gate result:** `passed``")
    if not commit_m:
        errors.append("sweep header is missing `- **Tested commit:** `<40-hex-sha>``")
        return errors

    tested_sha = commit_m.group(1)
    exists = subprocess.run(
        ["git", "cat-file", "-e", f"{tested_sha}^{{commit}}"],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        creationflags=CREATE_NO_WINDOW,
        check=False,
    )
    if exists.returncode != 0:
        errors.append(f"sweep header cites unknown or unreachable tested commit {tested_sha} (fetch full git history)")
        return errors

    diff = subprocess.run(
        ["git", "diff", "--name-only", tested_sha, "HEAD", "--", "src/", "tests/", "scripts/"],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        creationflags=CREATE_NO_WINDOW,
        check=False,
    )
    if diff.returncode != 0:
        errors.append(f"git diff failed while checking tested commit {tested_sha}: {diff.stderr.strip()}")
        return errors
    changed = [line.strip() for line in diff.stdout.splitlines() if line.strip()]
    if changed:
        errors.append(
            f"code or test files changed after tested commit {tested_sha[:12]}: {', '.join(changed[:5])}"
        )
    return errors


def check_sweep(
    spec_text: str,
    sweep_text: str,
    repo_root: Path = ROOT,
    *,
    operator_todo_text: str | None = None,
    check_receipt: bool = True,
) -> list[str]:
    """Validate that sweep_text covers every §1..§20 SPEC item with matching digest, real proof, and valid status."""
    errors: list[str] = []
    expected_items = extract_spec_items(spec_text)
    expected_by_ref = {item.ref: item for item in expected_items}
    test_index = _collect_existing_test_names(repo_root)
    operator_sections = _parse_operator_sections(repo_root, operator_todo_text=operator_todo_text)

    cleaned_sweep, had_unclosed_comment = _strip_html_comments(sweep_text)
    if had_unclosed_comment:
        errors.append("sweep contains an unclosed HTML/Markdown comment ('<!--' without matching '-->')")
    if check_receipt:
        errors.extend(_check_published_receipt(cleaned_sweep, repo_root))
    catalog = _parse_proof_catalog(cleaned_sweep)

    seen: dict[str, tuple[str, str, str]] = {}
    in_sweep_table = False
    in_fence = False
    for line_no, raw in enumerate(cleaned_sweep.splitlines(), start=1):
        stripped = raw.strip()
        if stripped.startswith("```"):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
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
        if not c1.strip():
            errors.append(f"sweep row {ref} at line {line_no} has an empty Requirement description")

        status = c3.strip()
        if status == "yes":
            if ref in DEFERRED_TOPIC_KEYWORDS:
                # Reject 'yes' while the owning D99 T01 operator observation steps are still open.
                for op_sec, (body, observation_open, _) in operator_sections.items():
                    if observation_open and all(kw in body for kw in DEFERRED_TOPIC_KEYWORDS[ref]):
                        errors.append(
                            f"sweep row {ref} cannot be marked 'yes' while operator section D99 T01 §{op_sec} is still open"
                        )
                        break
                # Completed operator-deferred rows must also cite and back up their recorded observation document.
                obs_doc = DEFERRED_OBSERVATION_DOCS[ref]
                raw_proof_sources = c2 + " " + " ".join(
                    raw_line for raw_line in cleaned_sweep.splitlines()
                    if any(f"`{pid}`:" in raw_line for pid in re.findall(r"`(P[0-9A-Za-z._-]+)`", c2))
                )
                if f"`{obs_doc}`" not in raw_proof_sources:
                    errors.append(
                        f"sweep row {ref} marked 'yes' must cite recorded observation document `{obs_doc}`"
                    )
                else:
                    obs_path = repo_root / obs_doc
                    if not obs_path.exists() or not obs_path.read_text(encoding="utf-8").strip():
                        errors.append(f"sweep row {ref} cites missing or empty observation document `{obs_doc}`")
            errors.extend(_validate_citations(ref, c2, catalog, test_index, repo_root))
        else:
            dm = re.fullmatch(r"deferred \(D99 T01 §(\d+)\)", status)
            if not dm:
                errors.append(f"sweep row {ref} has invalid status {status!r} (must be 'yes' or 'deferred (D99 T01 §N)')")
            else:
                op_sec = int(dm.group(1))
                errors.extend(_validate_citations(ref, c2, catalog, test_index, repo_root))
                if op_sec not in operator_sections:
                    errors.append(f"sweep row {ref} defers to nonexistent D99 T01 §{op_sec}")
                elif ref not in DEFERRED_TOPIC_KEYWORDS:
                    errors.append(f"sweep row {ref} is not an approved operator-deferred item")
                else:
                    body, _, section_open = operator_sections[op_sec]
                    keywords = DEFERRED_TOPIC_KEYWORDS[ref]
                    if not all(kw in body for kw in keywords):
                        errors.append(
                            f"sweep row {ref} defers to unrelated D99 T01 §{op_sec} (missing topic keywords {keywords})"
                        )
                    elif not section_open:
                        errors.append(
                            f"sweep row {ref} still defers to closed operator section D99 T01 §{op_sec}; update status to 'yes' with the recorded observation"
                        )

    for item in expected_items:
        if item.ref not in seen:
            errors.append(f"missing sweep row for {item.ref} (line {item.line}, {item.kind}, [{item.digest}]): {item.text[:60]}")

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="List SPEC §1..§20 items or check docs/acceptance/2026-spec-sweep.md.")
    parser.add_argument("--spec", type=Path, default=DEFAULT_SPEC, help="Path to design/SPEC.md")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--list", action="store_true", help="List every SPEC §1..§20 item with its ref and digest")
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
