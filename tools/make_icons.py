"""Generate the TubeWorks command and feature icons.

Output (in src/TubeWorks.AddIn/Icons):
  toolbar_<size>.png  horizontal strip, one square icon per command, sizes 20, 32, 40, 64, 96, 128
  main_<size>.png     the command-group icon at the same sizes
  feat_<kind>[_suppressed|_highlighted].bmp  16x18 FeatureManager icons for the macro features

Usage: python3 tools/make_icons.py   (needs Pillow)
"""
import math
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "TubeWorks.AddIn", "Icons")
S = 128  # master drawing size
BLUE = (30, 110, 200, 255)
DARK = (20, 60, 110, 255)
GREY = (150, 150, 150, 255)
ORANGE = (230, 130, 20, 255)
RED = (200, 40, 40, 255)

# Order must match TubeWorksAddIn.Commands.
COMMANDS = ["base", "bend", "straight", "edit", "reverse", "multi", "plane", "trim", "notch",
            "unbend", "rebend", "flatten", "unwrap", "export", "table", "props", "check", "library"]
PLANNED = {"multi", "trim", "notch"}


def tube(d, x0, y0, x1, y1, w, c):
    d.line([(x0, y0), (x1, y1)], fill=c, width=w)


def arc(d, cx, cy, r, a0, a1, w, c):
    d.arc([cx - r, cy - r, cx + r, cy + r], a0, a1, fill=c, width=w)


def arrow(d, x0, y0, x1, y1, c, w=8):
    d.line([(x0, y0), (x1, y1)], fill=c, width=w)
    a = math.atan2(y1 - y0, x1 - x0)
    for s in (2.6, -2.6):
        d.line([(x1, y1), (x1 - 22 * math.cos(a + s / 3), y1 - 22 * math.sin(a + s / 3))], fill=c, width=w)


def draw(name):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = GREY if name in PLANNED else BLUE
    W = 22
    if name == "base":
        tube(d, 14, 100, 114, 28, W, c)
    elif name == "bend":
        tube(d, 14, 104, 60, 104, W, c); arc(d, 60, 54, 50, 270, 360, W, c); tube(d, 110, 54, 110, 14, W, c)
        d.rectangle([52, 92, 68, 116], fill=ORANGE)
    elif name == "straight":
        tube(d, 14, 64, 80, 64, W, c); arrow(d, 80, 64, 120, 64, ORANGE)
    elif name == "edit":
        arc(d, 50, 70, 40, 270, 360, W, c); tube(d, 90, 70, 90, 120, W, c)
        d.polygon([(70, 40), (110, 0), (126, 16), (86, 56)], fill=ORANGE)
    elif name == "reverse":
        arc(d, 64, 64, 44, 180, 360, W, c); arrow(d, 20, 64, 20, 110, ORANGE); arrow(d, 108, 64, 108, 110, ORANGE)
    elif name == "multi":
        arc(d, 40, 64, 30, 90, 270, W, c); arc(d, 88, 64, 30, 270, 450, W, c)
    elif name == "plane":
        d.polygon([(10, 90), (60, 60), (118, 90), (68, 120)], outline=DARK, width=6)
        arc(d, 64, 60, 36, 180, 360, W, c)
    elif name in ("trim", "notch"):
        tube(d, 14, 64, 100, 64, W, c)
        if name == "trim":
            d.line([(100, 20), (100, 108)], fill=RED, width=8)
        else:
            d.pieslice([80, 36, 124, 92], 90, 270, fill=RED)
    elif name == "unbend":
        tube(d, 14, 90, 114, 90, W, c); arc(d, 64, 70, 40, 200, 340, 8, GREY)
    elif name == "rebend":
        arc(d, 64, 80, 44, 180, 360, W, c); tube(d, 14, 116, 114, 116, 8, GREY)
    elif name == "flatten":
        arc(d, 40, 40, 26, 0, 90, 14, GREY); arrow(d, 64, 60, 64, 86, ORANGE); tube(d, 14, 106, 114, 106, W, c)
    elif name == "unwrap":
        d.rectangle([16, 30, 112, 98], outline=c, width=8)
    elif name == "export":
        tube(d, 14, 96, 74, 96, W, c); arrow(d, 60, 70, 116, 20, ORANGE)
    elif name == "table":
        d.rectangle([14, 18, 114, 110], outline=DARK, width=6)
        for y in (46, 74):
            d.line([(14, y), (114, y)], fill=DARK, width=5)
        d.line([(54, 18), (54, 110)], fill=DARK, width=5)
        arc(d, 84, 92, 16, 180, 270, 8, c)
    elif name == "props":
        d.ellipse([20, 20, 108, 108], outline=c, width=14); d.ellipse([46, 46, 82, 82], outline=c, width=8)
    elif name == "library":
        for i, x in enumerate((18, 46, 74)):
            d.rectangle([x, 20, x + 22, 110], outline=DARK, width=6)
        d.line([(100, 24), (118, 108)], fill=c, width=14)
    elif name == "check":
        tube(d, 14, 90, 70, 90, W, c); d.line([(60, 60), (80, 84), (118, 26)], fill=(40, 160, 60, 255), width=12)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    icons = [draw(n) for n in COMMANDS]
    for size in (20, 32, 40, 64, 96, 128):
        strip = Image.new("RGBA", (size * len(icons), size), (0, 0, 0, 0))
        for i, ic in enumerate(icons):
            strip.paste(ic.resize((size, size), Image.LANCZOS), (i * size, 0))
        strip.save(os.path.join(OUT, f"toolbar_{size}.png"))
        draw("bend").resize((size, size), Image.LANCZOS).save(os.path.join(OUT, f"main_{size}.png"))

    for kind, src in (("basetube", "base"), ("straight", "straight"), ("bend", "bend"), ("flatpattern", "flatten"), ("unbend", "unbend"), ("rebend", "rebend")):
        ic = draw(src)
        for state, tint in (("", None), ("_suppressed", (160, 160, 160)), ("_highlighted", (255, 255, 255))):
            canvas = Image.new("RGB", (16, 18), (255, 255, 255))
            small = ic.resize((16, 16), Image.LANCZOS)
            if tint == (160, 160, 160):
                g = small.convert("LA").convert("RGBA")
                small = Image.composite(g, small, small)
            bg = (0, 0, 120) if state == "_highlighted" else (255, 255, 255)
            canvas = Image.new("RGB", (16, 18), bg)
            canvas.paste(small, (0, 1), small)
            canvas.save(os.path.join(OUT, f"feat_{kind}{state}.bmp"))
    print("icons written to", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
