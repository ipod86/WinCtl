#!/usr/bin/env python3
"""Erzeugt die Icons von WinCtl aus dem Logo von ioBroker.desktop-control (gleiches Motiv wie tools/make_logo.py im Adapter-Repo).

Aufruf (im WinCtl-Repo):  python3 tools/make_icon.py
Benötigt: pip install pillow
Schreibt: assets/winctl.ico (exe-Icon, 16-256 px), assets/tray.png (64x64, Grundlage fürs Tray-Icon, wird in die exe eingebettet)
          und assets/winctl.png (512x512, README)
"""
import os
from PIL import Image, ImageDraw

BLUE, DEEP, WHITE, YELLOW = (51, 153, 204), (15, 39, 64), (255, 255, 255), (255, 196, 61)
S = 2048  # Supersampling, danach runterskaliert


def px(v):
    return round(v * S)


def rrect(d, box, radius, fill):
    d.rounded_rectangle([px(c) for c in box], radius=px(radius), fill=fill)


def draw():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Hintergrund-Kachel (Squircle) in ioBroker-Blau
    rrect(d, (0.02, 0.02, 0.98, 0.98), 0.24, BLUE)

    # 2x2-Kacheln = der "Bildschirm": drei weiß, eine gelb als Akzent
    x0, y0, w, h, gap = 0.17, 0.20, 0.31, 0.21, 0.04
    tiles = [(x0, y0), (x0 + w + gap, y0), (x0, y0 + h + gap), (x0 + w + gap, y0 + h + gap)]
    for (tx, ty), color in zip(tiles, [WHITE, WHITE, WHITE, YELLOW]):
        rrect(d, (tx, ty, tx + w, ty + h), 0.04, color)

    # Standfuß als Pille
    rrect(d, (0.38, 0.76, 0.62, 0.81), 0.025, WHITE)

    # Mauszeiger auf der gelben Kachel: weißer Rand (Halo) + dunkelblaue Füllung
    ox, oy = tiles[3][0] + w * 0.42, tiles[3][1] + h * 0.30
    arrow = [(0, 0), (0, .205), (.047, .16), (.078, .235), (.112, .220), (.081, .148), (.142, .148)]
    pts = [(px(ox + x), px(oy + y)) for x, y in arrow]
    d.polygon(pts, fill=WHITE)
    d.line(pts + [pts[0]], fill=WHITE, width=px(0.03), joint="curve")
    d.polygon(pts, fill=DEEP)
    return img


if __name__ == "__main__":
    logo = draw()
    os.makedirs("assets", exist_ok=True)
    logo.resize((256, 256), Image.LANCZOS).save("assets/winctl.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    logo.resize((64, 64), Image.LANCZOS).save("assets/tray.png")
    logo.resize((512, 512), Image.LANCZOS).save("assets/winctl.png")
    print("geschrieben: assets/winctl.ico, assets/tray.png, assets/winctl.png")
