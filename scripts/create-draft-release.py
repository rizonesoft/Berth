#!/usr/bin/env python3
"""Create or update a draft GitHub release for a verified version tag.

Checks that:
1. The tag equals `v` + `<Version>` from Directory.Build.props.
2. Both `artifacts/berth.exe` and `artifacts/berth.exe.sha256` exist and match.
3. `scripts/changelog-section.py <version>` yields non-empty release notes.
4. No published (non-draft) release already exists for the tag.
5. Creates the draft release with `gh release create --draft --verify-tag` (or updates
   an existing draft and uploads both assets with `--clobber` on rerun) and verifies
   both assets are present on the draft before exiting 0.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_PROPS = ROOT / "Directory.Build.props"
DEFAULT_CHANGELOG = ROOT / "CHANGELOG.md"
DEFAULT_ARTIFACTS = ROOT / "artifacts"
EXPECTED_ASSETS = ("berth.exe", "berth.exe.sha256")


def read_props_version(props_path: Path) -> str:
    tree = ET.parse(props_path)
    root = tree.getroot()
    for elem in root.iter():
        local_name = elem.tag.rsplit("}", 1)[-1]
        if local_name == "Version" and elem.text and elem.text.strip():
            return elem.text.strip()
    raise ValueError(f"no <Version> element found in {props_path}")


def verify_artifacts(artifacts_dir: Path) -> tuple[Path, Path]:
    exe_path = artifacts_dir / "berth.exe"
    sha_path = artifacts_dir / "berth.exe.sha256"
    if not exe_path.is_file() or exe_path.stat().st_size == 0:
        raise FileNotFoundError(f"missing or empty executable: {exe_path}")
    if not sha_path.is_file():
        raise FileNotFoundError(f"missing checksum file: {sha_path}")

    digest = hashlib.sha256(exe_path.read_bytes()).hexdigest().lower()
    expected = f"{digest}  berth.exe"
    actual = sha_path.read_text(encoding="utf-8").strip()
    if actual != expected:
        raise ValueError(f"checksum mismatch: expected {expected!r}, got {actual!r}")
    return exe_path, sha_path


def run_gh(gh_bin: str, args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [gh_bin, *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Create or update a draft GitHub release for a tag.")
    parser.add_argument("--tag", required=True, help="Git tag name, e.g. v0.1.0")
    parser.add_argument("--props", type=Path, default=DEFAULT_PROPS)
    parser.add_argument("--changelog", type=Path, default=DEFAULT_CHANGELOG)
    parser.add_argument("--artifacts", type=Path, default=DEFAULT_ARTIFACTS)
    parser.add_argument("--gh", default="gh", help="Path or command name for GitHub CLI")
    args = parser.parse_args(argv)

    try:
        version = read_props_version(args.props)
    except (OSError, ET.ParseError, ValueError) as exc:
        print(f"create-draft-release: {exc}", file=sys.stderr)
        return 1

    expected_tag = f"v{version}"
    if args.tag != expected_tag:
        print(
            f"create-draft-release: tag {args.tag!r} does not match Directory.Build.props Version ({expected_tag!r})",
            file=sys.stderr,
        )
        return 1

    try:
        exe_path, sha_path = verify_artifacts(args.artifacts)
    except (OSError, ValueError) as exc:
        print(f"create-draft-release: {exc}", file=sys.stderr)
        return 1

    with tempfile.TemporaryDirectory(prefix="berth-release-notes-") as tmp:
        notes_path = Path(tmp) / f"notes-{version}.md"
        extract = subprocess.run(
            [
                sys.executable,
                str(ROOT / "scripts" / "changelog-section.py"),
                version,
                "--changelog",
                str(args.changelog),
                "--out",
                str(notes_path),
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )
        if extract.returncode != 0:
            sys.stderr.write(extract.stderr or f"create-draft-release: changelog extraction failed for {version}\n")
            return extract.returncode

        title = f"Berth {version}"
        existing = run_gh(args.gh, ["release", "view", args.tag, "--json", "isDraft,assets"])
        if existing.returncode == 0:
            try:
                info = json.loads(existing.stdout)
            except json.JSONDecodeError as exc:
                print(f"create-draft-release: could not parse existing release JSON: {exc}", file=sys.stderr)
                return 1

            if not info.get("isDraft", False):
                print(
                    f"create-draft-release: release {args.tag!r} is already published; refusing to modify it",
                    file=sys.stderr,
                )
                return 1

            attached_existing = {asset.get("name", "") for asset in info.get("assets", [])}
            edit = run_gh(
                args.gh,
                ["release", "edit", args.tag, "--draft", "--title", title, "--notes-file", str(notes_path)],
            )
            if edit.returncode != 0:
                sys.stderr.write(edit.stderr or "create-draft-release: gh release edit failed\n")
                return edit.returncode

            to_upload: list[str] = []
            if "berth.exe" not in attached_existing:
                to_upload.append(str(exe_path))
            if "berth.exe.sha256" not in attached_existing:
                to_upload.append(str(sha_path))

            if to_upload:
                # Re-check draft status and upload only missing assets without --clobber so a release published
                # concurrently can never have its existing public assets deleted.
                recheck = run_gh(args.gh, ["release", "view", args.tag, "--json", "isDraft"])
                if recheck.returncode != 0 or not json.loads(recheck.stdout).get("isDraft", False):
                    print(
                        f"create-draft-release: release {args.tag!r} is no longer a draft; refusing asset upload",
                        file=sys.stderr,
                    )
                    return 1

                upload = run_gh(args.gh, ["release", "upload", args.tag, *to_upload])
                if upload.returncode != 0:
                    sys.stderr.write(upload.stderr or "create-draft-release: gh release upload failed\n")
                    return upload.returncode
        else:
            create = run_gh(
                args.gh,
                [
                    "release",
                    "create",
                    args.tag,
                    "--draft",
                    "--verify-tag",
                    "--title",
                    title,
                    "--notes-file",
                    str(notes_path),
                    str(exe_path),
                    str(sha_path),
                ],
            )
            if create.returncode != 0:
                sys.stderr.write(create.stderr or "create-draft-release: gh release create failed\n")
                return create.returncode

        verify = run_gh(args.gh, ["release", "view", args.tag, "--json", "isDraft,assets"])
        if verify.returncode != 0:
            sys.stderr.write(verify.stderr or "create-draft-release: gh release view verification failed\n")
            return verify.returncode

        try:
            final_info = json.loads(verify.stdout)
        except json.JSONDecodeError as exc:
            print(f"create-draft-release: could not parse verification JSON: {exc}", file=sys.stderr)
            return 1

        if not final_info.get("isDraft", False):
            print(f"create-draft-release: verified release {args.tag!r} is not a draft", file=sys.stderr)
            return 1

        attached = {asset.get("name", "") for asset in final_info.get("assets", [])}
        missing = [name for name in EXPECTED_ASSETS if name not in attached]
        if missing:
            print(
                f"create-draft-release: draft release {args.tag!r} is missing asset(s): {', '.join(missing)}",
                file=sys.stderr,
            )
            return 1

    print(f"create-draft-release: draft release {args.tag} verified with {', '.join(EXPECTED_ASSETS)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
