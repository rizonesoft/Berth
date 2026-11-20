#!/usr/bin/env python3
"""Generate WPF ResourceDictionary files under src/Berth/Theme/ from design/tokens.json."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TOKENS = ROOT / "design" / "tokens.json"
DEFAULT_OUT_DIR = ROOT / "src" / "Berth" / "Theme"

# SPEC §2 (clarified 2026-11-20, D04 T01 §4): all 17 colour tokens mapped to the four
# Windows High Contrast system colours (Window, WindowText, Highlight, GrayText).
HIGH_CONTRAST_MAP: dict[str, str] = {
    "mapBackground": "WindowColorKey",
    "mapGrid": "GrayTextColorKey",
    "mapBorder": "WindowTextColorKey",
    "monitorFill": "WindowColorKey",
    "monitorStroke": "WindowTextColorKey",
    "markerInPlace": "WindowTextColorKey",
    "markerMoved": "WindowTextColorKey",
    "markerMissing": "WindowTextColorKey",
    "markerNeutral": "WindowTextColorKey",
    "markerSelected": "HighlightColorKey",
    "activeBadgeFill": "WindowTextColorKey",
    "activeBadgeText": "WindowColorKey",
    "textSecondary": "WindowTextColorKey",
    "noticeWarningFill": "WindowColorKey",
    "noticeWarningBorder": "WindowTextColorKey",
    "noticeErrorFill": "WindowColorKey",
    "noticeErrorBorder": "WindowTextColorKey",
}

FONT_WEIGHT_MAP: dict[int, str] = {
    100: "Thin",
    200: "ExtraLight",
    300: "Light",
    400: "Normal",
    500: "Medium",
    600: "SemiBold",
    700: "Bold",
    800: "ExtraBold",
    900: "Black",
}


def pascal(name: str) -> str:
    if not name:
        raise ValueError("Token key must not be empty.")
    return name[0].upper() + name[1:]


def format_number(value: int | float) -> str:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"Expected numeric token value, got {value!r}")
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return str(value)


def _header(source_label: str) -> str:
    return (
        f"<!-- Generated from {source_label} by scripts/build-tokens.py. "
        f"Do not edit by hand; run python scripts/build-tokens.py. -->\n"
    )


def render_color_dictionary(
    colors: dict[str, str], source_label: str
) -> str:
    lines = [
        _header(source_label).rstrip("\n"),
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    ]
    for key, hex_value in colors.items():
        resource_key = f"Berth.{pascal(key)}"
        lines.append(
            f'    <SolidColorBrush x:Key="{escape(resource_key)}" Color="{escape(hex_value)}" />'
        )
    lines.append("</ResourceDictionary>")
    return "\n".join(lines) + "\n"


def render_high_contrast_dictionary(
    color_keys: list[str], source_label: str
) -> str:
    missing = [key for key in color_keys if key not in HIGH_CONTRAST_MAP]
    if missing:
        raise ValueError(
            f"Missing High Contrast mapping for colour token(s): {', '.join(missing)}"
        )
    lines = [
        _header(source_label).rstrip("\n"),
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    ]
    for key in color_keys:
        resource_key = f"Berth.{pascal(key)}"
        system_key = HIGH_CONTRAST_MAP[key]
        lines.append(
            f'    <SolidColorBrush x:Key="{escape(resource_key)}" '
            f'Color="{{DynamicResource {{x:Static SystemColors.{system_key}}}}}" />'
        )
    lines.append("</ResourceDictionary>")
    return "\n".join(lines) + "\n"


def render_shared_dictionary(tokens: dict[str, Any], source_label: str) -> str:
    sizes: dict[str, int | float] = tokens["size"]
    radii: dict[str, int | float] = tokens["radius"]
    spacings: dict[str, str] = tokens.get("spacing", {})
    timings: dict[str, int | float] = tokens["timing"]
    type_section: dict[str, Any] = tokens["type"]

    family_keys = [k for k, v in type_section.items() if isinstance(v, str)]
    style_keys = [k for k, v in type_section.items() if isinstance(v, dict)]

    lines = [
        _header(source_label).rstrip("\n"),
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">',
    ]

    for key, val in sizes.items():
        lines.append(
            f'    <sys:Double x:Key="Berth.{escape(pascal(key))}">{format_number(val)}</sys:Double>'
        )

    for key, val in radii.items():
        lines.append(
            f'    <CornerRadius x:Key="Berth.Radius{escape(pascal(key))}">{format_number(val)}</CornerRadius>'
        )

    for key, val in spacings.items():
        if not isinstance(val, str) or not val.strip():
            raise ValueError(f"Expected non-empty Thickness string for spacing '{key}', got {val!r}")
        lines.append(
            f'    <Thickness x:Key="Berth.Space.{escape(pascal(key))}">{escape(val)}</Thickness>'
        )

    for key, val in timings.items():
        lines.append(
            f'    <sys:Double x:Key="Berth.{escape(pascal(key))}">{format_number(val)}</sys:Double>'
        )

    for key in family_keys:
        family_value = type_section[key]
        lines.append(
            f'    <FontFamily x:Key="Berth.Type.{escape(pascal(key))}">{escape(family_value)}</FontFamily>'
        )

    for key in style_keys:
        spec = type_section[key]
        family_ref = spec.get("family", "family")
        if family_ref not in family_keys:
            raise ValueError(
                f"Type style '{key}' references unknown font family '{family_ref}'."
            )
        weight_num = int(spec["weight"])
        if weight_num not in FONT_WEIGHT_MAP:
            raise ValueError(
                f"Type style '{key}' uses unsupported font weight {weight_num}."
            )
        weight_name = FONT_WEIGHT_MAP[weight_num]
        size_str = format_number(spec["size"])
        style_key = f"Berth.Type.{pascal(key)}"
        family_resource = f"Berth.Type.{pascal(family_ref)}"
        lines.append(
            f'    <Style x:Key="{escape(style_key)}" TargetType="TextBlock">'
        )
        lines.append(
            f'        <Setter Property="FontFamily" Value="{{StaticResource {escape(family_resource)}}}" />'
        )
        lines.append(f'        <Setter Property="FontSize" Value="{size_str}" />')
        lines.append(
            f'        <Setter Property="FontWeight" Value="{weight_name}" />'
        )
        lines.append("    </Style>")

    lines.append("</ResourceDictionary>")
    return "\n".join(lines) + "\n"


def generate_dictionaries(
    tokens: dict[str, Any], source_label: str = "design/tokens.json"
) -> dict[str, str]:
    light: dict[str, str] = tokens["color"]["light"]
    dark: dict[str, str] = tokens["color"]["dark"]

    light_keys = list(light.keys())
    dark_keys = list(dark.keys())
    if set(light_keys) != set(dark_keys):
        only_light = sorted(set(light_keys) - set(dark_keys))
        only_dark = sorted(set(dark_keys) - set(light_keys))
        raise ValueError(
            f"Light and dark colour key sets differ: only in light={only_light}, only in dark={only_dark}"
        )

    shared_key_list = (
        [f"Berth.{pascal(k)}" for k in tokens["size"]]
        + [f"Berth.Radius{pascal(k)}" for k in tokens["radius"]]
        + [f"Berth.{pascal(k)}" for k in tokens["timing"]]
        + [f"Berth.Type.{pascal(k)}" for k in tokens["type"]]
    )
    seen_shared: set[str] = set()
    dup_shared: list[str] = []
    for key in shared_key_list:
        if key in seen_shared:
            dup_shared.append(key)
        seen_shared.add(key)
    if dup_shared:
        raise ValueError(
            f"Shared dictionary defines duplicate resource key(s): {', '.join(sorted(set(dup_shared)))}"
        )

    shared_keys = seen_shared
    color_resource_keys = {f"Berth.{pascal(k)}" for k in light_keys}
    overlap = sorted(shared_keys & color_resource_keys)
    if overlap:
        raise ValueError(
            f"Shared and colour dictionaries share resource key(s): {', '.join(overlap)}"
        )

    return {
        "Tokens.Light.xaml": render_color_dictionary(light, source_label),
        "Tokens.Dark.xaml": render_color_dictionary(dark, source_label),
        "Tokens.HighContrast.xaml": render_high_contrast_dictionary(
            light_keys, source_label
        ),
        "Tokens.Shared.xaml": render_shared_dictionary(tokens, source_label),
    }


def normalize_newlines(text: str) -> str:
    return text.replace("\r\n", "\n").replace("\r", "\n")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Generate WPF ResourceDictionary files from design/tokens.json."
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="Verify generated files on disk match design/tokens.json without writing.",
    )
    parser.add_argument(
        "--tokens",
        type=Path,
        default=DEFAULT_TOKENS,
        help="Path to tokens.json (default: design/tokens.json).",
    )
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=DEFAULT_OUT_DIR,
        help="Output directory for Tokens.*.xaml (default: src/Berth/Theme).",
    )
    args = parser.parse_args(argv)

    tokens = json.loads(args.tokens.read_text(encoding="utf-8"))
    source_label = "design/tokens.json"
    try:
        source_label = args.tokens.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        source_label = args.tokens.name

    expected = generate_dictionaries(tokens, source_label=source_label)

    if args.check:
        stale: list[str] = []
        for filename, content in expected.items():
            target = args.out_dir / filename
            if not target.exists():
                stale.append(f"missing: {target}")
                continue
            actual = normalize_newlines(target.read_text(encoding="utf-8"))
            if actual != normalize_newlines(content):
                stale.append(f"stale: {target}")
        if stale:
            for item in stale:
                print(item, file=sys.stderr)
            return 1
        print("tokens current")
        return 0

    args.out_dir.mkdir(parents=True, exist_ok=True)
    for filename, content in expected.items():
        target = args.out_dir / filename
        with target.open("w", encoding="utf-8", newline="\n") as fh:
            fh.write(content)
    print(f"wrote {len(expected)} token dictionaries to {args.out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
