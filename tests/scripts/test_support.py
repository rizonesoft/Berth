"""Tests for the script tests' own support module."""

from __future__ import annotations

import subprocess
import sys
import time
import unittest
from pathlib import Path

import tempfile

from support import ROOT, make_scratch, remove_scratch, run_bounded, scratch_root

# A parent that starts a grandchild, records its PID, then waits on it for far longer than the test allows.
PARENT = (
    "import subprocess, sys; "
    "child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], creationflags=0x08000000); "
    "open(sys.argv[1], 'w').write(str(child.pid)); child.wait()"
)


def alive(pid: int) -> bool:
    found = subprocess.run(
        ["tasklist", "/FI", f"PID eq {pid}", "/NH"], capture_output=True, text=True, creationflags=0x08000000
    )
    return str(pid) in found.stdout


class SupportTests(unittest.TestCase):
    def test_timeout_stops_the_whole_tree(self) -> None:
        folder = make_scratch("berth-support-")
        self.addCleanup(remove_scratch, folder)
        pid_file = folder / "child.pid"
        with self.assertRaises(subprocess.TimeoutExpired):
            run_bounded([sys.executable, "-c", PARENT, str(pid_file)], timeout=3, cwd=folder)
        child = int(pid_file.read_text(encoding="utf-8"))
        deadline = time.monotonic() + 10
        while alive(child) and time.monotonic() < deadline:
            time.sleep(0.2)
        self.assertFalse(alive(child), f"grandchild {child} outlived its test")

    def test_scratch_folders_live_under_the_scratch_root_and_are_removed(self) -> None:
        folder = make_scratch("berth-support-")
        self.assertEqual(folder.parent.resolve(), scratch_root().resolve())
        self.assertFalse(folder.resolve().is_relative_to(ROOT.resolve()))
        remove_scratch(folder)
        self.assertFalse(folder.exists())


class ScratchRootTests(unittest.TestCase):
    def setUp(self) -> None:
        self.base = Path(tempfile.mkdtemp(prefix="berth-scratch-root-"))
        self.addCleanup(remove_scratch, self.base)
        self.candidate = self.base / "candidate"
        self.local = self.base / "local.toml"

    def configure(self, folder: Path) -> None:
        self.local.write_text(f'[paths]\nscratch = "{folder.as_posix()}"\n', encoding="utf-8")

    def test_session_scratchpad_wins(self) -> None:
        session = self.base / "session"
        self.configure(self.base / "configured")
        self.candidate.mkdir()
        made = make_scratch("x-", environ={"BERTH_SCRATCH": str(session)}, local_file=self.local, candidate=self.candidate)
        self.assertEqual(made.parent, session)

    def test_configured_folder_is_created_and_wins_over_the_candidate(self) -> None:
        configured = self.base / "not" / "yet" / "there"
        self.configure(configured)
        self.candidate.mkdir()
        made = make_scratch("x-", environ={}, local_file=self.local, candidate=self.candidate)
        self.assertEqual(made.parent, configured)
        self.assertTrue(made.is_dir())

    def test_existing_candidate_wins_without_configuration(self) -> None:
        self.candidate.mkdir()
        self.assertEqual(scratch_root(environ={}, local_file=self.local, candidate=self.candidate), self.candidate)

    def test_system_temporary_folder_last_and_the_candidate_is_never_created(self) -> None:
        result = scratch_root(environ={}, local_file=self.local, candidate=self.candidate)
        self.assertEqual(result, Path(tempfile.gettempdir()))
        self.assertFalse(self.candidate.exists())

    def test_production_defaults(self) -> None:
        # Expected without asking scratch_root: R:\scratch when this machine has it, else the temporary folder.
        expected = Path(r"R:\scratch") if Path(r"R:\scratch").is_dir() else Path(tempfile.gettempdir())
        made = make_scratch("berth-defaults-", environ={}, local_file=self.base / "missing.toml")
        self.addCleanup(remove_scratch, made)
        self.assertEqual(made.parent, expected)


if __name__ == "__main__":
    unittest.main()
