"""The review prompts a reviewer actually receives say that an open `Commit:` item is not a gap.

The sentence lives on each template's "TODO section" line, so `scripts/review-prompt.py` keeps it for a section
review and drops it from a review that names no section. These tests read the generated prompts, never the
templates: text above a template's `---` separator is not sent.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SENTENCE = (
    "Its `- [ ] Commit:` item and its Implementation Order row stay open until `stamp` ticks them after the last"
    " review, so an open `Commit:` item is not a gap."
)
REF = "D00 T01 §1"
FINDINGS = "docs/reviews/00-workspace/D00-T01-s3.md"
TIMEOUT_SECONDS = 60
CREATE_NO_WINDOW = 0x08000000


class ReviewPromptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = Path(tempfile.mkdtemp(prefix="berth-review-prompts-"))
        self.addCleanup(shutil.rmtree, self.folder, ignore_errors=True)
        self.gates = self.folder / "gates.txt"
        self.gates.write_text("gate output\n", encoding="utf-8")

    def generate(self, kind: str, *extra: str) -> str:
        out = self.folder / f"{kind}.md"
        command = [
            sys.executable,
            str(ROOT / "scripts" / "review-prompt.py"),
            "--kind",
            kind,
            "--candidate",
            "HEAD",
            "--governing",
            "AGENTS.md",
            "--out",
            str(out),
            *extra,
        ]
        result = subprocess.run(
            command,
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=TIMEOUT_SECONDS,
            creationflags=CREATE_NO_WINDOW if sys.platform == "win32" else 0,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return out.read_text(encoding="utf-8")

    def assert_once(self, prompt: str) -> None:
        self.assertEqual(prompt.count(SENTENCE), 1, "the generated prompt must carry the Commit rule exactly once")

    def test_plan_prompt(self) -> None:
        self.assert_once(self.generate("plan", "--ref", REF))

    def test_round_prompt_for_a_section(self) -> None:
        self.assert_once(self.generate("round", "--ref", REF, "--purpose", "x"))

    def test_round_prompt_without_a_section_says_nothing_about_stamps(self) -> None:
        self.assertNotIn(SENTENCE, self.generate("round", "--purpose", "x"))

    def test_completion_prompt(self) -> None:
        self.assert_once(self.generate("completion", "--ref", REF, "--purpose", "x"))

    def test_reconciliation_prompt(self) -> None:
        self.assert_once(
            self.generate(
                "reconciliation",
                "--ref",
                REF,
                "--failed",
                "HEAD",
                "--findings",
                FINDINGS,
                "--gates",
                str(self.gates),
            )
        )


if __name__ == "__main__":
    unittest.main()
