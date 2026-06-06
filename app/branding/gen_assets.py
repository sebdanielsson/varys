"""Generate the WinUI app icon assets from logo.svg.

Run:  uv run --with cairosvg --with pillow python app/branding/gen_assets.py
"""
import struct
from pathlib import Path

import cairosvg
from PIL import Image

HERE = Path(__file__).resolve().parent
SVG = HERE / "logo.svg"
ASSETS = HERE.parent / "TranscribeApp" / "Assets"


def render(path: str, size: int) -> None:
    cairosvg.svg2png(url=str(SVG), write_to=str(ASSETS / path),
                     output_width=size, output_height=size)


def composite(path: str, w: int, h: int, logo: int) -> None:
    tmp = ASSETS / "_logo.png"
    cairosvg.svg2png(url=str(SVG), write_to=str(tmp), output_width=logo, output_height=logo)
    mark = Image.open(tmp).convert("RGBA")
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(mark, ((w - logo) // 2, (h - logo) // 2), mark)
    canvas.save(ASSETS / path)
    tmp.unlink(missing_ok=True)


# Square tiles (rendered directly from the SVG).
render("Square44x44Logo.scale-200.png", 88)
render("Square44x44Logo.targetsize-24_altform-unplated.png", 24)
render("Square44x44Logo.targetsize-48_altform-lightunplated.png", 48)
render("Square150x150Logo.scale-200.png", 300)
render("StoreLogo.png", 50)
render("LockScreenLogo.scale-200.png", 48)

# Wide tile + splash (logo centered on a transparent canvas).
composite("Wide310x150Logo.scale-200.png", 620, 300, 248)
composite("SplashScreen.scale-200.png", 1240, 600, 360)

# Multi-size .ico with each frame rendered DIRECTLY from the SVG (crisp at every
# size, incl. the small title-bar sizes used at high DPI). Frames are PNG-encoded.
def build_ico(out_path: Path) -> None:
    sizes = [16, 20, 24, 28, 32, 40, 48, 64, 128, 256]
    frames = []
    for s in sizes:
        tmp = ASSETS / f"_ico_{s}.png"
        cairosvg.svg2png(url=str(SVG), write_to=str(tmp), output_width=s, output_height=s)
        frames.append((s, tmp.read_bytes()))
        tmp.unlink(missing_ok=True)
    header = struct.pack("<HHH", 0, 1, len(frames))
    entries = bytearray()
    data = bytearray()
    offset = 6 + 16 * len(frames)
    for s, png in frames:
        wh = 0 if s >= 256 else s
        entries += struct.pack("<BBBBHHII", wh, wh, 0, 0, 1, 32, len(png), offset)
        data += png
        offset += len(png)
    out_path.write_bytes(header + bytes(entries) + bytes(data))


build_ico(ASSETS / "AppIcon.ico")

print("assets generated in", ASSETS)
