#!/usr/bin/env python3
"""
White browser icons: writes each icon as an SVG here (Icons~, which Unity skips), then renders it to a PNG in
../Resources/Textures for the editor to load.

    pip install cairosvg
    python3 make_icons.py          # rewrites every SVG, then renders every SVG
    python3 make_icons.py --render # only renders the SVGs (after editing them by hand)
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Resources", "Textures")
SIZE, SCALE = 32, 4  # drawn on a 32 grid, rendered at 128 px so they stay crisp small and large

ICONS = {
    # An audio clip: a recorded waveform, as bars.
    "audio-clip-icon": '''
  <g fill="#ffffff">
    <rect x="3" y="12" width="3" height="8" rx="1.5"/>
    <rect x="8" y="7" width="3" height="18" rx="1.5"/>
    <rect x="13" y="3" width="3" height="26" rx="1.5"/>
    <rect x="18" y="9" width="3" height="14" rx="1.5"/>
    <rect x="23" y="5" width="3" height="22" rx="1.5"/>
    <rect x="28" y="12" width="2.5" height="8" rx="1.25"/>
  </g>''',
    # A Sound Cue: a shaped wave between two nodes (a sound built in a graph).
    "sound-cue-icon": '''
  <path d="M7 16 C10 4, 14 4, 16 16 S22 28, 25 16" fill="none" stroke="#ffffff" stroke-width="3" stroke-linecap="round"/>
  <circle cx="5" cy="16" r="4" fill="#ffffff"/>
  <circle cx="27" cy="16" r="4" fill="#ffffff"/>''',
}


def main():
    if "--render" not in sys.argv:
        for name, body in ICONS.items():
            with open(os.path.join(HERE, name + ".svg"), "w") as f:
                f.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">{body}\n</svg>\n')
    import cairosvg
    os.makedirs(OUT, exist_ok=True)
    for file in sorted(os.listdir(HERE)):
        if file.endswith(".svg"):
            cairosvg.svg2png(url=os.path.join(HERE, file), write_to=os.path.join(OUT, file[:-4] + ".png"),
                             output_width=SIZE * SCALE, output_height=SIZE * SCALE)
            print("rendered", file)


if __name__ == "__main__":
    main()
