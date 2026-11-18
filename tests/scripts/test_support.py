"""Tests for the script tests' own support module."""

from __future__ import annotations

import subprocess
import sys
import time
import unittest
from pathlib import Path

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


if __name__ == "__main__":
    unittest.main()
