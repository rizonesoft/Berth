"""Tests for NOTICE.md third-party component coverage, CHANGELOG.md 0.1.0 notes, and README notice link."""

from __future__ import annotations

import importlib.util
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
NOTICE_PATH = ROOT / "NOTICE.md"
PACKAGES_PROPS = ROOT / "Directory.Packages.props"
CHANGELOG_PATH = ROOT / "CHANGELOG.md"
README_PATH = ROOT / "README.md"

SHIPPED_HEADING = "Shipped in berth.exe"
REPO_ONLY_HEADING = "Repository and test suite (not shipped in berth.exe)"

EXPECTED_PACKAGE_LICENCES = {
    "Microsoft.CodeAnalysis.CSharp": "MIT",
    "xunit.v3": "Apache-2.0",
}


def _load_extract_section():
    spec = importlib.util.spec_from_file_location("changelog_section", ROOT / "scripts" / "changelog-section.py")
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.extract_section


extract_changelog_section = _load_extract_section()


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def read_central_packages(props_path: Path = PACKAGES_PROPS) -> dict[str, str]:
    root = ET.fromstring(props_path.read_text(encoding="utf-8"))
    packages: dict[str, str] = {}
    for elem in root.iter():
        if _local_name(elem.tag) == "PackageVersion":
            name = (elem.attrib.get("Include") or "").strip()
            version = (elem.attrib.get("Version") or "").strip()
            if name:
                packages[name] = version
    return packages


def read_project_package_references(repo_root: Path = ROOT) -> tuple[set[str], set[str]]:
    """Return (shipped_app_refs, test_only_refs) from .csproj files under src/ and tests/."""
    app_refs: set[str] = set()
    test_refs: set[str] = set()
    for base, target in ((repo_root / "src", app_refs), (repo_root / "tests", test_refs)):
        if not base.exists():
            continue
        for csproj in base.rglob("*.csproj"):
            root = ET.fromstring(csproj.read_text(encoding="utf-8"))
            for elem in root.iter():
                if _local_name(elem.tag) == "PackageReference":
                    name = (elem.attrib.get("Include") or "").strip()
                    if name:
                        target.add(name)
    return app_refs, test_refs - app_refs


def parse_notice_sections(markdown: str) -> dict[str, dict[str, tuple[str, str, str]]]:
    """Parse level-2 sections and their 4-column tables: {section: {component: (version, licence, usage)}}."""
    sections: dict[str, dict[str, tuple[str, str, str]]] = {}
    current: str | None = None
    for line in markdown.splitlines():
        h2 = re.match(r"^##\s+(.+?)\s*$", line)
        if h2:
            current = h2.group(1)
            sections.setdefault(current, {})
            continue
        if current is None or not line.strip().startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) != 4:
            continue
        comp, version, licence, usage = cells
        if comp.lower() == "component" or set(comp) <= {"-", " "}:
            continue
        sections[current][comp] = (version, licence, usage)
    return sections


