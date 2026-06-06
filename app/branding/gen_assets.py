"""Generate the WinUI app icon assets from logo.svg.

Run:  uv run --with cairosvg --with pillow python app/branding/gen_assets.py
"""
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

# Multi-size .ico for the window / title bar / taskbar.
base = ASSETS / "_icobase.png"
cairosvg.svg2png(url=str(SVG), write_to=str(base), output_width=256, output_height=256)
Image.open(base).save(
    ASSETS / "AppIcon.ico", format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
base.unlink(missing_ok=True)

print("assets generated in", ASSETS)
