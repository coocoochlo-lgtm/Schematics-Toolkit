#!/usr/bin/env python3
"""
Sound Cue node icons: writes each icon as an SVG here (Icons~, which Unity skips), then renders it to a PNG in
../Resources/SoundCueIcons for the editor to load. Same style as the Signal nodes' icons: a glossy blob, white at the
top to deep color at the bottom, with a nub where values leave (controls: right) or arrive (Output: left), and a white
symbol on it.

    pip install cairosvg
    python3 make_icons.py          # rewrites every SVG, then renders every SVG
    python3 make_icons.py --render # only renders the SVGs (after editing them by hand, in Inkscape...)
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Resources", "SoundCueIcons")
W, H = 100, 84
SCALE = 2  # PNGs at twice the size, so they stay crisp on high-DPI screens

# Symbols, drawn around (0, 0) = the blob's middle. "S" = a white stroke (with a soft dark edge); "F" = white fill.
SYMBOLS = {
    # A clock.
    "time": [("S", '<circle cx="0" cy="0" r="19"/>'), ("S", '<path d="M0 0 V-11 M0 0 L9 6"/>')],
    # A sound spreading out from a point.
    "distance": [("F", '<circle cx="-16" cy="0" r="6"/>'),
                 ("S", '<path d="M-5 -10 A14 14 0 0 1 -5 10"/>'),
                 ("S", '<path d="M4 -17 A23 23 0 0 1 4 17"/>'),
                 ("S", '<path d="M13 -22 A31 31 0 0 1 13 22" opacity="0.6"/>')],
    # A slider and its knob.
    "parameter": [("S", '<path d="M-22 0 H22"/>'), ("F", '<circle cx="8" cy="0" r="9"/>')],
    # A die.
    "random": [("F", '<rect x="-16" y="-16" width="32" height="32" rx="7"/>'),
               ("P", '<circle cx="-7" cy="-7" r="3.6"/><circle cx="0" cy="0" r="3.6"/><circle cx="7" cy="7" r="3.6"/>')],
    # A smooth curve between two points.
    "curve": [("S", '<path d="M-22 16 C-6 16, -6 -16, 22 -16"/>'), ("F", '<circle cx="-22" cy="16" r="4.5"/><circle cx="22" cy="-16" r="4.5"/>')],
    # Two waves of a sine.
    "oscillator": [("S", '<path d="M-24 0 Q-18 -20 -12 0 T0 0 T12 0 T24 0"/>')],
    # A wall, a sound before it, a fainter one past it.
    "occlusion": [("F", '<rect x="-4" y="-21" width="9" height="42" rx="2"/>'),
                  ("S", '<path d="M-20 -12 A16 16 0 0 1 -20 12"/>'),
                  ("S", '<path d="M14 -8 A11 11 0 0 1 14 8" opacity="0.45"/>')],
    # Looking down at the ground.
    "surface": [("S", '<path d="M0 -22 V0 M-8 -8 L0 0 L8 -8"/>'), ("S", '<path d="M-22 10 H22"/>'),
                ("F", '<g stroke="none"><rect x="-17" y="17" width="34" height="4" rx="2" opacity="0.55"/></g>')],
    # A room, ringing.
    "room": [("S", '<path d="M-20 20 V-3 L0 -20 L20 -3 V20 Z"/>'), ("F", '<circle cx="-4" cy="7" r="4"/>'),
             ("S", '<path d="M3 1 A8 8 0 0 1 3 13" stroke-width="4"/>')],
    # A speaker.
    "output": [("F", '<path d="M-22 -8 H-13 L1 -19 V19 L-13 8 H-22 Z" stroke-linejoin="round"/>'),
               ("S", '<path d="M8 -9 A12 12 0 0 1 8 9"/>'), ("S", '<path d="M15 -17 A23 23 0 0 1 15 17"/>')],
}

COLORS = {
    "time": "#17b3a6", "distance": "#1d9fe0", "parameter": "#f0a01c", "random": "#9a5cf0", "curve": "#38bf5c",
    "oscillator": "#ff7a2e", "occlusion": "#6f7fa8", "surface": "#c47838", "room": "#5a68f0", "output": "#ff4fa0",
}


def shade(color, amount):
    """The color mixed towards black (amount < 0) or white (amount > 0)."""
    r, g, b = (int(color[i:i + 2], 16) for i in (1, 3, 5))
    target = 255 if amount > 0 else 0
    a = abs(amount)
    return "#%02x%02x%02x" % tuple(round(c + (target - c) * a) for c in (r, g, b))


def svg(name):
    color = COLORS[name]
    nub_left = name == "output"
    cx = 54 if nub_left else 46
    nub = '<rect x="6" y="31" width="22" height="22" rx="9"/>' if nub_left else '<rect x="72" y="31" width="22" height="22" rx="9"/>'
    body = f'<ellipse cx="{cx}" cy="42" rx="37" ry="36"/>{nub}'
    dark = shade(color, -0.45)
    parts = []
    for kind, element in SYMBOLS[name]:
        if kind == "S":
            parts.append(f'<g fill="none" stroke="{dark}" stroke-opacity="0.45" stroke-width="10" stroke-linecap="round" stroke-linejoin="round">{element}</g>')
            parts.append(f'<g fill="none" stroke="#ffffff" stroke-width="6" stroke-linecap="round" stroke-linejoin="round">{element}</g>')
        elif kind == "F":
            parts.append(f'<g fill="#ffffff" stroke="{dark}" stroke-opacity="0.45" stroke-width="3">{element}</g>')
        else:  # pips etc.: in the icon's color, on white
            parts.append(f'<g fill="{shade(color, -0.2)}">{element}</g>')
    symbols = "\n    ".join(parts)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">
  <defs>
    <linearGradient id="body" x1="0" y1="6" x2="0" y2="78" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#ffffff"/>
      <stop offset="0.3" stop-color="{shade(color, 0.55)}"/>
      <stop offset="1" stop-color="{color}"/>
    </linearGradient>
    <linearGradient id="gloss" x1="0" y1="8" x2="0" y2="42" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.85"/>
      <stop offset="1" stop-color="#ffffff" stop-opacity="0"/>
    </linearGradient>
  </defs>
  <!-- edge -->
  <g fill="{shade(color, -0.35)}" stroke="{shade(color, -0.35)}" stroke-width="3" opacity="0.55">{body}</g>
  <!-- body -->
  <g fill="url(#body)">{body}</g>
  <!-- shine -->
  <ellipse cx="{cx}" cy="22" rx="27" ry="14" fill="url(#gloss)"/>
  <!-- symbol -->
  <g transform="translate({cx} 44)">
    {symbols}
  </g>
</svg>
'''


def main():
    if "--render" not in sys.argv:
        for name in SYMBOLS:
            with open(os.path.join(HERE, name + ".svg"), "w") as f:
                f.write(svg(name))
    import cairosvg
    os.makedirs(OUT, exist_ok=True)
    for file in sorted(os.listdir(HERE)):
        if file.endswith(".svg"):
            cairosvg.svg2png(url=os.path.join(HERE, file), write_to=os.path.join(OUT, file[:-4] + ".png"),
                             output_width=W * SCALE, output_height=H * SCALE)
            print("rendered", file)


if __name__ == "__main__":
    main()
