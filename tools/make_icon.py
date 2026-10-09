"""Renders src/Snipperoo/Assets/snipperoo.ico (the app/exe icon).

The tray icon and the windows load this file too, so it is the only place the logo is defined.
Usage: python tools/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

S = 1024  # supersampled canvas
VIOLET, PINK = (124, 92, 255), (255, 92, 168)


def gradient() -> Image.Image:
    img = Image.new("RGBA", (S, S))
    px = img.load()
    assert px is not None
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * (S - 1))
            px[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(VIOLET, PINK)) + (255,)
    return img


def logo() -> Image.Image:
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.24), fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(gradient(), (0, 0), mask)

    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    inset, arm, width = S * 0.24, S * 0.17, int(S * 0.075)
    lo, hi = inset, S - inset
    # Viewfinder corners, each an L of two rounded bars.
    for cx, sx in ((lo, 1), (hi, -1)):
        for cy, sy in ((lo, 1), (hi, -1)):
            d.line([(cx, cy + sy * arm), (cx, cy), (cx + sx * arm, cy)], fill=white, width=width, joint="curve")
            for px, py in ((cx, cy + sy * arm), (cx + sx * arm, cy), (cx, cy)):
                r = width / 2
                d.ellipse((px - r, py - r, px + r, py + r), fill=white)
    r = S * 0.11
    d.ellipse((S / 2 - r, S / 2 - r, S / 2 + r, S / 2 + r), fill=white)
    return img


if __name__ == "__main__":
    out = Path(__file__).resolve().parent.parent / "src" / "Snipperoo" / "Assets" / "snipperoo.ico"
    out.parent.mkdir(parents=True, exist_ok=True)
    icon = logo().resize((256, 256), Image.Resampling.LANCZOS)
    icon.save(out, sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    print(out)
