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


def _load_extract_section():
    spec = importlib.util.spec_from_file_location("changelog_section", ROOT / "scripts" / "changelog-section.py")
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.extract_section


extract_changelog_section = _load_extract_section()

SHIPPED_HEADING = "Shipped in berth.exe"
REPO_ONLY_HEADING = "Repository and test suite (not shipped in berth.exe)"


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
        if not lic:
            errors.append(f"missing licence for {pkg}")
        if not usage:
            errors.append(f"missing usage description for {pkg}")
        if pkg in test_only_refs and pkg in shipped:
            errors.append(f"test-only package {pkg} must not be listed under '{SHIPPED_HEADING}'")
        if pkg in app_refs and pkg not in shipped:
            errors.append(f"app package {pkg} must be listed under '{SHIPPED_HEADING}'")

    for pkg in sorted(app_refs | test_only_refs):
        if pkg not in all_entries:
            errors.append(f"referenced package missing from NOTICE.md: {pkg}")

    # Check required non-NuGet components, their licences, and their usage descriptions.
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

    if "Contributor Covenant" not in repo_only:
        errors.append("missing Contributor Covenant entry in NOTICE.md")
    else:
        _, lic, usage = repo_only["Contributor Covenant"]
        if "CC BY 4.0" not in lic:
            errors.append(f"unexpected licence for Contributor Covenant: {lic}")
        if "CODE_OF_CONDUCT.md" not in usage:
            errors.append("Contributor Covenant usage must reference CODE_OF_CONDUCT.md")

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
        "https://github.com/rizonesoft/Berth/blob/v0.1.0/NOTICE.md",
    )
    for phrase in required_phrases:
        if phrase not in section:
            errors.append(f"0.1.0 changelog section missing required content: {phrase}")
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

        # Missing bundled .NET runtime must fail.
        without_runtime = "\n".join(line for line in text.splitlines() if ".NET 11 Runtime" not in line)
        errs = check_notice(without_runtime, central, app_refs, test_only_refs)
        self.assertTrue(any(".NET 11 Runtime" in e for e in errs))

        # Missing Contributor Covenant must fail.
        without_coc = "\n".join(line for line in text.splitlines() if "Contributor Covenant" not in line)
        errs = check_notice(without_coc, central, app_refs, test_only_refs)
        self.assertTrue(any("Contributor Covenant" in e for e in errs))

        # Listing a test-only package under Shipped in berth.exe must fail.
        misclassified = text.replace(
            "| .NET 11 Windows Desktop Runtime | 11.0 | MIT | Bundled for WPF main window and Windows Forms tray icon in `berth.exe` |",
            "| .NET 11 Windows Desktop Runtime | 11.0 | MIT | Bundled for WPF main window and Windows Forms tray icon in `berth.exe` |\n"
            "| xunit.v3 | 4.0.3 | Apache-2.0 | Shipped in berth.exe |",
        )
        errs = check_notice(misclassified, central, app_refs, test_only_refs)
        self.assertTrue(any("must not be listed under" in e and "xunit.v3" in e for e in errs))

    def test_changelog_010_extracts_cleanly_undated_and_dated_and_rejects_omissions(self) -> None:
        text = CHANGELOG_PATH.read_text(encoding="utf-8")
        self.assertEqual(check_changelog_010(text), [])

        # Confirm compatibility with the dated heading D99 T01 §3 creates.
        dated = text.replace("## 0.1.0", "## 0.1.0 - 2026-11-21", 1)
        self.assertEqual(check_changelog_010(dated), [])

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
