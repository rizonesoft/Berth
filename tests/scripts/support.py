"""Shared helpers for the project's script tests: a scratch folder and a bounded child process."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
import tomllib
from collections.abc import Mapping
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
CREATE_NO_WINDOW = 0x08000000


DEFAULT_CANDIDATE = Path(r"R:\scratch")


def scratch_root(
    environ: Mapping[str, str] = os.environ,
    local_file: Path = ROOT / ".conclave" / "local.toml",
    candidate: Path = DEFAULT_CANDIDATE,
) -> Path:
    """Where script tests put their temporary folders, the first that applies of: `BERTH_SCRATCH` (the session
    scratchpad, as the sibling projects' BOSUN_SCRATCH and OFFICE_SCRATCH); `paths.scratch` in
    `.conclave/local.toml`; `R:\\scratch` when it exists (AGENTS.md, "Working rules"); the system's temporary
    folder. A chosen BERTH_SCRATCH or paths.scratch folder is created; the candidate never is."""
    session = environ.get("BERTH_SCRATCH", "")
    if session:
        folder = Path(session)
        folder.mkdir(parents=True, exist_ok=True)
        return folder
    if local_file.is_file():
        with local_file.open("rb") as handle:
            configured = tomllib.load(handle).get("paths", {}).get("scratch", "")
        if configured:
            folder = Path(configured)
            folder.mkdir(parents=True, exist_ok=True)
            return folder
    if candidate.is_dir():
        return candidate
    return Path(tempfile.gettempdir())


def make_scratch(prefix: str, **where: Any) -> Path:
    """A new folder under `scratch_root(**where)`; remove it with `remove_scratch`."""
    return Path(tempfile.mkdtemp(prefix=prefix, dir=scratch_root(**where)))

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