def check_notice(
    notice_markdown: str,
    central_packages: dict[str, str],
    app_refs: set[str],
    test_only_refs: set[str],
) -> list[str]:
    errors: list[str] = []
    for idx, line in enumerate(notice_markdown.splitlines(), start=1):
        if line.strip().startswith("|") and len(line) > 120:
            errors.append(f"NOTICE.md line {idx} exceeds 120 characters ({len(line)})")

    sections = parse_notice_sections(notice_markdown)
    shipped = sections.get(SHIPPED_HEADING, {})
    repo_only = sections.get(REPO_ONLY_HEADING, {})
    all_entries = {**shipped, **repo_only}

    # Check every central package pin and PackageReference.
    for pkg, expected_ver in sorted(central_packages.items()):
        if pkg not in all_entries:
            errors.append(f"missing NuGet package in NOTICE.md: {pkg}")
            continue
        ver, lic, usage = all_entries[pkg]
        if expected_ver and ver != expected_ver:
            errors.append(f"version mismatch for {pkg}: expected {expected_ver}, got {ver}")
        expected_lic = EXPECTED_PACKAGE_LICENCES.get(pkg)
        if expected_lic is not None:
            if lic != expected_lic:
                errors.append(f"licence mismatch for {pkg}: expected {expected_lic}, got {lic}")
        elif not lic:
            errors.append(f"missing licence for {pkg}")
        if not usage:
            errors.append(f"missing usage description for {pkg}")
        if pkg in test_only_refs:
            if pkg in shipped:
                errors.append(f"test-only package {pkg} must not be listed under '{SHIPPED_HEADING}'")
            if "tests/" not in usage or re.search(r"\b(shipped|bundled)\b.*\bberth\.exe\b", usage, re.IGNORECASE):
                errors.append(f"test-only package {pkg} usage must identify tests/ and never claim shipping in berth.exe")
        if pkg in app_refs and pkg not in shipped:
            errors.append(f"app package {pkg} must be listed under '{SHIPPED_HEADING}'")

    for pkg in sorted(app_refs | test_only_refs):
        if pkg not in all_entries:
            errors.append(f"referenced package missing from NOTICE.md: {pkg}")

    # Check required non-NuGet shipped components and upstream notice links.
    required_shipped = (
        (".NET 11 Runtime", "MIT", "berth.exe"),
        (".NET 11 Windows Desktop Runtime", "MIT", "berth.exe"),
    )
    for comp, expected_lic, usage_hint in required_shipped:
        if comp not in shipped:
            errors.append(f"missing required shipped component in NOTICE.md: {comp}")
            continue
        _, lic, usage = shipped[comp]
        if expected_lic not in lic:
            errors.append(f"unexpected licence for {comp}: {lic}")
        if usage_hint.lower() not in usage.lower():
            errors.append(f"usage for {comp} must explain where it is used ({usage_hint})")

    required_upstream_notices = (
        "https://github.com/dotnet/runtime/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT",
        "https://github.com/dotnet/wpf/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT",
        "https://github.com/dotnet/winforms/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT",
    )
    for url in required_upstream_notices:
        pattern = rf"\[[^\]]+\]\({re.escape(url)}\)"
        if not re.search(pattern, notice_markdown):
            errors.append(f"NOTICE.md must link version-matched upstream notice: {url}")

    # Check required repository-only non-NuGet components.
    required_repo_only = (
        ("Pillow", "MIT-CMU", "assets/build.py"),
        ("Contributor Covenant", "CC BY 4.0", "CODE_OF_CONDUCT.md"),
    )
    for comp, expected_lic, usage_hint in required_repo_only:
        if comp not in repo_only:
            errors.append(f"missing {comp} entry in NOTICE.md")
            continue
        _, lic, usage = repo_only[comp]
        if expected_lic not in lic:
            errors.append(f"unexpected licence for {comp}: {lic}")
        if usage_hint not in usage:
            errors.append(f"{comp} usage must reference {usage_hint}")

    return errors


def check_changelog_010(changelog_markdown: str) -> list[str]:
    errors: list[str] = []
    try:
        section = extract_changelog_section(changelog_markdown, "0.1.0")
    except (KeyError, ValueError) as exc:
        return [f"failed to extract 0.1.0 changelog section: {exc}"]

    required_phrases = (
        "Command line",
        "Tray application",
        "Main window",
        "Automatic restore",
        "assets/",
        "design/SPEC.md",
        "docs/plans/2026-11-15-berth-foundation.md",
    )
    for phrase in required_phrases:
        if phrase not in section:
            errors.append(f"0.1.0 changelog section missing required content: {phrase}")

    expected_notice_link = r"\[[^\]]+\]\(https://github\.com/rizonesoft/Berth/blob/v0\.1\.0/NOTICE\.md\)"
    if not re.search(expected_notice_link, section):
        errors.append("0.1.0 changelog section must link to https://github.com/rizonesoft/Berth/blob/v0.1.0/NOTICE.md")

    return errors


def check_readme_licence_links_notice(readme_markdown: str) -> list[str]:
    match = re.search(r"^##\s+What is the licence\?\s*$(.*?)(?:^##\s+|\Z)", readme_markdown, flags=re.MULTILINE | re.DOTALL)
    if not match:
        return ["README.md is missing '## What is the licence?' heading"]
    body = match.group(1)
    if not re.search(r"\[[^\]]+\]\(NOTICE\.md\)", body):
        return ["README.md '## What is the licence?' section must link to NOTICE.md"]
    return []


