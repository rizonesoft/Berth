from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest

from scripts.tests.test_readme import check_readme_links


REPO_ROOT = Path(__file__).resolve().parents[2]
RENDER_SCRIPT = REPO_ROOT / "scripts" / "render-docs-images.ps1"
IMAGES = ("main-window-light.png", "main-window-dark.png")
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _write_pair(directory: Path, tag: str) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    for name in IMAGES:
        (directory / name).write_bytes(f"{tag}:{name}".encode("ascii"))


def _start_publish(
    stage_dir: Path,
    dest_dir: Path,
    pause_signal: Path | None = None,
) -> subprocess.Popen[str]:
    env = os.environ.copy()
    if pause_signal is not None:
        env["BERTH_DOCS_TEST_PAUSE_AFTER_FIRST"] = str(pause_signal)
    else:
        env.pop("BERTH_DOCS_TEST_PAUSE_AFTER_FIRST", None)
    return subprocess.Popen(
        [
            "pwsh",
            "-NoProfile",
            "-File",
            str(RENDER_SCRIPT),
            "-PublishOnly",
            "-StageDir",
            str(stage_dir),
            "-DestDir",
            str(dest_dir),
        ],
        cwd=str(REPO_ROOT),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        creationflags=CREATE_NO_WINDOW,
    )


class RenderDocsImagesTests(unittest.TestCase):
    def test_interrupted_publish_preserves_canonical_readme_images_and_recovers(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            dest = root / "docs" / "images"
            stage_new = root / "stage-new"
            signal = root / "paused.flag"
            readme = root / "README.md"
            readme.write_text(
                "# Berth\n\n"
                '<img src="docs/images/main-window-light.png" alt="Light">\n'
                '<img src="docs/images/main-window-dark.png" alt="Dark">\n',
                encoding="utf-8",
            )
            _write_pair(dest, "old")
            _write_pair(stage_new, "new")

            proc = _start_publish(stage_new, dest, pause_signal=signal)
            try:
                deadline = time.monotonic() + 15.0
                while not signal.exists() and time.monotonic() < deadline:
                    self.assertIsNone(proc.poll(), f"publisher exited early: {proc.stderr.read() if proc.stderr else ''}")
                    time.sleep(0.05)
                self.assertTrue(signal.exists(), "publisher did not reach mid-publish pause point")
                proc.kill()
                proc.wait(timeout=10)
            finally:
                if proc.poll() is None:
                    proc.kill()
                    proc.wait(timeout=10)
                if proc.stdout:
                    proc.stdout.close()
                if proc.stderr:
                    proc.stderr.close()

            # Before any retry, both canonical paths exist and resolve under the README checker.
            for name in IMAGES:
                path = dest / name
                self.assertTrue(path.is_file(), f"{name} disappeared during interrupted publish")
                self.assertGreater(path.stat().st_size, 0)
            self.assertEqual(check_readme_links(readme.read_text(encoding="utf-8"), root), [])

            # A subsequent publish recovers from the abandoned mutex, cleans orphaned temp files, and writes a matched pair.
            retry = _start_publish(stage_new, dest)
            stdout, stderr = retry.communicate(timeout=20)
            self.assertEqual(retry.returncode, 0, f"retry failed: {stderr}\n{stdout}")
            for name in IMAGES:
                self.assertEqual((dest / name).read_bytes(), f"new:{name}".encode("ascii"))
            leftovers = [p.name for p in dest.iterdir() if p.name not in IMAGES]
            self.assertEqual(leftovers, [], f"orphaned temp files remained: {leftovers}")

    def test_concurrent_publishers_serialize_without_clobbering_temp_files(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            dest = root / "docs" / "images"
            stage_a = root / "stage-a"
            stage_b = root / "stage-b"
            _write_pair(dest, "seed")
            _write_pair(stage_a, "run-a")
            _write_pair(stage_b, "run-b")

            proc_a = _start_publish(stage_a, dest)
            proc_b = _start_publish(stage_b, dest)
            out_a, err_a = proc_a.communicate(timeout=30)
            out_b, err_b = proc_b.communicate(timeout=30)
            self.assertEqual(proc_a.returncode, 0, f"publisher A failed: {err_a}\n{out_a}")
            self.assertEqual(proc_b.returncode, 0, f"publisher B failed: {err_b}\n{out_b}")

            light = (dest / IMAGES[0]).read_bytes().decode("ascii")
            dark = (dest / IMAGES[1]).read_bytes().decode("ascii")
            light_tag = light.split(":", 1)[0]
            dark_tag = dark.split(":", 1)[0]
            self.assertIn(light_tag, {"run-a", "run-b"})
            self.assertEqual(light_tag, dark_tag, f"mismatched pair after concurrent publish: {light} vs {dark}")
            leftovers = [p.name for p in dest.iterdir() if p.name not in IMAGES]
            self.assertEqual(leftovers, [], f"leftover temp files after concurrent publish: {leftovers}")


if __name__ == "__main__":
    unittest.main()
