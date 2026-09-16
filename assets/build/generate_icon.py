#!/usr/bin/env python3
"""
Generatore dell'icona MedReminder.

Produce:
  - assets/medreminder.svg  (sorgente vettoriale, ~solo per riferimento)
  - assets/medreminder.ico  (multi-risoluzione 16/24/32/48/64/128/256)
  - assets/medreminder-256.png (per anteprima/README)

Design: pillola/capsula orizzontale ruotata di -30 gradi, bicolore
blu scuro/blu chiaro, senza sfondo, con highlight superiore.
Dimensione target di riferimento: 256x256.

Prerequisiti: pip install Pillow

Esecuzione:
  python3 assets/build/generate_icon.py
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "assets"
ASSETS.mkdir(exist_ok=True)

# Palette blu MedReminder.
BLUE_DARK = (31, 90, 166, 255)     # #1F5AA6
BLUE_LIGHT = (74, 158, 255, 255)   # #4A9EFF
HIGHLIGHT = (255, 255, 255, 110)    # bianco semitrasparente

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
CANVAS = 512  # renderizziamo a 512 e poi facciamo downsample: bordi più lisci


def draw_pill(canvas_size: int) -> Image.Image:
    """Disegna la capsula ruotata su un'immagine canvas_size x canvas_size."""
    # Renderizza la capsula orizzontale su un'immagine sovradimensionata
    # per far spazio alla rotazione senza clipping.
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

    # ---- Metà sinistra (blu scuro) ----
    # Semicerchio sinistro + rettangolo fino a xm.
    draw.pieslice([x0, y0, x0 + pill_h, y1], start=90, end=270, fill=BLUE_DARK)
    draw.rectangle([x0 + r, y0, xm, y1], fill=BLUE_DARK)

    # ---- Metà destra (blu chiaro) ----
    draw.pieslice([x1 - pill_h, y0, x1, y1], start=270, end=450, fill=BLUE_LIGHT)
    draw.rectangle([xm, y0, x1 - r, y1], fill=BLUE_LIGHT)

    # ---- Linea di giunzione centrale (leggerissima ombra) ----
    line_w = max(1, canvas_size // 128)
    draw.line([(xm, y0), (xm, y1)], fill=(255, 255, 255, 40), width=line_w)

    # ---- Highlight superiore per dare volume ----
    # Ellisse schiacciato bianco semitrasparente sulla metà alta della pillola.
    hl_pad_x = int(pill_w * 0.10)
    hl_h = int(pill_h * 0.35)
    hl_y = y0 + int(pill_h * 0.10)
    draw.ellipse(
        [x0 + hl_pad_x, hl_y, x1 - hl_pad_x, hl_y + hl_h],
        fill=HIGHLIGHT,
    )

    # ---- Rotazione ----
    rotated = temp.rotate(-30, resample=Image.BICUBIC, expand=True)

    # Componi centrato su canvas quadrato trasparente.
    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    ox = (canvas_size - rotated.width) // 2
    oy = (canvas_size - rotated.height) // 2
    canvas.alpha_composite(rotated, (ox, oy))
    return canvas


def write_svg() -> None:
    """Scrive una versione SVG stilizzata equivalente (sorgente/riferimento).

    Non è usata dall'app a runtime — l'app usa l'ICO. Utile per README e
    modifiche future: chi vuole ritoccare il design lavora qui e ri-genera
    l'ICO con questo script.
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
    <!-- Metà sinistra -->
    <path d="M 96 176 L 256 176 L 256 336 L 96 336 A 80 80 0 0 1 96 176 Z" fill="#1F5AA6"/>
    <!-- Metà destra -->
    <path d="M 416 176 L 256 176 L 256 336 L 416 336 A 80 80 0 0 0 416 176 Z" fill="#4A9EFF"/>
    <!-- Linea di giunzione centrale (leggera) -->
    <line x1="256" y1="176" x2="256" y2="336" stroke="#ffffff" stroke-opacity="0.25" stroke-width="3"/>
    <!-- Highlight superiore -->
    <ellipse cx="256" cy="212" rx="120" ry="22" fill="url(#hl)"/>
  </g>
</svg>
"""
    (ASSETS / "medreminder.svg").write_text(svg, encoding="utf-8")


def main() -> None:
    # 1) Renderizza a 512, poi genera i mip-map per ogni size richiesto.
    big = draw_pill(CANVAS)

    frames = []
    for size in ICO_SIZES:
        frame = big.resize((size, size), resample=Image.LANCZOS)
        frames.append(frame)

    # 2) PNG di anteprima 256.
    preview = big.resize((256, 256), resample=Image.LANCZOS)
    preview.save(ASSETS / "medreminder-256.png", "PNG")

    # 3) Scrivi l'ICO multi-res.
    # Pillow scarta le sizes richieste se sono più grandi dell'immagine
    # base: usiamo quindi la frame PIÙ GRANDE come base e lasciamo che
    # sizes= elenchi tutte le risoluzioni desiderate. Pillow farà il
    # downsampling interno con LANCZOS.
    largest = big  # 512x512 render pulito
    largest.save(
        ASSETS / "medreminder.ico",
        format="ICO",
        sizes=[(s, s) for s in ICO_SIZES],
    )

    # 4) SVG statico (documentazione, non usato dall'app).
    write_svg()

    print(f"Wrote: {ASSETS / 'medreminder.ico'}")
    print(f"Wrote: {ASSETS / 'medreminder.svg'}")
    print(f"Wrote: {ASSETS / 'medreminder-256.png'}")


if __name__ == "__main__":
    main()
