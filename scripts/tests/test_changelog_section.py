"""Tests for scripts/changelog-section.py, scripts/create-draft-release.py and .github/workflows/release.yml."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHANGELOG_SCRIPT = ROOT / "scripts" / "changelog-section.py"
RELEASE_SCRIPT = ROOT / "scripts" / "create-draft-release.py"
RELEASE_WORKFLOW = ROOT / ".github" / "workflows" / "release.yml"
CI_CONFIG = ROOT / ".conclave" / "ci.json"

SAMPLE_CHANGELOG = textwrap.dedent(
    """\
    # Changelog

    ## Unreleased

    ### Added

    - Something not shipped yet.

    ## 0.2.0 - 2026-12-01

    ### Added

    - Second release feature.

    ## 0.1.0

    ### Added

    - First release feature.
    - Another item.

    ## [0.0.1] - 2026-11-01

    ### Added

    - Prototype.

    ## 0.0.0

    """
)


class ChangelogSectionTests(unittest.TestCase):
    def _run(self, version: str, changelog_text: str) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "CHANGELOG.md"
            path.write_text(changelog_text, encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(CHANGELOG_SCRIPT), version, "--changelog", str(path)],
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )

    def test_extracts_undated_version_section_without_adjacent_versions(self) -> None:
        proc = self._run("0.1.0", SAMPLE_CHANGELOG)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn("- First release feature.", proc.stdout)
        self.assertIn("- Another item.", proc.stdout)
        self.assertNotIn("Second release feature", proc.stdout)
        self.assertNotIn("Prototype", proc.stdout)
        self.assertNotIn("Something not shipped yet", proc.stdout)

    def test_extracts_dated_and_bracketed_version_headings(self) -> None:
        dated = self._run("0.2.0", SAMPLE_CHANGELOG)
        self.assertEqual(dated.returncode, 0, dated.stderr)
        self.assertIn("- Second release feature.", dated.stdout)
        self.assertNotIn("First release feature", dated.stdout)

        bracketed = self._run("0.0.1", SAMPLE_CHANGELOG)
        self.assertEqual(bracketed.returncode, 0, bracketed.stderr)
        self.assertIn("- Prototype.", bracketed.stdout)
        self.assertNotIn("First release feature", bracketed.stdout)

    def test_fenced_h2_inside_section_does_not_truncate_notes(self) -> None:
        changelog = textwrap.dedent(
            """\
            ## 0.1.0

            ### Added

            ```markdown
            ## Example heading inside fence
            ```

            - Trailing release bullet.

            ## 0.0.1

            - Older release.
            """
        )
        proc = self._run("0.1.0", changelog)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn("## Example heading inside fence", proc.stdout)
        self.assertIn("- Trailing release bullet.", proc.stdout)
        self.assertNotIn("Older release", proc.stdout)

    def test_missing_or_empty_version_exits_nonzero(self) -> None:
        missing = self._run("9.9.9", SAMPLE_CHANGELOG)
        self.assertNotEqual(missing.returncode, 0)
        self.assertIn("not found", missing.stderr)

        empty = self._run("0.0.0", SAMPLE_CHANGELOG)
        self.assertNotEqual(empty.returncode, 0)
        self.assertIn("empty", empty.stderr)


class CreateDraftReleaseTests(unittest.TestCase):
    def _setup_fixture(self, tmp: Path, mode: str) -> tuple[Path, Path, Path, Path, Path]:
        props = tmp / "Directory.Build.props"
        props.write_text(
            "<Project><PropertyGroup><LangVersion>latest</LangVersion><Version>0.1.0</Version></PropertyGroup></Project>\n",
            encoding="utf-8",
        )
        changelog = tmp / "CHANGELOG.md"
        changelog.write_text(SAMPLE_CHANGELOG, encoding="utf-8")
        artifacts = tmp / "artifacts"
        artifacts.mkdir()
        exe = artifacts / "berth.exe"
        exe.write_bytes(b"fake-berth-binary")
        digest = hashlib.sha256(exe.read_bytes()).hexdigest().lower()
        (artifacts / "berth.exe.sha256").write_text(f"{digest}  berth.exe\n", encoding="utf-8")

        log_path = tmp / "gh-calls.jsonl"
        shim_py = tmp / "gh_shim.py"
        shim_py.write_text(
            textwrap.dedent(
                f"""\
                import json, sys
                from pathlib import Path
                args = sys.argv[1:]
                with open({str(log_path)!r}, "a", encoding="utf-8") as fh:
                    fh.write(json.dumps(args) + "\\n")
                mode = {mode!r}
                state_file = Path({str(tmp / 'gh-state.json')!r})
                if args[:2] == ["release", "view"]:
                    if state_file.exists():
                        print(state_file.read_text(encoding="utf-8"))
                        sys.exit(0)
                    if mode == "existing-published":
                        print(json.dumps({{"isDraft": False, "assets": []}}))
                        sys.exit(0)
                    if mode == "existing-draft":
                        print(json.dumps({{"isDraft": True, "assets": [{{"name": "berth.exe"}}]}}))
                        sys.exit(0)
                    sys.exit(1)
                if args[:2] == ["release", "create"]:
                    state_file.write_text(json.dumps({{"isDraft": True, "assets": [{{"name": "berth.exe"}}, {{"name": "berth.exe.sha256"}}]}}), encoding="utf-8")
                    sys.exit(0)
                if args[:2] == ["release", "edit"]:
                    sys.exit(0)
                if args[:2] == ["release", "upload"]:
                    state_file.write_text(json.dumps({{"isDraft": True, "assets": [{{"name": "berth.exe"}}, {{"name": "berth.exe.sha256"}}]}}), encoding="utf-8")
                    sys.exit(0)
                sys.exit(2)
                """
            ),
            encoding="utf-8",
        )
        shim_cmd = tmp / "gh.cmd"
        shim_cmd.write_text(f'@"{sys.executable}" "{shim_py}" %*\r\n', encoding="utf-8")
        return props, changelog, artifacts, shim_cmd, log_path

    def test_matching_tag_creates_draft_with_exact_flags_and_assets(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            props, changelog, artifacts, gh_cmd, log_path = self._setup_fixture(Path(tmp), "fresh")
            proc = subprocess.run(
                [
                    sys.executable,
                    str(RELEASE_SCRIPT),
                    "--tag",
                    "v0.1.0",
                    "--props",
                    str(props),
                    "--changelog",
                    str(changelog),
                    "--artifacts",
                    str(artifacts),
                    "--gh",
                    str(gh_cmd),
                ],
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )
            self.assertEqual(proc.returncode, 0, proc.stderr)
            calls = [json.loads(line) for line in log_path.read_text(encoding="utf-8").splitlines()]
            create_calls = [c for c in calls if c[:2] == ["release", "create"]]
            self.assertEqual(len(create_calls), 1)
            cmd = create_calls[0]
            self.assertIn("v0.1.0", cmd)
            self.assertIn("--draft", cmd)
            self.assertIn("--verify-tag", cmd)
            self.assertIn("Berth 0.1.0", cmd)
            self.assertIn("--notes-file", cmd)
            self.assertIn(str(artifacts / "berth.exe"), cmd)
            self.assertIn(str(artifacts / "berth.exe.sha256"), cmd)

    def test_mismatched_tag_refuses_without_calling_gh(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            props, changelog, artifacts, gh_cmd, log_path = self._setup_fixture(Path(tmp), "fresh")
            proc = subprocess.run(
                [
                    sys.executable,
                    str(RELEASE_SCRIPT),
                    "--tag",
                    "v0.2.0",
                    "--props",
                    str(props),
                    "--changelog",
                    str(changelog),
                    "--artifacts",
                    str(artifacts),
                    "--gh",
                    str(gh_cmd),
                ],
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )
            self.assertNotEqual(proc.returncode, 0)
            self.assertFalse(log_path.exists())

    def test_existing_published_release_is_refused_and_existing_draft_is_repaired(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            props, changelog, artifacts, gh_cmd, _ = self._setup_fixture(Path(tmp), "existing-published")
            pub = subprocess.run(
                [
                    sys.executable,
                    str(RELEASE_SCRIPT),
                    "--tag",
                    "v0.1.0",
                    "--props",
                    str(props),
                    "--changelog",
                    str(changelog),
                    "--artifacts",
                    str(artifacts),
                    "--gh",
                    str(gh_cmd),
                ],
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )
            self.assertNotEqual(pub.returncode, 0)
            self.assertIn("already published", pub.stderr)

        with tempfile.TemporaryDirectory() as tmp:
            props, changelog, artifacts, gh_cmd, log_path = self._setup_fixture(Path(tmp), "existing-draft")
            rerun = subprocess.run(
                [
                    sys.executable,
                    str(RELEASE_SCRIPT),
                    "--tag",
                    "v0.1.0",
                    "--props",
                    str(props),
                    "--changelog",
                    str(changelog),
                    "--artifacts",
                    str(artifacts),
                    "--gh",
                    str(gh_cmd),
                ],
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )
            self.assertEqual(rerun.returncode, 0, rerun.stderr)
            calls = [json.loads(line) for line in log_path.read_text(encoding="utf-8").splitlines()]
            self.assertTrue(any(c[:2] == ["release", "edit"] and "--draft" in c for c in calls))
            upload_calls = [c for c in calls if c[:2] == ["release", "upload"]]
            self.assertEqual(len(upload_calls), 1)
            self.assertNotIn("--clobber", upload_calls[0])
            self.assertIn(str(artifacts / "berth.exe.sha256"), upload_calls[0])
            self.assertNotIn(str(artifacts / "berth.exe"), upload_calls[0])


class ReleaseWorkflowStructureTests(unittest.TestCase):
    def test_release_workflow_triggers_guards_token_and_ci_json_exclusion(self) -> None:
        text = RELEASE_WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("name: release", text)
        self.assertIn("'v[0-9]+.[0-9]+.[0-9]+'", text)
        self.assertIn("workflow_dispatch:", text)
        self.assertIn("contents: write", text)
        self.assertIn("runs-on: windows-latest", text)
        self.assertIn("pwsh scripts/check-all.ps1", text)
        self.assertIn("pwsh scripts/publish.ps1", text)
        self.assertIn("artifacts/berth.exe", text)
        self.assertIn("artifacts/berth.exe.sha256", text)
        self.assertIn("if: github.event_name == 'push' && startsWith(github.ref, 'refs/tags/')", text)
        self.assertIn("GH_TOKEN: ${{ github.token }}", text)

        ci_data = json.loads(CI_CONFIG.read_text(encoding="utf-8"))
        self.assertEqual(ci_data["workflows"], ["build.yml", "plan.yml"])
        self.assertNotIn("release.yml", ci_data["workflows"])


if __name__ == "__main__":
    unittest.main()
