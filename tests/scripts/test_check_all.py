"""Tests for scripts/check-all.ps1.

The real script runs against shim programs on a private PATH: `python`, `node`, `dotnet` and (when a test wants it)
`bosun` are `.cmd` files that append their name and arguments to a log and exit with a code a test chooses. So the
commands, their arguments and their order are what the script actually started, never what it says it started.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "check-all.ps1"
_PWSH = shutil.which("pwsh")
TIMEOUT_SECONDS = 120
CREATE_NO_WINDOW = 0x08000000

STEPS = [
    "plan-validate",
    "plan-current",
    "tooling-tests",
    "script-tests",
    "agent-tiers",
    "build",
    "test",
]

EXPECTED_CALLS = [
    ["python", "scripts/todo-graph.py", "validate"],
    ["python", "scripts/todo-graph.py", "plan", "--check"],
    ["python", "-m", "unittest", "discover", "-s", "scripts/tests"],
    ["python", "-m", "unittest", "discover", "-s", "tests/scripts"],
    ["node", "scripts/verify-agent-tiers.mjs"],
    ["dotnet", "build", "Berth.slnx", "-c", "Release"],
    ["dotnet", "test", "--solution", "Berth.slnx", "-c", "Release", "--no-build"],
]

SHIM = textwrap.dedent(
    """\
    import json, os, shutil, subprocess, sys
    name, args = sys.argv[1], sys.argv[2:]
    with open(os.environ["CHECK_SHIM_LOG"], "a", encoding="utf-8") as log:
        log.write(json.dumps({"cwd": os.getcwd(), "call": [name] + args}) + "\\n")
    if name == "bosun":
        assert args[:2] == ["run", "--"], args
        wrapped = shutil.which(args[2])
        sys.exit(subprocess.call([wrapped] + args[3:]))
    failures = json.loads(os.environ.get("CHECK_SHIM_FAIL", "{}"))
    line = " ".join([name] + args)
    for needle, code in failures.items():
        if needle in line:
            sys.exit(code)
    sys.exit(0)
    """
)


class CheckAllTests(unittest.TestCase):
    def setUp(self) -> None:
        # Berth builds only on Windows with PowerShell 7 (AGENTS.md): a missing pwsh is a broken machine, not a skip.
        if _PWSH is None:
            self.fail("pwsh (PowerShell 7) is not on PATH; check-all.ps1 needs it")
        self.pwsh: str = _PWSH
        self.folder = Path(tempfile.mkdtemp(prefix="berth-check-all-José-"))
        self.addCleanup(shutil.rmtree, self.folder, ignore_errors=True)
        self.script = SCRIPT
        self.checkout = ROOT
        self.shims = self.folder / "shims"
        self.shims.mkdir()
        self.log = self.folder / "calls.log"
        (self.folder / "shim.py").write_text(SHIM, encoding="utf-8")
        for name in ("python", "node", "dotnet"):
            self.add_shim(name)

    def add_shim(self, name: str) -> None:
        # The interpreter and shim paths come from the environment, so the batch file stays ASCII whatever the
        # account's temporary folder is called.
        (self.shims / f"{name}.cmd").write_text(
            f'@"%CHECK_SHIM_PYTHON%" "%CHECK_SHIM_SCRIPT%" {name} %*\r\n@exit /b %ERRORLEVEL%\r\n',
            encoding="ascii",
        )

    def remove_shim(self, name: str) -> None:
        (self.shims / f"{name}.cmd").unlink()

    def run_script(
        self, *args: str, fail: dict[str, int] | None = None, command: str | None = None
    ) -> subprocess.CompletedProcess[str]:
        """Runs the script with `-File`, or `command` with `-Command` when a test needs a caller's session."""
        system = Path(os.environ.get("SystemRoot", r"C:\Windows"))
        env = {
            key: value
            for key, value in os.environ.items()
            if key.upper() not in {"PATH", "PATHEXT"} and not key.upper().startswith("BERTH_")
        }
        env["PATH"] = os.pathsep.join([str(self.shims), str(Path(self.pwsh).parent), str(system / "System32"), str(system)])
        env["PATHEXT"] = ".COM;.EXE;.BAT;.CMD"
        env["CHECK_SHIM_LOG"] = str(self.log)
        env["CHECK_SHIM_FAIL"] = json.dumps(fail or {})
        env["CHECK_SHIM_PYTHON"] = sys.executable
        env["CHECK_SHIM_SCRIPT"] = str(self.folder / "shim.py")
        launch = (
            ["-Command", command] if command is not None else ["-File", str(self.script), *args]
        )
        process = subprocess.Popen(
            [self.pwsh, "-NoProfile", "-NonInteractive", *launch],
            cwd=self.folder,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            creationflags=CREATE_NO_WINDOW,
        )
        try:
            out, err = process.communicate(timeout=TIMEOUT_SECONDS)
        finally:
            # On a timeout, Ctrl+C or any other exit, stop the whole tree, never only the direct child (AGENTS.md),
            # before the temporary shim folder is removed.
            if process.poll() is None:
                subprocess.run(
                    ["taskkill", "/T", "/F", "/PID", str(process.pid)],
                    capture_output=True,
                    creationflags=CREATE_NO_WINDOW,
                    check=False,
                )
                process.communicate()
        return subprocess.CompletedProcess(process.args, process.returncode, out, err)

    def entries(self) -> list[dict[str, object]]:
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text(encoding="utf-8").splitlines()]

    def calls(self) -> list[list[str]]:
        """Every call the shims saw, each checked to have run in the checkout the script belongs to."""
        entries = self.entries()
        for entry in entries:
            self.assertEqual(Path(str(entry["cwd"])).resolve(), self.checkout.resolve(), entry)
        return [list(entry["call"]) for entry in entries]  # type: ignore[call-overload]

    def test_full_run_starts_the_seven_commands_in_order(self) -> None:
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), EXPECTED_CALLS)
        announced = [line for line in result.stdout.splitlines() if line.startswith("== ")]
        self.assertEqual(
            announced,
            [f"== {step}: {' '.join(call)}" for step, call in zip(STEPS, EXPECTED_CALLS)],
        )

    def test_first_failure_stops_the_run_with_its_exit_code(self) -> None:
        result = self.run_script(fail={"verify-agent-tiers": 7})
        self.assertEqual(result.returncode, 7)
        self.assertEqual(self.calls(), EXPECTED_CALLS[:5])
        self.assertEqual(
            result.stdout.splitlines()[-1],
            "FAILED agent-tiers: node scripts/verify-agent-tiers.mjs (exit 7)",
        )

    def test_missing_program_exits_127_and_starts_nothing_after(self) -> None:
        self.remove_shim("node")
        result = self.run_script()
        self.assertEqual(result.returncode, 127)
        self.assertEqual(self.calls(), EXPECTED_CALLS[:4])
        self.assertEqual(result.stdout.splitlines()[-1], "FAILED agent-tiers: node not found")

    def test_list_prints_the_names_and_starts_nothing(self) -> None:
        result = self.run_script("-List")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout.splitlines(), STEPS)
        self.assertEqual(self.calls(), [])

    def test_only_one_step(self) -> None:
        result = self.run_script("-Only", "build")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), [EXPECTED_CALLS[5]])

    def test_only_runs_in_declared_order_and_test_alone_rebuilds(self) -> None:
        result = self.run_script("-Only", "test,plan-validate")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(
            self.calls(),
            [EXPECTED_CALLS[0], ["dotnet", "test", "--solution", "Berth.slnx", "-c", "Release"]],
        )

    def test_only_with_an_unknown_name_exits_2_before_anything_starts(self) -> None:
        result = self.run_script("-Only", "build,nosuch")
        self.assertEqual(result.returncode, 2)
        self.assertIn("nosuch", result.stderr)
        self.assertEqual(self.calls(), [])

    def test_heavy_steps_go_through_bosun_when_it_is_on_path(self) -> None:
        self.add_shim("bosun")
        result = self.run_script("-Only", "build,test")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(
            self.calls(),
            [
                ["bosun", "run", "--", *EXPECTED_CALLS[5]],
                EXPECTED_CALLS[5],
                ["bosun", "run", "--", *EXPECTED_CALLS[6]],
                EXPECTED_CALLS[6],
            ],
        )
        self.assertIn("== build: bosun run -- dotnet build Berth.slnx -c Release", result.stdout)

    def test_runs_in_its_own_checkout_even_when_the_path_looks_like_a_wildcard(self) -> None:
        # Berth[1] next to a decoy Berth1: a wildcard Set-Location would enter the decoy.
        self.checkout = self.folder / "Berth[1]"
        (self.checkout / "scripts").mkdir(parents=True)
        (self.folder / "Berth1" / "scripts").mkdir(parents=True)
        shutil.copy2(SCRIPT, self.checkout / "scripts" / "check-all.ps1")
        self.script = self.checkout / "scripts" / "check-all.ps1"
        result = self.run_script("-Only", "plan-validate")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), [EXPECTED_CALLS[0]])

    def test_a_callers_native_error_preference_does_not_hide_the_failure(self) -> None:
        command = (
            "$PSNativeCommandUseErrorActionPreference = $true; $ErrorActionPreference = 'Stop'; "
            f"& '{SCRIPT}' -Only agent-tiers; exit $LASTEXITCODE"
        )
        result = self.run_script(command=command, fail={"verify-agent-tiers": 7})
        self.assertEqual(result.returncode, 7, result.stdout + result.stderr)
        self.assertEqual(
            result.stdout.splitlines()[-1],
            "FAILED agent-tiers: node scripts/verify-agent-tiers.mjs (exit 7)",
        )

    def test_heavy_steps_run_directly_without_bosun(self) -> None:
        result = self.run_script("-Only", "build,test")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), EXPECTED_CALLS[5:])
        self.assertNotIn("bosun", result.stdout)


if __name__ == "__main__":
    unittest.main()
