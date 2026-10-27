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
    # A Signal (dock rows, nodes, minimap; tinted where it's drawn: blue received, red sent): a triangle going off,
    # outlined, its centre dimmer.
    "signal-mark": '''
  <path d="M8 4.5 L8 27.5 L28 16 Z" fill="#ffffff" fill-opacity="0.45"/>
  <path d="M9.6 7.3 L9.6 24.7 L24.8 16 Z" fill="none" stroke="#ffffff" stroke-width="3.2" stroke-linejoin="round"/>''',
    # The same, pointing the other way: a Signal coming into the graph (one it reacts to).
    "signal-mark-in": '''
  <g transform="translate(32,0) scale(-1,1)">
    <path d="M8 4.5 L8 27.5 L28 16 Z" fill="#ffffff" fill-opacity="0.45"/>
    <path d="M9.6 7.3 L9.6 24.7 L24.8 16 Z" fill="none" stroke="#ffffff" stroke-width="3.2" stroke-linejoin="round"/>
  </g>''',
    # A Variable (tinted with its type's colour): a value, as a ring with a dimmer centre.
    "variable-mark": '''
  <circle cx="16" cy="16" r="13.5" fill="#ffffff" fill-opacity="0.45"/>
  <circle cx="16" cy="16" r="11.75" fill="none" stroke="#ffffff" stroke-width="3.5"/>''',
    # A note on the graph (its mark on the minimap): a pencil.
    "note-pencil": '''
  <g fill="#ffffff">
    <path d="M21.5 4.5 L27.5 10.5 L12 26 L6 20 Z"/>
    <path d="M23.2 2.8 a2.5 2.5 0 0 1 3.5 0 l2.5 2.5 a2.5 2.5 0 0 1 0 3.5 l-0.9 0.9 -6 -6 Z"/>
    <path d="M4.8 21.4 L10.6 27.2 L2.5 29.5 Z"/>
  </g>''',
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
    # An Audio graph: an audio signal flowing through a processing node.
    "audio": '''
  <rect x="2" y="11" width="7" height="10" rx="2" fill="{c}"/>
  <rect x="23" y="11" width="7" height="10" rx="2" fill="{c}"/>
  <path d="M9 16 H12 M20 16 H23" stroke="{c}" stroke-width="2.2" stroke-linecap="round"/>
  <path d="M12 16 C13 16, 13 7, 16 7 S19 25, 20 16" fill="none" stroke="{c}" stroke-width="2.5" stroke-linecap="round"/>''',
    # A UI graph: a UI Toolkit panel connected to a compact graph node.
    "ui": '''
  <rect x="2.5" y="3" width="19" height="26" rx="2.5" fill="none" stroke="{c}" stroke-width="2.3"/>
  <path d="M3 9 H21" stroke="{c}" stroke-width="2.3"/>
  <rect x="6" y="13" width="11" height="5" rx="1.3" fill="{c}"/>
  <path d="M6 22 H16" stroke="{c}" stroke-width="2" stroke-linecap="round"/>
  <rect x="22" y="11" width="8" height="8" rx="2" fill="{c}"/>
  <path d="M21.5 15 H22" stroke="{c}" stroke-width="2.3"/>''',
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
    # An AI Controller Graph: a gamepad, with the spark of something thinking above it.
    "ai-controller": '''
  <path d="M8.5 12 H23.5 C28 12, 30.5 15.5, 30.2 21 C29.9 26.5, 26 28, 23.5 25.2 L21.2 22.6 H10.8 L8.5 25.2 C6 28, 2.1 26.5, 1.8 21 C1.5 15.5, 4 12, 8.5 12 Z" fill="none" stroke="{c}" stroke-width="2.4" stroke-linejoin="round"/>
  <path d="M9 15.6 V21.4 M6.1 18.5 H11.9" stroke="{c}" stroke-width="2.2" stroke-linecap="round"/>
  <circle cx="21.6" cy="17" r="1.8" fill="{c}"/>
  <circle cx="25" cy="20.2" r="1.8" fill="{c}"/>
  <path d="M16 0.8 L17.4 4.4 L21 5.8 L17.4 7.2 L16 10.8 L14.6 7.2 L11 5.8 L14.6 4.4 Z" fill="{c}"/>''',
    # A Mesh Graph: a wireframe cube, one corner a node.
    "mesh": '''
  <path d="M16 3 L28 9.5 L28 22.5 L16 29 L4 22.5 L4 9.5 Z" fill="none" stroke="{c}" stroke-width="2.3" stroke-linejoin="round"/>
  <path d="M4 9.5 L16 16 L28 9.5 M16 16 V29" fill="none" stroke="{c}" stroke-width="2" stroke-linejoin="round"/>
  <circle cx="16" cy="16" r="3.2" fill="{c}"/>''',
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