class NoticeAndChangelogTests(unittest.TestCase):
    def test_notice_covers_all_packages_and_non_nuget_components(self) -> None:
        central = read_central_packages(PACKAGES_PROPS)
        app_refs, test_only_refs = read_project_package_references(ROOT)
        errors = check_notice(NOTICE_PATH.read_text(encoding="utf-8"), central, app_refs, test_only_refs)
        self.assertEqual(errors, [], f"NOTICE.md errors: {errors}")

    def test_notice_fails_when_package_or_runtime_or_covenant_missing_or_misclassified(self) -> None:
        text = NOTICE_PATH.read_text(encoding="utf-8")
        central = read_central_packages(PACKAGES_PROPS)
        app_refs, test_only_refs = read_project_package_references(ROOT)

        # Missing a Directory.Packages.props package must fail.
        without_xunit = "\n".join(line for line in text.splitlines() if "xunit.v3" not in line)
        errs = check_notice(without_xunit, central, app_refs, test_only_refs)
        self.assertTrue(any("xunit.v3" in e for e in errs))

        # Wrong licence or false shipping claim on xunit.v3 must fail even inside the repository-only table.
        bad_xunit = text.replace(
            "| xunit.v3 | 4.0.3 | Apache-2.0 | `tests/Berth.Tests` only: automated unit and UI test suite |",
            "| xunit.v3 | 4.0.3 | MIT | bundled in berth.exe |",
        )
        self.assertNotEqual(bad_xunit, text)
        errs = check_notice(bad_xunit, central, app_refs, test_only_refs)
        self.assertTrue(any("licence mismatch for xunit.v3" in e for e in errs))
        self.assertTrue(any("never claim shipping in berth.exe" in e for e in errs))

        # Missing bundled .NET runtime must fail.
        without_runtime = "\n".join(line for line in text.splitlines() if ".NET 11 Runtime" not in line)
        errs = check_notice(without_runtime, central, app_refs, test_only_refs)
        self.assertTrue(any(".NET 11 Runtime" in e for e in errs))

        # Changing any upstream runtime notice link to moving 'main' or removing it must fail.
        for repo in ("runtime", "wpf", "winforms"):
            pinned = f"https://github.com/dotnet/{repo}/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT"
            moving = f"https://github.com/dotnet/{repo}/blob/main/THIRD-PARTY-NOTICES.TXT"
            errs_main = check_notice(text.replace(pinned, moving), central, app_refs, test_only_refs)
            self.assertTrue(any(pinned in e for e in errs_main), f"Moving {repo} notice to main should fail")
            errs_removed = check_notice(text.replace(pinned, "https://example.org/"), central, app_refs, test_only_refs)
            self.assertTrue(any(pinned in e for e in errs_removed), f"Removing {repo} notice should fail")

        # Missing Pillow or Contributor Covenant must fail.
        for comp in ("Pillow", "Contributor Covenant"):
            stripped = "\n".join(line for line in text.splitlines() if comp not in line)
            errs = check_notice(stripped, central, app_refs, test_only_refs)
            self.assertTrue(any(comp in e for e in errs), f"Missing {comp} should have failed")

        # Listing a test-only package under Shipped in berth.exe must fail (anchored on section heading).
        lines = text.splitlines()
        shipped_idx = next(i for i, l in enumerate(lines) if l.strip() == f"## {SHIPPED_HEADING}")
        table_sep_idx = next(i for i in range(shipped_idx, len(lines)) if lines[i].strip().startswith("| ---"))
        lines.insert(table_sep_idx + 1, "| xunit.v3 | 4.0.3 | Apache-2.0 | `tests/Berth.Tests` only |")
        errs = check_notice("\n".join(lines), central, app_refs, test_only_refs)
        self.assertTrue(any("must not be listed under" in e and "xunit.v3" in e for e in errs))

    def test_changelog_010_extracts_cleanly_undated_and_dated_and_rejects_omissions(self) -> None:
        text = CHANGELOG_PATH.read_text(encoding="utf-8")
        self.assertEqual(check_changelog_010(text), [])

        # Confirm compatibility with the dated heading D99 T01 §3 creates.
        dated = text.replace("## 0.1.0", "## 0.1.0 - 2026-11-21", 1)
        self.assertEqual(check_changelog_010(dated), [])

        # Spoofed link destination with expected URL only in visible text must fail.
        spoofed = text.replace(
            "[NOTICE.md](https://github.com/rizonesoft/Berth/blob/v0.1.0/NOTICE.md)",
            "[https://github.com/rizonesoft/Berth/blob/v0.1.0/NOTICE.md](https://example.org/)",
        )
        self.assertNotEqual(spoofed, text)
        self.assertTrue(check_changelog_010(spoofed))

        # Removing any required summary or the NOTICE.md tag link must fail.
        for phrase in ("Automatic restore", "Command line", "NOTICE.md"):
            stripped = "\n".join(line for line in text.splitlines() if phrase not in line)
            errs = check_changelog_010(stripped)
            self.assertTrue(errs, f"Removing {phrase!r} should have failed changelog check")

    def test_readme_licence_section_links_notice_and_rejects_missing_or_misplaced_link(self) -> None:
        text = README_PATH.read_text(encoding="utf-8")
        self.assertEqual(check_readme_licence_links_notice(text), [])

        removed = text.replace("[NOTICE.md](NOTICE.md)", "[LICENSE](LICENSE)")
        self.assertTrue(check_readme_licence_links_notice(removed))

        misplaced = removed.replace("## What is the licence?", "[NOTICE.md](NOTICE.md)\n\n## What is the licence?")
        self.assertTrue(check_readme_licence_links_notice(misplaced))


if __name__ == "__main__":
    unittest.main()
