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

# Window tab icons, one per graph type (and the IO Dock and the State Machine window): drawn in {c}, written twice,
# grey for the dark editor skin ("tab-NAME") and dark grey for the light one ("tab-NAME-light"), rendered at 64 px.
TAB_COLORS = {"": "#c8c8c8", "-light": "#3a3a3a"}
TAB_ICONS = {
    # An object's graph: two nodes, wired.
    "object": '''
  <rect x="2" y="4" width="12" height="10" rx="2.5" fill="{c}"/>
  <rect x="18" y="18" width="12" height="10" rx="2.5" fill="{c}"/>
  <path d="M14 9 C21 9, 11 23, 18 23" fill="none" stroke="{c}" stroke-width="2.5" stroke-linecap="round"/>''',
    # A Singleton's graph: one node, inside the ring of the whole game.
    "singleton": '''
  <circle cx="16" cy="16" r="12.5" fill="none" stroke="{c}" stroke-width="2.5"/>
  <rect x="10" y="11" width="12" height="10" rx="2.5" fill="{c}"/>''',
    # A Sound Cue: a shaped wave between two nodes.
    "sound-cue": '''
  <path d="M7 16 C10 4, 14 4, 16 16 S22 28, 25 16" fill="none" stroke="{c}" stroke-width="3" stroke-linecap="round"/>
  <circle cx="5" cy="16" r="4" fill="{c}"/>
  <circle cx="27" cy="16" r="4" fill="{c}"/>''',
    # A Timeline Graph: clips on tracks, and the playhead.
    "timeline": '''
  <rect x="2" y="8" width="11" height="5" rx="1.5" fill="{c}"/>
  <rect x="7" y="15" width="10" height="5" rx="1.5" fill="{c}"/>
  <rect x="3" y="22" width="13" height="5" rx="1.5" fill="{c}"/>
  <rect x="24" y="15" width="6" height="5" rx="1.5" fill="{c}"/>
  <path d="M21 6 V30" stroke="{c}" stroke-width="2" stroke-linecap="round"/>
  <path d="M17.5 2 H24.5 L21 6.5 Z" fill="{c}"/>''',
    # A State Machine: states, one of them current, and the ways between them.
    "state-machine": '''
  <circle cx="8" cy="8" r="5" fill="{c}"/>
  <circle cx="24" cy="8" r="4.5" fill="none" stroke="{c}" stroke-width="2.5"/>
  <circle cx="16" cy="24" r="4.5" fill="none" stroke="{c}" stroke-width="2.5"/>
  <path d="M13.5 8 H16.5" stroke="{c}" stroke-width="2.2" stroke-linecap="round"/>
  <path d="M16 4.8 L19.5 8 L16 11.2 Z" fill="{c}"/>
  <path d="M22 12.5 L19.5 17" stroke="{c}" stroke-width="2.2" stroke-linecap="round"/>
  <path d="M16.6 15.6 L18.3 20 L21.8 17.4 Z" fill="{c}"/>
  <path d="M12.5 20.5 L10.5 15.5" stroke="{c}" stroke-width="2.2" stroke-linecap="round"/>
  <path d="M7.9 16.6 L9.7 12.2 L13.4 15 Z" fill="{c}"/>''',
    # The IO Dock: an object's rows, each with its pin.
    "io-dock": '''
  <rect x="3" y="5" width="18" height="5" rx="2.5" fill="{c}"/>
  <circle cx="26.5" cy="7.5" r="3" fill="{c}"/>
  <rect x="3" y="13.5" width="18" height="5" rx="2.5" fill="{c}"/>
  <circle cx="26.5" cy="16" r="3" fill="none" stroke="{c}" stroke-width="2"/>
  <rect x="3" y="22" width="18" height="5" rx="2.5" fill="{c}"/>
  <circle cx="26.5" cy="24.5" r="3" fill="{c}"/>''',
    # The IO Dock's sections, as tabs inside a Behavior's.
    # Variables: values, as the ringed dots their rows carry.
    "dock-variables": '''
  <circle cx="10" cy="10" r="6" fill="none" stroke="{c}" stroke-width="2.5"/>
  <circle cx="10" cy="10" r="2.2" fill="{c}"/>
  <circle cx="22" cy="22" r="6" fill="none" stroke="{c}" stroke-width="2.5"/>
  <circle cx="22" cy="22" r="2.2" fill="{c}"/>''',
    # Signals: a bolt, sent.
    "dock-signals": '''
  <path d="M18 2 L6 18 H14.5 L12 30 L26 12 H17.5 Z" fill="{c}"/>''',
    # Properties: settings, as sliders.
    "dock-properties": '''
  <path d="M4 8 H28 M4 16 H28 M4 24 H28" stroke="{c}" stroke-width="2.5" stroke-linecap="round"/>
  <circle cx="20" cy="8" r="3.5" fill="{c}"/>
  <circle cx="10" cy="16" r="3.5" fill="{c}"/>
  <circle cx="23" cy="24" r="3.5" fill="{c}"/>''',
    # More: what the Behavior adds itself (a Brain's slots, a vehicle's seats).
    "dock-more": '''
  <rect x="3" y="3" width="11" height="11" rx="2.5" fill="{c}"/>
  <rect x="18" y="3" width="11" height="11" rx="2.5" fill="none" stroke="{c}" stroke-width="2.5"/>
  <rect x="3" y="18" width="11" height="11" rx="2.5" fill="none" stroke="{c}" stroke-width="2.5"/>
  <rect x="18" y="18" width="11" height="11" rx="2.5" fill="{c}"/>''',
}


def main():
    if "--render" not in sys.argv:
        for name, body in ICONS.items():
            with open(os.path.join(HERE, name + ".svg"), "w") as f:
                f.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">{body}\n</svg>\n')
        for name, body in TAB_ICONS.items():
            for suffix, color in TAB_COLORS.items():
                with open(os.path.join(HERE, f"tab-{name}{suffix}.svg"), "w") as f:
                    f.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">{body.replace("{c}", color)}\n</svg>\n')
    import cairosvg
    os.makedirs(OUT, exist_ok=True)
    for file in sorted(os.listdir(HERE)):
        if file.endswith(".svg"):
            cairosvg.svg2png(url=os.path.join(HERE, file), write_to=os.path.join(OUT, file[:-4] + ".png"),
                             output_width=SIZE * (2 if file.startswith("tab-") else SCALE),
                             output_height=SIZE * (2 if file.startswith("tab-") else SCALE))
            print("rendered", file)


if __name__ == "__main__":
    main()
