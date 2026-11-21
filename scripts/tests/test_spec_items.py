"""Tests for scripts/spec-items.py and docs/acceptance/2026-spec-sweep.md."""

from __future__ import annotations

import importlib.util
import json
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
        tg_proc = subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "todo-graph.py"), "query", "plan"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        self.assertEqual(tg_proc.returncode, 0, tg_proc.stderr)
        plan_data = json.loads(tg_proc.stdout)
        valid_refs = {s["ref"] for s in plan_data["sections"]}
        ref_topics = {s["ref"]: s["title"] for s in plan_data["sections"]}
        self.assertGreater(len(valid_refs), 50)

        # Case-insensitive audit of every Clarified / Corrected / Added / Deviation note in §2..§20.
        dated_tag_re = re.compile(
            r"(?:(?:Clarified|Corrected|Added|Deviation)\s+\d{4}-\d{2}-\d{2}\s*\((D\d{2}\s+T\d{2}\s+§\d+)\)"
            r"|(?:clarified|corrected|added|deviation)\s+\d{4}-\d{2}-\d{2}(?:,|\s+in)\s*(D\d{2}\s+T\d{2}\s+§\d+))",
            re.IGNORECASE,
        )
        bare_note_re = re.compile(r"\b(?:clarified|corrected|added|deviation)\b", re.IGNORECASE)

        def validate_deviation_clause(line: str, known_refs: set[str], ref_topics: dict[str, str] | None = None) -> tuple[bool, str]:
            """Validate a Deviation note: requires ISO date, resolvable TODO ref, non-empty reason inside the deviation clause, and topic match."""
            m = re.search(
                r"\bDeviation\s+\d{4}-\d{2}-\d{2}\s*\((D\d{2}\s+T\d{2}\s+§\d+)\)\s*:\s*([^.\n]+)",
                line,
                re.IGNORECASE,
            )
            if not m:
                return False, "missing dated Deviation (DNN TNN §N): <reason> clause"
            ref, reason = m.group(1), m.group(2).strip()
            if len(reason) < 10:
                return False, "deviation reason is too short"
            if ref not in known_refs:
                return False, f"nonexistent closing TODO {ref}"
            if ref_topics is not None:
                topic = ref_topics.get(ref, "").lower()
                reason_words = [w for w in re.findall(r"[a-z]{4,}", reason.lower()) if w not in {"with", "that", "this", "from", "when", "will", "then"}]
                if not any(w in topic for w in reason_words):
                    return False, f"closing TODO {ref} does not address deviation topic"
            return True, ref

        body = spec_text.split("## 2. Brand and tokens", 1)[1]
        checked = 0
        referenced_in_spec: set[str] = set()
        for line_no, line in enumerate(body.splitlines(), start=1):
            if bare_note_re.search(line):
                matches = list(dated_tag_re.finditer(line))
                self.assertTrue(
                    matches,
                    f"SPEC note at offset line {line_no} is missing ISO date or DNN TNN §N reference: {line}",
                )
                for m in matches:
                    ref = m.group(1) or m.group(2)
                    self.assertIn(ref, valid_refs, f"SPEC note cites nonexistent TODO ref {ref}: {line}")
                    referenced_in_spec.add(ref)
                    checked += 1
                if re.search(r"\bdeviation\b", line, re.IGNORECASE):
                    ok, msg = validate_deviation_clause(line, valid_refs, ref_topics)
                    self.assertTrue(ok, f"Invalid SPEC deviation at offset line {line_no}: {msg}")
        self.assertGreaterEqual(checked, 15, "Expected all dated SPEC notes (including lowercase inline notes) to be audited")

        # Negative cases for §1.7 Deviation policy:
        # 1. Undated deviation fails.
        self.assertFalse(validate_deviation_clause("Some rule. Deviation: skipped.", valid_refs)[0])
        # 2. Reasonless deviation after an ordinary requirement sentence fails (preceding text does not count as reason).
        self.assertFalse(
            validate_deviation_clause("The window is 980x640 at 100% scale. Deviation 2026-11-21 (D07 T01 §5).", valid_refs)[0]
        )
        # 3. Nonexistent closing TODO fails.
        self.assertFalse(
            validate_deviation_clause("Deviation 2026-11-21 (D99 T99 §99): native DWM frame pending.", valid_refs)[0]
        )
        # 4. Unrelated closing TODO fails topic check.
        topics = {"D99 T01 §1": "social preview image on github repository"}
        self.assertFalse(
            validate_deviation_clause("Deviation 2026-11-21 (D99 T01 §1): auto arrange flag cannot clear.", valid_refs, topics)[0]
        )

        # §1.1 Design first: verify both initial SPEC-before-src order AND that every SPEC-editing commit
        # for a stamped section landed at or before that section's stamped SHA (never in a later range).
        spec_first = subprocess.run(
            ["git", "log", "--reverse", "--format=%at", "--", "design/SPEC.md"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        src_first = subprocess.run(
            ["git", "log", "--reverse", "--format=%at", "--", "src/"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        self.assertEqual(spec_first.returncode, 0, spec_first.stderr)
        self.assertEqual(src_first.returncode, 0, src_first.stderr)
        self.assertLessEqual(
            int(spec_first.stdout.strip().splitlines()[0]),
            int(src_first.stdout.strip().splitlines()[0]),
            "design/SPEC.md must precede src/ in repository history",
        )

        # Load stamped SHAs from docs/reviews/d*.json and confirm every SPEC commit naming a stamped ref
        # is an ancestor of (or equal to) that section's stamped SHA.
        stamped_shas: dict[str, str] = {}
        for receipt in (ROOT / "docs" / "reviews").glob("d*-t*-s*.json"):
            data = json.loads(receipt.read_text(encoding="utf-8"))
            if "ref" in data and "implementation_sha" in data:
                stamped_shas[data["ref"]] = data["implementation_sha"]
        self.assertGreaterEqual(len(stamped_shas), 50, "Expected stamped receipts in docs/reviews/d*-t*-s*.json")

        spec_log = subprocess.run(
            ["git", "log", "--format=%H", "--", "design/SPEC.md"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        self.assertEqual(spec_log.returncode, 0, spec_log.stderr)
        verified_ranges = 0
        for sha in spec_log.stdout.strip().splitlines():
            diff_proc = subprocess.run(
                ["git", "show", "--format=", "-U0", sha, "--", "design/SPEC.md"],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                creationflags=CREATE_NO_WINDOW,
                check=False,
            )
            added_refs = {
                (m.group(1) or m.group(2))
                for l in diff_proc.stdout.splitlines()
                if l.startswith("+") and not l.startswith("+++")
                for m in dated_tag_re.finditer(l[1:])
            }
            removed_refs = {
                (m.group(1) or m.group(2))
                for l in diff_proc.stdout.splitlines()
                if l.startswith("-") and not l.startswith("---")
                for m in dated_tag_re.finditer(l[1:])
            }
            target_refs = sorted(added_refs - removed_refs) or sorted(added_refs & removed_refs)
            for ref in target_refs:
                if ref in stamped_shas:
                    anc = subprocess.run(
                        ["git", "merge-base", "--is-ancestor", sha, stamped_shas[ref]],
                        cwd=str(ROOT),
                        capture_output=True,
                        text=True,
                        creationflags=CREATE_NO_WINDOW,
                        check=False,
                    )
                    self.assertEqual(
                        anc.returncode,
                        0,
                        f"SPEC commit {sha[:8]} for {ref} landed AFTER stamped commit {stamped_shas[ref][:8]}",
                    )
                    verified_ranges += 1
        self.assertGreaterEqual(verified_ranges, 20, "Expected SPEC-before-stamp ordering checks across stamped sections")

        # Also verify no unstamped surface or token commit exists outside a stamped section's range:
        # every commit touching design/tokens.json or src/Berth/Window must be an ancestor of at least one stamped SHA.
        surface_log = subprocess.run(
            ["git", "log", "--format=%H", "--", "design/tokens.json", "src/Berth/Window"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        self.assertEqual(surface_log.returncode, 0, surface_log.stderr)
        surface_commits = [s for s in surface_log.stdout.strip().splitlines() if s]
        self.assertGreater(len(surface_commits), 0)
        latest_stamped_sha = max(
            stamped_shas.values(),
            key=lambda s: int(
                subprocess.check_output(
                    ["git", "show", "-s", "--format=%ct", s],
                    cwd=str(ROOT),
                    text=True,
                    creationflags=CREATE_NO_WINDOW,
                ).strip()
            ),
        )
        for sha in surface_commits:
            anc = subprocess.run(
                ["git", "merge-base", "--is-ancestor", sha, latest_stamped_sha],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                creationflags=CREATE_NO_WINDOW,
                check=False,
            )
            self.assertEqual(
                anc.returncode,
                0,
                f"Surface or token commit {sha[:8]} landed outside all stamped sections (after {latest_stamped_sha[:8]})",
            )


if __name__ == "__main__":
    unittest.main()
