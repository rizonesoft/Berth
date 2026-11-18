"""Shared helpers for the project's script tests: a scratch folder and a bounded child process."""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CREATE_NO_WINDOW = 0x08000000


def scratch_root() -> Path:
    """`paths.scratch` from `.conclave/local.toml` when set, else the operating system's temporary folder."""
    local = ROOT / ".conclave" / "local.toml"
    if local.is_file():
        with local.open("rb") as handle:
            configured = tomllib.load(handle).get("paths", {}).get("scratch", "")
        if configured:
            folder = Path(configured)
            folder.mkdir(parents=True, exist_ok=True)
            return folder
    return Path(tempfile.gettempdir())


def make_scratch(prefix: str) -> Path:
    """A new folder under `scratch_root()`; remove it with `remove_scratch`."""
    return Path(tempfile.mkdtemp(prefix=prefix, dir=scratch_root()))


def remove_scratch(folder: Path) -> None:
    """Deletes a scratch folder, letting any failure surface instead of leaving the folder behind unseen."""
    if folder.exists():
        shutil.rmtree(folder)


def run_bounded(
    command: list[str], *, timeout: float, cwd: Path, env: dict[str, str] | None = None
) -> subprocess.CompletedProcess[str]:
    """Runs `command` with no console window, and stops its whole process tree when it outlives `timeout` or the
    caller is interrupted, never only the direct child (AGENTS.md)."""
    process = subprocess.Popen(
        command,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    try:
        out, err = process.communicate(timeout=timeout)
    finally:
        if process.poll() is None:
            subprocess.run(
                ["taskkill", "/T", "/F", "/PID", str(process.pid)],
                capture_output=True,
                creationflags=CREATE_NO_WINDOW,
                check=False,
            )
            process.communicate()
    return subprocess.CompletedProcess(process.args, process.returncode, out, err)
