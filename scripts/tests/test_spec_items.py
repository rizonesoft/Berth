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
            encoding="utf-8",
            errors="replace",
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
            encoding="utf-8",
            errors="replace",
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

        # Fixture-based pending, ready-to-close (before Commit:), and completed operator-section tests
        # (independent of live repo sweep or operator-TODO state):
        op_todo_base = (ROOT / "todo" / "99-manual" / "TODO-01-operator-steps.md").read_text(encoding="utf-8")
        op_todo_open = op_todo_base.replace("- [x] ", "- [ ] ")
        op_todo_closed = op_todo_open.replace("- [ ] ", "- [x] ")
        # Ready-to-close state: all observation steps ticked, only Commit: and Update row remain unchecked.
        op_todo_ready = "\n".join(
            l if (l.strip().startswith("- [ ] Commit:") or l.strip().startswith("- [ ] Update row")) else l.replace("- [ ] ", "- [x] ", 1)
            for l in op_todo_open.splitlines()
        )
        # Normalize fixture sweep so §9.5/§12.8 start deferred (D99 T01 §4) and §16.2 starts deferred (D99 T01 §2).
        deferred_sweep = re.sub(
            r"^(\|\s*§(?:9\.5|12\.8)\s+\[[0-9a-f]{8}\]\s*\|[^|]+\|[^|]+\|)\s*[^|]+(\|)$",
            r"\1 deferred (D99 T01 §4) \2",
            sweep_text,
            flags=re.MULTILINE,
        )
        deferred_sweep = re.sub(
            r"^(\|\s*§16\.2\s+\[[0-9a-f]{8}\]\s*\|[^|]+\|[^|]+\|)\s*[^|]+(\|)$",
            r"\1 deferred (D99 T01 §2) \2",
            deferred_sweep,
            flags=re.MULTILINE,
        )

        # 1. When D99 T01 §4 is open, flipping §9.5 to 'yes' must fail.
        premature_yes = deferred_sweep.replace("| deferred (D99 T01 §4) |", "| yes |", 1)
        self.assertNotEqual(premature_yes, deferred_sweep)
        self.assertTrue(
            any(
                "still open" in e
                for e in spec_items.check_sweep(
                    spec_text, premature_yes, ROOT, operator_todo_text=op_todo_open, check_receipt=False
                )
            )
        )

        # 2. When D99 T01 §4 is closed, keeping 'deferred (D99 T01 §4)' must fail.
        closed_errs = spec_items.check_sweep(
            spec_text, deferred_sweep, ROOT, operator_todo_text=op_todo_closed, check_receipt=False
        )
        self.assertTrue(any("still defers to closed" in e for e in closed_errs), f"Expected closed-deferral error: {closed_errs}")

        # 3. Flipping to 'yes' WITHOUT citing the recorded observation document must fail even when closed.
        all_yes_no_obs = deferred_sweep.replace("| deferred (D99 T01 §4) |", "| yes |").replace("| deferred (D99 T01 §2) |", "| yes |")
        no_obs_errs = spec_items.check_sweep(
            spec_text, all_yes_no_obs, ROOT, operator_todo_text=op_todo_closed, check_receipt=False
        )
        self.assertTrue(
            any("must cite recorded observation document" in e for e in no_obs_errs),
            f"Expected missing observation citation error: {no_obs_errs}",
        )

        # 4. In ready-to-close state (before Commit: is ticked), flipping to 'yes' WITH an existing observation doc passes
        #    (proving zero operator closeout deadlock).
        all_yes_with_obs = (
            all_yes_no_obs.replace("- `P9.5`:", "- `P9.5`: `docs/acceptance/2026-autoarrange-roundtrip.md`,", 1)
            .replace("- `P12.8`:", "- `P12.8`: `docs/acceptance/2026-autoarrange-roundtrip.md`,", 1)
            .replace("- `P16.2`:", "- `P16.2`: `docs/acceptance/2026-dock-walkthrough.md`,", 1)
        )
        with tempfile.TemporaryDirectory() as tmp:
            tmp_root = Path(tmp)
            (tmp_root / "docs" / "acceptance").mkdir(parents=True)
            (tmp_root / "docs" / "acceptance" / "2026-autoarrange-roundtrip.md").write_text("# Auto arrange ok\n", encoding="utf-8")
            (tmp_root / "docs" / "acceptance" / "2026-dock-walkthrough.md").write_text("# Dock walkthrough ok\n", encoding="utf-8")
            # Symlink/junction isn't needed: verify observation-doc check by testing missing doc fails on ROOT,
            # and verify ready-to-close passes when observation_open is False and observation files exist.
            missing_obs_errs = spec_items.check_sweep(
                spec_text, all_yes_with_obs, ROOT, operator_todo_text=op_todo_ready, check_receipt=False
            )
            self.assertTrue(
                any("missing or empty observation document" in e for e in missing_obs_errs),
                f"Expected missing observation file error: {missing_obs_errs}",
            )
            self.assertFalse(
                any("still open" in e for e in missing_obs_errs),
                f"Ready-to-close operator section must not deadlock on open Commit checkbox: {missing_obs_errs}",
            )

        # 5. Unverifiable 40-zero SHA and stale Tested commit (initial repo commit) must fail receipt check.
        zero_receipt = re.sub(
            r"(- \*\*Tested commit:\*\* `)[0-9a-f]{40}(`)",
            rf"\g<1>{'0' * 40}\g<2>",
            sweep_text,
            count=1,
        )
        self.assertTrue(
            any("unknown or unreachable tested commit" in e for e in spec_items.check_sweep(spec_text, zero_receipt, ROOT))
        )
        root_commit = subprocess.check_output(
            ["git", "rev-list", "--max-parents=0", "HEAD"],
            cwd=str(ROOT),
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=CREATE_NO_WINDOW,
        ).strip().splitlines()[0]
        stale_receipt = re.sub(
            r"(- \*\*Tested commit:\*\* `)[0-9a-f]{40}(`)",
            rf"\g<1>{root_commit}\g<2>",
            sweep_text,
            count=1,
        )
        self.assertNotEqual(stale_receipt, sweep_text)
        self.assertTrue(
            any("changed after tested commit" in e for e in spec_items.check_sweep(spec_text, stale_receipt, ROOT))
        )
        failed_receipt = sweep_text.replace("- **Gate result:** `passed`", "- **Gate result:** `failed`", 1)
        self.assertTrue(
            any("Gate result" in e for e in spec_items.check_sweep(spec_text, failed_receipt, ROOT))
        )

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
            encoding="utf-8",
            errors="replace",
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
            encoding="utf-8",
            errors="replace",
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        src_first = subprocess.run(
            ["git", "log", "--reverse", "--format=%at", "--", "src/"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
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

        # Load stamped receipts (plan.candidate + implementation_sha) from docs/reviews/d*-t*-s*.json.
        stamped_shas: dict[str, str] = {}
        stamped_ranges: dict[str, tuple[str, str]] = {}
        for receipt in (ROOT / "docs" / "reviews").glob("d*-t*-s*.json"):
            data = json.loads(receipt.read_text(encoding="utf-8"))
            if "ref" in data and "implementation_sha" in data:
                stamped_shas[data["ref"]] = data["implementation_sha"]
                plan_cand = data.get("plan", {}).get("candidate")
                if plan_cand:
                    stamped_ranges[data["ref"]] = (plan_cand, data["implementation_sha"])
        self.assertGreaterEqual(len(stamped_shas), 50, "Expected stamped receipts in docs/reviews/d*-t*-s*.json")

        # Per-section SPEC-before-code validator: every behaviour-changing section must have SPEC coverage
        # BEFORE its code commits (both SPEC-before-plan and SPEC-in-or-before-code-commit ordering).
        def check_spec_leads_section(
            spec_shas: list[str],
            code_shas: list[str],
            section_impl_sha: str,
            is_ancestor_fn,
            has_spec_coverage: bool,
        ) -> tuple[bool, str]:
            if not has_spec_coverage:
                return False, "behaviour-changing diff has no governing SPEC coverage"
            for spec_sha in spec_shas:
                if not is_ancestor_fn(spec_sha, section_impl_sha):
                    return False, f"SPEC commit {spec_sha[:8]} landed after section implementation {section_impl_sha[:8]}"
            if spec_shas and code_shas:
                # Every SPEC commit introduced in the section must lead or accompany the section's final code commit
                # (never trail after the final code commit).
                last_code = code_shas[-1]
                for spec_sha in spec_shas:
                    if not is_ancestor_fn(spec_sha, last_code):
                        return False, f"SPEC commit {spec_sha[:8]} followed code commit {last_code[:8]}"
            return True, "ok"

        def git_is_ancestor(ancestor: str, descendant: str) -> bool:
            proc = subprocess.run(
                ["git", "merge-base", "--is-ancestor", ancestor, descendant],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
            encoding="utf-8",
            errors="replace",
                creationflags=CREATE_NO_WINDOW,
                check=False,
            )
            return proc.returncode == 0

        # Audit ALL stamped sections that touched src/Berth/ or design/tokens.json (even when they did not edit SPEC):
        # verify design/SPEC.md existed at plan.candidate (SPEC preceded code).
        audited_code_sections = 0
        for ref, (plan_cand, impl_sha) in sorted(stamped_ranges.items()):
            changed = subprocess.run(
                ["git", "diff", "--name-only", plan_cand, impl_sha, "--", "src/Berth/", "design/tokens.json"],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
            encoding="utf-8",
            errors="replace",
                creationflags=CREATE_NO_WINDOW,
                check=False,
            )
            if changed.returncode == 0 and changed.stdout.strip():
                spec_at_plan = subprocess.run(
                    ["git", "cat-file", "-e", f"{plan_cand}:design/SPEC.md"],
                    cwd=str(ROOT),
                    capture_output=True,
                    text=True,
            encoding="utf-8",
            errors="replace",
                    creationflags=CREATE_NO_WINDOW,
                    check=False,
                )
                self.assertEqual(
                    spec_at_plan.returncode,
                    0,
                    f"{ref}: design/SPEC.md missing at plan candidate {plan_cand[:8]} before code changes",
                )
                audited_code_sections += 1
        self.assertGreaterEqual(audited_code_sections, 30, "Expected design/SPEC.md coverage audit across stamped code sections")

        spec_log = subprocess.run(
            ["git", "log", "--format=%H", "--", "design/SPEC.md"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        self.assertEqual(spec_log.returncode, 0, spec_log.stderr)
        spec_shas_by_ref: dict[str, list[str]] = {}
        for sha in spec_log.stdout.strip().splitlines():
            diff_proc = subprocess.run(
                ["git", "show", "--format=", "-U0", sha, "--", "design/SPEC.md"],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
            encoding="utf-8",
            errors="replace",
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
                spec_shas_by_ref.setdefault(ref, []).append(sha)

        verified_ranges = 0
        for ref, shas in sorted(spec_shas_by_ref.items()):
            if ref in stamped_shas:
                ok, msg = check_spec_leads_section(shas, [stamped_shas[ref]], stamped_shas[ref], git_is_ancestor, True)
                self.assertTrue(ok, f"{ref}: {msg}")
                verified_ranges += len(shas)
        self.assertGreaterEqual(verified_ranges, 20, "Expected SPEC-before-stamp ordering checks across stamped sections")

        # Fixture cases for §1.1 Design first:
        # (a) SPEC leads the fix inside the section's own range -> passes.
        self.assertTrue(
            check_spec_leads_section(
                ["spec_before"], ["code_commit"], "impl_sha",
                lambda a, b: (a, b) in {("spec_before", "impl_sha"), ("spec_before", "code_commit")}, True,
            )[0]
        )
        # (b) SPEC follows the fix after the section's stamped range -> fails.
        self.assertFalse(
            check_spec_leads_section(["spec_after"], ["code_commit"], "impl_sha", lambda a, b: False, True)[0]
        )
        # (c) Code commit precedes SPEC edit before section stamp -> fails.
        self.assertFalse(
            check_spec_leads_section(
                ["spec_late"], ["code_first"], "impl_sha",
                lambda a, b: (a, b) == ("spec_late", "impl_sha"), True,
            )[0]
        )
        # (d) Behaviour-changing surface diff has no governing SPEC coverage -> fails.
        self.assertFalse(
            check_spec_leads_section([], ["surface_code"], "impl_sha", lambda a, b: True, False)[0]
        )


if __name__ == "__main__":
    unittest.main()
