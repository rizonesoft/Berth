"""Tests for scripts/build-tokens.py."""

from __future__ import annotations

import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build-tokens.py"
TOKENS_JSON = ROOT / "design" / "tokens.json"

_SPEC = importlib.util.spec_from_file_location("build_tokens", SCRIPT)
assert _SPEC is not None and _SPEC.loader is not None
build_tokens = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(build_tokens)

XAML_NS = "http://schemas.microsoft.com/winfx/2006/xaml"
KEY_ATTR = f"{{{XAML_NS}}}Key"


class BuildTokensTests(unittest.TestCase):
    def test_fixture_output_matches_expected_xml(self) -> None:
        fixture = {
            "color": {
                "light": {
                    "mapBackground": "#FFFFFF",
                    "markerSelected": "#1A1A1A",
                },
                "dark": {
                    "mapBackground": "#161616",
                    "markerSelected": "#FFFFFF",
                },
            },
            "size": {
                "marker": 11,
                "monitorStrokeThickness": 1.5,
            },
            "radius": {
                "card": 6,
            },
            "type": {
                "family": "Segoe UI Variable Text, Segoe UI",
                "familyDisplay": "Segoe UI Variable Display, Segoe UI",
                "body": {"size": 14, "weight": 400},
                "title": {"size": 24, "weight": 600, "family": "familyDisplay"},
            },
            "timing": {
                "visiblePollSeconds": 5,
            },
        }

        out = build_tokens.generate_dictionaries(fixture, source_label="fixture.json")

        self.assertEqual(
            out["Tokens.Light.xaml"],
            "<!-- Generated from fixture.json by scripts/build-tokens.py. Do not edit by hand; run python scripts/build-tokens.py. -->\n"
            '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
            '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
            '    <SolidColorBrush x:Key="Berth.MapBackground" Color="#FFFFFF" />\n'
            '    <SolidColorBrush x:Key="Berth.MarkerSelected" Color="#1A1A1A" />\n'
            "</ResourceDictionary>\n",
        )
        self.assertEqual(
            out["Tokens.HighContrast.xaml"],
            "<!-- Generated from fixture.json by scripts/build-tokens.py. Do not edit by hand; run python scripts/build-tokens.py. -->\n"
            '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
            '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
            '    <SolidColorBrush x:Key="Berth.MapBackground" Color="{DynamicResource {x:Static SystemColors.WindowColorKey}}" />\n'
            '    <SolidColorBrush x:Key="Berth.MarkerSelected" Color="{DynamicResource {x:Static SystemColors.HighlightColorKey}}" />\n'
            "</ResourceDictionary>\n",
        )
        self.assertEqual(
            out["Tokens.Shared.xaml"],
            "<!-- Generated from fixture.json by scripts/build-tokens.py. Do not edit by hand; run python scripts/build-tokens.py. -->\n"
            '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
            '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n'
            '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">\n'
            '    <sys:Double x:Key="Berth.Marker">11</sys:Double>\n'
            '    <sys:Double x:Key="Berth.MonitorStrokeThickness">1.5</sys:Double>\n'
            '    <CornerRadius x:Key="Berth.RadiusCard">6</CornerRadius>\n'
            '    <sys:Double x:Key="Berth.VisiblePollSeconds">5</sys:Double>\n'
            '    <FontFamily x:Key="Berth.Type.Family">Segoe UI Variable Text, Segoe UI</FontFamily>\n'
            '    <FontFamily x:Key="Berth.Type.FamilyDisplay">Segoe UI Variable Display, Segoe UI</FontFamily>\n'
            '    <Style x:Key="Berth.Type.Body" TargetType="TextBlock">\n'
            '        <Setter Property="FontFamily" Value="{StaticResource Berth.Type.Family}" />\n'
            '        <Setter Property="FontSize" Value="14" />\n'
            '        <Setter Property="FontWeight" Value="Normal" />\n'
            "    </Style>\n"
            '    <Style x:Key="Berth.Type.Title" TargetType="TextBlock">\n'
            '        <Setter Property="FontFamily" Value="{StaticResource Berth.Type.FamilyDisplay}" />\n'
            '        <Setter Property="FontSize" Value="24" />\n'
            '        <Setter Property="FontWeight" Value="SemiBold" />\n'
            "    </Style>\n"
            "</ResourceDictionary>\n",
        )

    def test_every_token_in_design_tokens_json_is_emitted_and_disjoint(self) -> None:
        tokens = json.loads(TOKENS_JSON.read_text(encoding="utf-8"))
        out = build_tokens.generate_dictionaries(tokens)

        light_keys = {el.attrib[KEY_ATTR] for el in ET.fromstring(out["Tokens.Light.xaml"])}
        dark_keys = {el.attrib[KEY_ATTR] for el in ET.fromstring(out["Tokens.Dark.xaml"])}
        hc_keys = {el.attrib[KEY_ATTR] for el in ET.fromstring(out["Tokens.HighContrast.xaml"])}
        shared_keys = {el.attrib[KEY_ATTR] for el in ET.fromstring(out["Tokens.Shared.xaml"])}

        expected_colors = {f"Berth.{build_tokens.pascal(k)}" for k in tokens["color"]["light"]}
        self.assertEqual(light_keys, expected_colors)
        self.assertEqual(dark_keys, expected_colors)
        self.assertEqual(hc_keys, expected_colors)

        expected_shared = (
            {f"Berth.{build_tokens.pascal(k)}" for k in tokens["size"]}
            | {f"Berth.Radius{build_tokens.pascal(k)}" for k in tokens["radius"]}
            | {f"Berth.{build_tokens.pascal(k)}" for k in tokens["timing"]}
            | {f"Berth.Type.{build_tokens.pascal(k)}" for k in tokens["type"]}
        )
        self.assertEqual(shared_keys, expected_shared)
        self.assertEqual(shared_keys & light_keys, set())

        # Verify all 17 High Contrast colour bindings match SPEC §2.
        hc_root = ET.fromstring(out["Tokens.HighContrast.xaml"])
        hc_bindings = {el.attrib[KEY_ATTR]: el.attrib["Color"] for el in hc_root}
        for key, sys_key in build_tokens.HIGH_CONTRAST_MAP.items():
            res_key = f"Berth.{build_tokens.pascal(key)}"
            self.assertEqual(
                hc_bindings[res_key],
                f"{{DynamicResource {{x:Static SystemColors.{sys_key}}}}}",
            )

    def test_check_is_read_only_newline_tolerant_and_reports_edited_and_missing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out_dir = Path(tmp)
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                rc = build_tokens.main(["--tokens", str(TOKENS_JSON), "--out-dir", str(out_dir)])
            self.assertEqual(rc, 0)

            # Fresh files pass --check even when converted to CRLF.
            light_path = out_dir / "Tokens.Light.xaml"
            light_path.write_bytes(light_path.read_text(encoding="utf-8").replace("\n", "\r\n").encode("utf-8"))
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                rc = build_tokens.main(["--check", "--tokens", str(TOKENS_JSON), "--out-dir", str(out_dir)])
            self.assertEqual(rc, 0)

            # Edit one file and remove another: --check exits 1, reports both, writes nothing.
            dark_path = out_dir / "Tokens.Dark.xaml"
            edited_bytes = dark_path.read_text(encoding="utf-8").replace("#161616", "#000000").encode("utf-8")
            dark_path.write_bytes(edited_bytes)
            hc_path = out_dir / "Tokens.HighContrast.xaml"
            hc_path.unlink()

            stderr = io.StringIO()
            with redirect_stderr(stderr):
                rc = build_tokens.main(["--check", "--tokens", str(TOKENS_JSON), "--out-dir", str(out_dir)])
            self.assertEqual(rc, 1)
            err_text = stderr.getvalue()
            self.assertIn("Tokens.Dark.xaml", err_text)
            self.assertIn("Tokens.HighContrast.xaml", err_text)
            self.assertEqual(dark_path.read_bytes(), edited_bytes)
            self.assertFalse(hc_path.exists())

    def test_overlap_between_shared_and_color_keys_raises(self) -> None:
        tokens = json.loads(TOKENS_JSON.read_text(encoding="utf-8"))
        tokens["size"]["monitorStroke"] = 1.5
        with self.assertRaises(ValueError):
            build_tokens.generate_dictionaries(tokens)


if __name__ == "__main__":
    unittest.main()
