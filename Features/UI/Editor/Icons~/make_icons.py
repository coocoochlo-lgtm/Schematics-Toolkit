#!/usr/bin/env python3
"""Render the UI Toolkit hierarchy's white element icons into the editor Resources folder."""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Resources", "UIElementIcons")
SIZE, SCALE = 32, 4

ICONS = {
    "ui-container": '''<rect x="4" y="5" width="24" height="22" rx="3" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M4 11h24" stroke="#fff" stroke-width="2.5"/>''',
    "ui-button": '''<rect x="3" y="7" width="26" height="18" rx="3" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M10 16h12" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/>''',
    "ui-text": '''<path d="M5 8h22M5 14h17M5 20h20M5 26h12" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/>''',
    "ui-input": '''<rect x="3" y="7" width="26" height="18" rx="2" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M10 11v10" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/>''',
    "ui-toggle": '''<rect x="4" y="5" width="22" height="22" rx="3" fill="none" stroke="#fff" stroke-width="2.5"/><path d="m9 16 4 4 8-9" fill="none" stroke="#fff" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"/>''',
    "ui-slider": '''<path d="M4 16h24" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/><circle cx="19" cy="16" r="5" fill="#fff"/>''',
    "ui-scroll-view": '''<rect x="4" y="3" width="24" height="26" rx="2" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M8 9h13M8 15h13M8 21h9" stroke="#fff" stroke-width="2" stroke-linecap="round"/><path d="M24 7v7" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/>''',
    "ui-image": '''<rect x="3" y="5" width="26" height="22" rx="2" fill="none" stroke="#fff" stroke-width="2.5"/><circle cx="21" cy="11" r="2.5" fill="#fff"/><path d="m5 24 8-9 5 5 3-3 7 8" fill="none" stroke="#fff" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>''',
    "ui-foldout": '''<path d="m8 6 12 10-12 10z" fill="#fff"/><path d="M23 9h6M23 16h6M23 23h6" stroke="#fff" stroke-width="2" stroke-linecap="round"/>''',
    "ui-list": '''<circle cx="6" cy="8" r="2" fill="#fff"/><circle cx="6" cy="16" r="2" fill="#fff"/><circle cx="6" cy="24" r="2" fill="#fff"/><path d="M12 8h16M12 16h16M12 24h16" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/>''',
    "ui-dropdown": '''<rect x="3" y="6" width="26" height="20" rx="2" fill="none" stroke="#fff" stroke-width="2.5"/><path d="m19 13 4 4 4-4" fill="none" stroke="#fff" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>''',
}


def main():
    os.makedirs(OUT, exist_ok=True)
    try:
        import cairosvg
    except ImportError:
        cairosvg = None
    for name, body in ICONS.items():
        svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">{body}</svg>'
        with open(os.path.join(HERE, name + ".svg"), "w", encoding="utf-8") as file:
            file.write(svg + "\n")
        if cairosvg is not None:
            cairosvg.svg2png(bytestring=svg.encode(), write_to=os.path.join(OUT, name + ".png"),
                             output_width=SIZE * SCALE, output_height=SIZE * SCALE)
        else:
            render_with_pillow(name, os.path.join(OUT, name + ".png"))


def render_with_pillow(name, path):
    from PIL import Image, ImageDraw

    scale = SCALE
    image = Image.new("RGBA", (SIZE * scale, SIZE * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    white = (255, 255, 255, 255)

    def xy(coords):
        return tuple(round(value * scale) for value in coords)

    def line(coords, width=2.5):
        draw.line(xy(coords), fill=white, width=round(width * scale), joint="curve")

    def rect(box, radius=0, width=2.5):
        draw.rounded_rectangle(xy(box), radius=radius * scale, outline=white, width=round(width * scale))

    if name == "ui-container":
        rect((4, 5, 28, 27), 3); line((4, 11, 28, 11))
    elif name == "ui-button":
        rect((3, 7, 29, 25), 3); line((10, 16, 22, 16))
    elif name == "ui-text":
        for y, end in ((8, 27), (14, 22), (20, 25), (26, 17)):
            line((5, y, end, y))
    elif name == "ui-input":
        rect((3, 7, 29, 25), 2); line((10, 11, 10, 21))
    elif name == "ui-toggle":
        rect((4, 5, 26, 27), 3); line((9, 16, 13, 20, 21, 11), 2.8)
    elif name == "ui-slider":
        line((4, 16, 28, 16)); draw.ellipse(xy((14, 11, 24, 21)), fill=white)
    elif name == "ui-scroll-view":
        rect((4, 3, 28, 29), 2); line((8, 9, 21, 9), 2); line((8, 15, 21, 15), 2); line((8, 21, 17, 21), 2); line((24, 7, 24, 14))
    elif name == "ui-image":
        rect((3, 5, 29, 27), 2); draw.ellipse(xy((18.5, 8.5, 23.5, 13.5)), fill=white); line((5, 24, 13, 15, 18, 20, 21, 17, 27, 24))
    elif name == "ui-foldout":
        draw.polygon([xy((8, 6)), xy((20, 16)), xy((8, 26))], fill=white); line((23, 9, 29, 9), 2); line((23, 16, 29, 16), 2); line((23, 23, 29, 23), 2)
    elif name == "ui-list":
        for y in (8, 16, 24):
            draw.ellipse(xy((4, y - 2, 8, y + 2)), fill=white); line((12, y, 28, y))
    elif name == "ui-dropdown":
        rect((3, 6, 29, 26), 2); line((19, 13, 23, 17, 27, 13))
    image.save(path)


if __name__ == "__main__":
    main()
