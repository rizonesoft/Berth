"""Reads back the CI configuration D00 T01 §5 sets: the build workflow, the README badge, Dependabot and the
workflows `scripts/ci_status.py` watches. Standard library only, so the YAML is read by line and key."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / ".github" / "workflows" / "build.yml"
REPO = "https://github.com/rizonesoft/Berth"


def lines(path: Path) -> list[str]:
    return path.read_text(encoding="utf-8").splitlines()


def entries(path: Path) -> list[dict[str, str]]:
    """The `updates` entries of dependabot.yml as flat key-value maps (`labels` joined by commas)."""
    found: list[dict[str, str]] = []
    for line in lines(path):
        stripped = line.strip()
        if stripped.startswith("- package-ecosystem:"):
            found.append({"package-ecosystem": stripped.split(":", 1)[1].strip()})
        elif found and stripped.startswith("- ") and ":" not in stripped:
            found[-1]["labels"] = ",".join(filter(None, [found[-1].get("labels", ""), stripped[2:].strip()]))
        elif found and ":" in stripped and not stripped.endswith(":"):
            key, value = (part.strip() for part in stripped.split(":", 1))
            found[-1][key] = value
    return found


class BuildWorkflowTests(unittest.TestCase):
    def setUp(self) -> None:
        self.text = BUILD.read_text(encoding="utf-8")
        self.lines = [line.rstrip() for line in self.text.splitlines()]

    def assert_line(self, line: str) -> None:
        self.assertIn(line, self.lines, f"build.yml must hold the line {line!r}")

    def test_name_triggers_and_permissions(self) -> None:
        for line in (
            "name: build",
            "on:",
            "  push:",
            "    branches: [main]",
            "  pull_request:",
            "  workflow_dispatch:",
            "permissions:",
            "  contents: read",
        ):
            self.assert_line(line)

    def test_concurrency_is_per_ref_and_its_own(self) -> None:
        self.assert_line("  group: build-${{ github.ref }}")
        self.assert_line("  cancel-in-progress: ${{ github.event_name == 'pull_request' }}")

    def test_one_windows_job_with_a_timeout(self) -> None:
        self.assert_line("    runs-on: windows-latest")
        self.assert_line("    timeout-minutes: 30")
        self.assertEqual(sum(1 for line in self.lines if line.strip().startswith("runs-on:")), 1)

    def test_steps_in_order(self) -> None:
        steps = [line.strip() for line in self.lines if line.strip().startswith(("- uses:", "run:"))]
        self.assertEqual(
            steps,
            [
                "- uses: actions/checkout@v7",
                "- uses: actions/setup-dotnet@v6",
                "- uses: actions/setup-python@v7",
                "- uses: actions/setup-node@v7",
                "run: pwsh scripts/check-all.ps1",
            ],
        )
        self.assert_line("          fetch-depth: 0")
        self.assert_line("          global-json-file: global.json")
        self.assert_line("          python-version: '3.14'")
        self.assert_line("          node-version: lts/*")

    def test_nothing_can_turn_a_failure_green(self) -> None:
        self.assertIsNone(re.search(r"^\s*(-\s*)?if:", self.text, re.MULTILINE))
        self.assertNotIn("continue-on-error", self.text)


class BadgeTests(unittest.TestCase):
    def test_build_badge_comes_first_and_opens_build(self) -> None:
        badges = re.findall(r"\[!\[(\w+)\]\(([^)]+)\)\]\(([^)]+)\)", (ROOT / "README.md").read_text(encoding="utf-8"))
        self.assertGreaterEqual(len(badges), 2)
        self.assertEqual(
            badges[0],
            ("Build", f"{REPO}/actions/workflows/build.yml/badge.svg?branch=main", f"{REPO}/actions/workflows/build.yml"),
        )
        self.assertEqual(badges[1][0], "Plan")


class DependabotTests(unittest.TestCase):
    def test_nuget_and_actions_are_watched_weekly_with_the_label(self) -> None:
        found = {entry["package-ecosystem"]: entry for entry in entries(ROOT / ".github" / "dependabot.yml")}
        self.assertEqual(set(found), {"github-actions", "nuget"})
        for ecosystem in ("github-actions", "nuget"):
            self.assertEqual(found[ecosystem]["directory"], "/")
            self.assertEqual(found[ecosystem]["interval"], "weekly")
            self.assertEqual(found[ecosystem]["labels"], "dependencies")


class WatchedWorkflowTests(unittest.TestCase):
    def test_ci_json_names_exactly_build_and_plan(self) -> None:
        document = json.loads((ROOT / ".conclave" / "ci.json").read_text(encoding="utf-8"))
        self.assertEqual(document, {"workflows": ["build.yml", "plan.yml"]})


if __name__ == "__main__":
    unittest.main()
