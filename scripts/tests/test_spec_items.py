"""Tests for scripts/spec-items.py and docs/acceptance/2026-spec-sweep.md."""

from __future__ import annotations

import importlib.util
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC_PATH = ROOT / "design" / "SPEC.md"
SWEEP_PATH = ROOT / "docs" / "acceptance" / "2026-spec-sweep.md"
SCRIPT_PATH = ROOT / "scripts" / "spec-items.py"
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _load_spec_items():
    spec = importlib.util.spec_from_file_location("spec_items", SCRIPT_PATH)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    sys.modules["spec_items"] = mod
    spec.loader.exec_module(mod)
    return mod


spec_items = _load_spec_items()


class SpecItemsTests(unittest.TestCase):
    def test_extracts_bullets_table_rows_normative_prose_and_schema(self) -> None:
        items = spec_items.extract_spec_items(SPEC_PATH.read_text(encoding="utf-8"))
        kinds = {item.kind for item in items}
        self.assertEqual(kinds, {"bullet", "table-row", "prose", "schema"})
        sections = {item.section for item in items}
        self.assertEqual(sections, set(range(1, 21)))

    def test_committed_sweep_passes_check_cli(self) -> None:
        proc = subprocess.run(
            [sys.executable, str(SCRIPT_PATH), "--check", str(SWEEP_PATH)],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        self.assertEqual(proc.returncode, 0, f"spec-items --check failed:\n{proc.stderr}\n{proc.stdout}")
        self.assertIn("spec-items: ok", proc.stdout)

    def test_adding_spec_bullet_without_row_exits_one(self) -> None:
        spec_text = SPEC_PATH.read_text(encoding="utf-8")
        modified_spec = spec_text.replace(
            "## 20. Out of scope",
            "- Extra unproven requirement added to section 19.\n\n## 20. Out of scope",
            1,
        )
        self.assertNotEqual(modified_spec, spec_text)
        with tempfile.TemporaryDirectory() as tmp:
            tmp_spec = Path(tmp) / "SPEC.md"
            tmp_spec.write_text(modified_spec, encoding="utf-8")
            proc = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), "--spec", str(tmp_spec), "--check", str(SWEEP_PATH)],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                creationflags=CREATE_NO_WINDOW,
                check=False,
            )
            self.assertEqual(proc.returncode, 1, f"Expected exit 1 for unmapped SPEC bullet: {proc.stdout}")
            self.assertIn("missing sweep row", proc.stderr)

    def test_modifying_existing_spec_row_without_updating_sweep_fails(self) -> None:
        spec_text = SPEC_PATH.read_text(encoding="utf-8")
        sweep_text = SWEEP_PATH.read_text(encoding="utf-8")
        modified_spec = spec_text.replace("desktop unreachable |", "desktop unreachable, loading |", 1)
        self.assertNotEqual(modified_spec, spec_text)
        errors = spec_items.check_sweep(modified_spec, sweep_text, ROOT)
        self.assertTrue(any("changed since sweep was written" in e for e in errors), f"Unexpected errors: {errors}")

        # Modifying §1 fidelity tolerance or §20 exclusions must also fail.
        mod_s1 = spec_text.replace("within 2 px at 100% scale", "within 4 px at 100% scale", 1)
        self.assertTrue(any("§1.6" in e for e in spec_items.check_sweep(mod_s1, sweep_text, ROOT)))
        mod_s20 = spec_text.replace("and telemetry or any network use.", "telemetry, network use, and themes.", 1)
        self.assertTrue(any("§20.1" in e for e in spec_items.check_sweep(mod_s20, sweep_text, ROOT)))

    def test_rejects_commented_table_tbd_proof_wrong_owner_duplicate_and_missing_test(self) -> None:
        spec_text = SPEC_PATH.read_text(encoding="utf-8")
        sweep_text = SWEEP_PATH.read_text(encoding="utf-8")

        # Commenting out the sweep table (closed or unclosed) must fail.
        commented = sweep_text.replace("| Ref | Requirement |", "<!--\n| Ref | Requirement |", 1).replace(
            "## Operator-owned", "-->\n\n## Operator-owned", 1
        )
        self.assertTrue(any("missing sweep row" in e for e in spec_items.check_sweep(spec_text, commented, ROOT)))

        unclosed_comment = "<!--\n" + sweep_text
        unclosed_errs = spec_items.check_sweep(spec_text, unclosed_comment, ROOT)
        self.assertTrue(any("unclosed" in e for e in unclosed_errs))
        self.assertTrue(any("missing sweep row" in e for e in unclosed_errs))

        # Blank Requirement description cell must fail.
        blank_desc = sweep_text.replace("| Design first: spec leads code |", "|  |", 1)
        self.assertNotEqual(blank_desc, sweep_text)
        self.assertTrue(any("empty Requirement description" in e for e in spec_items.check_sweep(spec_text, blank_desc, ROOT)))

        # Prematurely flipping an open operator-deferred row (§9.5) to 'yes' must fail.
        premature_yes = sweep_text.replace("| deferred (D99 T01 §4) |", "| yes |", 1)
        self.assertNotEqual(premature_yes, sweep_text)
        self.assertTrue(any("still open" in e for e in spec_items.check_sweep(spec_text, premature_yes, ROOT)))

        # Citing a non-test C# method (like DesktopCollection.Dispose) must fail.
        non_test_method = sweep_text.replace(
            "EmbeddedTrayIconsLoadFromAssemblyAndContain16PxFrame",
            "Dispose",
            1,
        ).replace(
            "tests/Berth.Tests/App/TrayIconTests.cs::Dispose",
            "tests/Berth.Tests/DesktopCollection.cs::Dispose",
            1,
        )
        self.assertNotEqual(non_test_method, sweep_text)
        self.assertTrue(any("missing test Dispose" in e for e in spec_items.check_sweep(spec_text, non_test_method, ROOT)))

        # Replacing a row's proof with 'TBD' must fail.
        with_tbd = sweep_text.replace("`P2.1`", "TBD", 1)
        self.assertNotEqual(with_tbd, sweep_text)
        self.assertTrue(any("must cite at least one" in e for e in spec_items.check_sweep(spec_text, with_tbd, ROOT)))

        # Status 'no' must fail.
        with_no = sweep_text.replace("| yes |", "| no |", 1)
        self.assertTrue(any("invalid status" in e for e in spec_items.check_sweep(spec_text, with_no, ROOT)))

        # Assigning Auto Arrange (§9.5) to unrelated D99 T01 §1 (social preview) must fail.
        wrong_owner = sweep_text.replace("| deferred (D99 T01 §4) |", "| deferred (D99 T01 §1) |", 1)
        self.assertNotEqual(wrong_owner, sweep_text)
        self.assertTrue(any("unrelated D99 T01 §1" in e for e in spec_items.check_sweep(spec_text, wrong_owner, ROOT)))

        # Duplicate row inside the sweep table must fail.
        lines = sweep_text.splitlines()
        first_idx = next(i for i, l in enumerate(lines) if l.startswith("| §2.1 "))
        dup_lines = list(lines)
        dup_lines.insert(first_idx + 1, lines[first_idx])
        self.assertTrue(any("duplicate sweep row" in e for e in spec_items.check_sweep(spec_text, "\n".join(dup_lines), ROOT)))

        # Missing test symbol in catalog must fail.
        bad_sym = sweep_text.replace("EmbeddedTrayIconsLoadFromAssemblyAndContain16PxFrame", "DeletedTestMethod", 1)
        self.assertNotEqual(bad_sym, sweep_text)
        self.assertTrue(any("missing test DeletedTestMethod" in e for e in spec_items.check_sweep(spec_text, bad_sym, ROOT)))

    def test_spec_policy_dated_clarifications_and_deviations_have_dates_and_todo_refs(self) -> None:
        spec_text = SPEC_PATH.read_text(encoding="utf-8")
        # Every Clarified / Corrected / Added / Deviation note in §2..§20 must carry an ISO date and a DNN TNN §N ref.
        dated_note_re = re.compile(r"\b(?:Clarified|Corrected|Added|Deviation)\s+\d{4}-\d{2}-\d{2}\b[^.\n]*\bD\d{2}\s+T\d{2}\s+§\d+\b")
        bare_note_re = re.compile(r"\b(?:Clarified|Corrected|Added|Deviation)\b")
        body = spec_text.split("## 2. Brand and tokens", 1)[1]
        checked = 0
        for line_no, line in enumerate(body.splitlines(), start=1):
            if bare_note_re.search(line):
                self.assertRegex(
                    line,
                    dated_note_re,
                    f"SPEC note at offset line {line_no} is missing ISO date or DNN TNN §N reference: {line}",
                )
                checked += 1
        self.assertGreaterEqual(checked, 10, "Expected dated SPEC clarification/correction notes to be audited")

        # Negative case: an undated deviation note fails the policy pattern.
        self.assertIsNone(dated_note_re.search("Deviation: skipped without date or owner."))


if __name__ == "__main__":
    unittest.main()
