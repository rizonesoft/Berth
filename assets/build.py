"""Rebuild Berth's raster brand assets from the sources in this folder.

Run:  python assets/build.py
Needs Pillow and Microsoft Edge (used headless to render the HTML sources).
Icon geometry here mirrors the SVG files in assets/icon/.
"""
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
EDGE = Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")
SS = 8  # supersampling factor for icon drawing

BLUE = (15, 108, 189)
DAY_BLUE = (76, 166, 240)
WHITE = (255, 255, 255)
INK = (26, 26, 26)


def _canvas(size):
    return Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))


def _rect(img, size, box, radius, colour, opacity=1.0):
    """Draw a rounded rectangle given in 96-unit icon coordinates."""
    k = size * SS / 96
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    x, y, w, h = box
    ImageDraw.Draw(layer).rounded_rectangle(
        [x * k, y * k, (x + w) * k, (y + h) * k],
        radius=radius * k,
        fill=colour + (round(255 * opacity),),
    )
    img.alpha_composite(layer)


def app_icon(size):
    img = _canvas(size)
    _rect(img, size, (0, 0, 96, 96), 21, BLUE)
    if size <= 24:  # berth-icon-small.svg
        for x, y in ((14, 14), (51, 14), (14, 51)):
            _rect(img, size, (x, y, 31, 31), 7, WHITE, 0.5)
        _rect(img, size, (51, 51, 31, 31), 7, WHITE)
    else:  # berth-icon.svg
        for x, y in ((17, 17), (51, 17), (17, 51)):
            _rect(img, size, (x, y, 28, 28), 7, WHITE, 0.5)
        _rect(img, size, (51, 51, 28, 28), 7, WHITE)
        _rect(img, size, (59.5, 59.5, 11, 11), 5.5, BLUE)
    return img.resize((size, size), Image.Resampling.LANCZOS)


def tray_icon(size, dark_taskbar):
    img = _canvas(size)
    quiet, opacity, held = (WHITE, 0.55, DAY_BLUE) if dark_taskbar else (INK, 0.45, BLUE)
    for x, y in ((6, 6), (52, 6), (6, 52)):
        _rect(img, size, (x, y, 38, 38), 9, quiet, opacity)
    _rect(img, size, (52, 52, 38, 38), 9, held)
    return img.resize((size, size), Image.Resampling.LANCZOS)


def save_ico(path, make, sizes):
    images = [make(s) for s in sorted(sizes, reverse=True)]
    images[0].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[1:])


def render(source, out, width, height, scale=1, transparent=False):
    """Screenshot an HTML source with headless Edge."""
    with tempfile.TemporaryDirectory() as profile:
        args = [
            str(EDGE), "--headless=new", "--disable-gpu", "--hide-scrollbars",
            f"--user-data-dir={profile}", f"--window-size={width},{height}",
            f"--force-device-scale-factor={scale}", f"--screenshot={out}",
        ]
        if transparent:
            args.append("--default-background-color=00000000")
        args.append(source)
        subprocess.run(args, check=True, timeout=120, capture_output=True,
                       creationflags=subprocess.CREATE_NO_WINDOW)


def render_lockup(out, dark):
    url = (HERE / "src" / "lockup.html").as_uri() + ("#dark" if dark else "")
    render(url, out, 700, 200, scale=2, transparent=True)
    img = Image.open(out).convert("RGBA")
    img.crop(img.getbbox()).save(out)


def main():
    icon, logo, banner = HERE / "icon", HERE / "logo", HERE / "banner"
    for folder in (logo, banner):
        folder.mkdir(exist_ok=True)

    save_ico(icon / "berth.ico", app_icon, [16, 20, 24, 32, 40, 48, 64, 256])
    save_ico(icon / "berth-tray-dark.ico", lambda s: tray_icon(s, True), [16, 20, 24, 32, 48])
    save_ico(icon / "berth-tray-light.ico", lambda s: tray_icon(s, False), [16, 20, 24, 32, 48])
    for size in (256, 512, 1024):
        app_icon(size).save(icon / f"berth-icon-{size}.png")

    render_lockup(logo / "berth-lockup.png", dark=False)
    render_lockup(logo / "berth-lockup-white.png", dark=True)
    src = HERE / "src"
    render((src / "github-social-preview.html").as_uri(), banner / "github-social-preview.png", 1280, 640)
    render((src / "social-banner.html").as_uri(), banner / "social-banner.png", 1500, 500)


if __name__ == "__main__":
    main()
