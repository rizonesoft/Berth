from __future__ import annotations

import os
from pathlib import Path
import struct
import subprocess
import tempfile
import time
import unittest
import zlib

from scripts.tests.test_readme import check_readme_links


REPO_ROOT = Path(__file__).resolve().parents[2]
RENDER_SCRIPT = REPO_ROOT / "scripts" / "render-docs-images.ps1"
IMAGES = ("main-window-light.png", "main-window-dark.png")
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _make_png(width: int, height: int, rgb: tuple[int, int, int]) -> bytes:
    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    row = b"\x00" + bytes(rgb) * width
    raw = row * height
    idat = zlib.compress(raw, level=1)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", idat) + chunk(b"IEND", b"")


PNG_BYTES = {
    ("old", IMAGES[0]): _make_png(980, 640, (240, 240, 240)),
    ("old", IMAGES[1]): _make_png(980, 640, (24, 24, 24)),
    ("new", IMAGES[0]): _make_png(980, 640, (230, 240, 250)),
    ("new", IMAGES[1]): _make_png(980, 640, (16, 26, 36)),
    ("seed", IMAGES[0]): _make_png(980, 640, (200, 200, 200)),
    ("seed", IMAGES[1]): _make_png(980, 640, (40, 40, 40)),
    ("run-a", IMAGES[0]): _make_png(980, 640, (210, 220, 230)),
    ("run-a", IMAGES[1]): _make_png(980, 640, (30, 40, 50)),
    ("run-b", IMAGES[0]): _make_png(980, 640, (220, 210, 200)),
    ("run-b", IMAGES[1]): _make_png(980, 640, (50, 40, 30)),
}


def _write_pair(directory: Path, tag: str) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    for name in IMAGES:
        (directory / name).write_bytes(PNG_BYTES[(tag, name)])


def _kill_process_tree(proc: subprocess.Popen[str]) -> None:
    if proc.poll() is None:
        subprocess.run(
            ["taskkill", "/T", "/F", "/PID", str(proc.pid)],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=CREATE_NO_WINDOW,
            check=False,
        )
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=5)
    if proc.stdout:
        proc.stdout.close()
    if proc.stderr:
        proc.stderr.close()


def _start_publish(
    stage_dir: Path | str,
    dest_dir: Path | str,
    pause_signal: Path | None = None,
    cwd: Path | None = None,
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
        cwd=str(cwd or REPO_ROOT),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        creationflags=CREATE_NO_WINDOW,
    )


def _communicate_with_teardown(proc: subprocess.Popen[str], timeout: float) -> tuple[int, str, str]:
    try:
        stdout, stderr = proc.communicate(timeout=timeout)
        return proc.returncode, stdout, stderr
    finally:
        _kill_process_tree(proc)


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
                    rc = proc.poll()
                    if rc is not None:
                        err = proc.stderr.read() if proc.stderr else ""
                        self.fail(f"publisher exited early ({rc}): {err}")
                    time.sleep(0.05)
                self.assertTrue(signal.exists(), "publisher did not reach mid-publish pause point")
                self.assertIsNone(proc.poll(), "publisher exited before termination")
                _kill_process_tree(proc)
                self.assertNotEqual(proc.returncode, 0, "terminated publisher must exit non-zero")
            finally:
                _kill_process_tree(proc)

            # Confirm the kill landed between light and dark, while BOTH canonical paths remained present.
            self.assertEqual((dest / IMAGES[0]).read_bytes(), PNG_BYTES[("new", IMAGES[0])])
            self.assertEqual((dest / IMAGES[1]).read_bytes(), PNG_BYTES[("old", IMAGES[1])])
            self.assertEqual(check_readme_links(readme.read_text(encoding="utf-8"), root), [])

            # A subsequent publish acquires the released kernel file lock, cleans orphans, and writes a matched pair.
            retry = _start_publish(stage_new, dest)
            rc, stdout, stderr = _communicate_with_teardown(retry, timeout=20.0)
            self.assertEqual(rc, 0, f"retry failed: {stderr}\n{stdout}")
            for name in IMAGES:
                self.assertEqual((dest / name).read_bytes(), PNG_BYTES[("new", name)])
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
            try:
                rc_a, out_a, err_a = _communicate_with_teardown(proc_a, timeout=30.0)
                rc_b, out_b, err_b = _communicate_with_teardown(proc_b, timeout=30.0)
            finally:
                _kill_process_tree(proc_a)
                _kill_process_tree(proc_b)

            self.assertEqual(rc_a, 0, f"publisher A failed: {err_a}\n{out_a}")
            self.assertEqual(rc_b, 0, f"publisher B failed: {err_b}\n{out_b}")

            light = (dest / IMAGES[0]).read_bytes()
            dark = (dest / IMAGES[1]).read_bytes()
            matched_a = light == PNG_BYTES[("run-a", IMAGES[0])] and dark == PNG_BYTES[("run-a", IMAGES[1])]
            matched_b = light == PNG_BYTES[("run-b", IMAGES[0])] and dark == PNG_BYTES[("run-b", IMAGES[1])]
            self.assertTrue(matched_a or matched_b, "concurrent publishers left a mismatched pair")
            leftovers = [p.name for p in dest.iterdir() if p.name not in IMAGES]
            self.assertEqual(leftovers, [], f"leftover temp files after concurrent publish: {leftovers}")

    def test_rejects_invalid_or_truncated_png_without_modifying_destination(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            dest = root / "docs" / "images"
            stage_bad = root / "stage-bad"
            _write_pair(dest, "old")
            stage_bad.mkdir(parents=True, exist_ok=True)
            (stage_bad / IMAGES[0]).write_bytes(PNG_BYTES[("new", IMAGES[0])])
            # Truncate the second PNG right after the IHDR chunk so it cannot decode.
            (stage_bad / IMAGES[1]).write_bytes(PNG_BYTES[("new", IMAGES[1])][:40])

            proc = _start_publish(stage_bad, dest)
            rc, stdout, stderr = _communicate_with_teardown(proc, timeout=20.0)
            self.assertNotEqual(rc, 0, f"truncated PNG should have been rejected: {stdout}\n{stderr}")
            for name in IMAGES:
                self.assertEqual((dest / name).read_bytes(), PNG_BYTES[("old", name)])

    def test_relative_dest_dir_from_outside_checkout_resolves_against_caller_cwd(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            stage_new = root / "stage-new"
            _write_pair(stage_new, "new")

            proc = _start_publish(stage_new, "rel-out/images", cwd=root)
            rc, stdout, stderr = _communicate_with_teardown(proc, timeout=20.0)
            self.assertEqual(rc, 0, f"relative -DestDir publish failed: {stderr}\n{stdout}")
            dest = root / "rel-out" / "images"
            for name in IMAGES:
                self.assertEqual((dest / name).read_bytes(), PNG_BYTES[("new", name)])


if __name__ == "__main__":
    unittest.main()
