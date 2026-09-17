#!/usr/bin/env python3
"""
Generator of the MedReminder icon.

Produces:
  - assets/medreminder.svg  (vector source, ~for reference only)
  - assets/medreminder.ico  (multi-resolution 16/24/32/48/64/128/256)
  - assets/medreminder-256.png (for preview / README)

Design: horizontal pill / capsule rotated by -30 degrees, two-tone
dark blue / light blue, transparent background, top highlight.
Reference target size: 256x256.

Prerequisites: pip install Pillow

Run:
  python3 assets/build/generate_icon.py
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "assets"
ASSETS.mkdir(exist_ok=True)

# MedReminder blue palette.
BLUE_DARK = (31, 90, 166, 255)     # #1F5AA6
BLUE_LIGHT = (74, 158, 255, 255)   # #4A9EFF
HIGHLIGHT = (255, 255, 255, 110)    # semi-transparent white

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
CANVAS = 512  # render at 512 and downsample: smoother edges


def draw_pill(canvas_size: int) -> Image.Image:
    """Draw the rotated capsule on a canvas_size x canvas_size image."""
    # Render the horizontal capsule on an oversized image so the
    # rotation does not clip.
    pad = canvas_size // 4
    pill_w = int(canvas_size * 0.78)
    pill_h = int(canvas_size * 0.34)
    temp_w = pill_w + 2 * pad
    temp_h = pill_h + 2 * pad
    temp = Image.new("RGBA", (temp_w, temp_h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(temp)

    x0 = pad
    y0 = pad
    x1 = x0 + pill_w
    y1 = y0 + pill_h
    xm = (x0 + x1) // 2
    r = pill_h // 2

    # ---- Left half (dark blue) ----
    # Left semicircle + rectangle up to xm.
    draw.pieslice([x0, y0, x0 + pill_h, y1], start=90, end=270, fill=BLUE_DARK)
    draw.rectangle([x0 + r, y0, xm, y1], fill=BLUE_DARK)

    # ---- Right half (light blue) ----
    draw.pieslice([x1 - pill_h, y0, x1, y1], start=270, end=450, fill=BLUE_LIGHT)
    draw.rectangle([xm, y0, x1 - r, y1], fill=BLUE_LIGHT)

    # ---- Central joint line (very slight shadow) ----
    line_w = max(1, canvas_size // 128)
    draw.line([(xm, y0), (xm, y1)], fill=(255, 255, 255, 40), width=line_w)

    # ---- Top highlight for volume ----
    # Flattened semi-transparent white ellipse on the upper half of
    # the pill.
    hl_pad_x = int(pill_w * 0.10)
    hl_h = int(pill_h * 0.35)
    hl_y = y0 + int(pill_h * 0.10)
    draw.ellipse(
        [x0 + hl_pad_x, hl_y, x1 - hl_pad_x, hl_y + hl_h],
        fill=HIGHLIGHT,
    )

    # ---- Rotation ----
    rotated = temp.rotate(-30, resample=Image.BICUBIC, expand=True)

    # Compose centered on a transparent square canvas.
    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    ox = (canvas_size - rotated.width) // 2
    oy = (canvas_size - rotated.height) // 2
    canvas.alpha_composite(rotated, (ox, oy))
    return canvas


def write_svg() -> None:
    """Write an equivalent stylized SVG version (source / reference).

    Not used by the app at runtime — the app uses the ICO. Useful for
    the README and future changes: anyone who wants to tweak the
    design works here and re-generates the ICO with this script.
    """
    svg = """<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
  <defs>
    <linearGradient id="hl" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" stop-color="#ffffff" stop-opacity="0.55"/>
      <stop offset="100%" stop-color="#ffffff" stop-opacity="0"/>
    </linearGradient>
  </defs>
  <g transform="translate(256 256) rotate(-30) translate(-256 -256)">
    <!-- Left half -->
    <path d="M 96 176 L 256 176 L 256 336 L 96 336 A 80 80 0 0 1 96 176 Z" fill="#1F5AA6"/>
    <!-- Right half -->
    <path d="M 416 176 L 256 176 L 256 336 L 416 336 A 80 80 0 0 0 416 176 Z" fill="#4A9EFF"/>
    <!-- Central joint line (subtle) -->
    <line x1="256" y1="176" x2="256" y2="336" stroke="#ffffff" stroke-opacity="0.25" stroke-width="3"/>
    <!-- Top highlight -->
    <ellipse cx="256" cy="212" rx="120" ry="22" fill="url(#hl)"/>
  </g>
</svg>
"""
    (ASSETS / "medreminder.svg").write_text(svg, encoding="utf-8")


def main() -> None:
    # 1) Render at 512, then generate mip-maps for each requested size.
    big = draw_pill(CANVAS)

    frames = []
    for size in ICO_SIZES:
        frame = big.resize((size, size), resample=Image.LANCZOS)
        frames.append(frame)

    # 2) 256 preview PNG.
    preview = big.resize((256, 256), resample=Image.LANCZOS)
    preview.save(ASSETS / "medreminder-256.png", "PNG")

    # 3) Write the multi-resolution ICO.
    # Pillow drops requested sizes that are larger than the base
    # image: we therefore use the LARGEST frame as the base and let
    # sizes= list every desired resolution. Pillow will downsample
    # internally with LANCZOS.
    largest = big  # 512x512 clean render
    largest.save(
        ASSETS / "medreminder.ico",
        format="ICO",
        sizes=[(s, s) for s in ICO_SIZES],
    )

    # 4) Static SVG (documentation, not used by the app).
    write_svg()

    print(f"Wrote: {ASSETS / 'medreminder.ico'}")
    print(f"Wrote: {ASSETS / 'medreminder.svg'}")
    print(f"Wrote: {ASSETS / 'medreminder-256.png'}")


if __name__ == "__main__":
    main()
