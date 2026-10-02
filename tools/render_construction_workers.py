#!/usr/bin/env python3
"""CPU-render real production-crew mesh snapshots from the source test fixture.

No substitute people are authored here. Vertices, triangles and material colors
come from executing CityConstructionCrew's actual Update and phase logic.
"""
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw
from compose_housing_previews import label

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "exports/housing-assets"
WIDTH, HEIGHT, TOP = 600, 560, 65
CAPTIONS = ("عمّال التأسيس", "عمّال البناء", "عمّال التشطيب")
LIGHT = (0.35, 0.8, -0.45)


def projection(point):
    x, y, z = point
    return (0.8 * x + 0.6 * z, 0.34 * (-0.6 * x + 0.8 * z) - 0.94 * y,
            -0.6 * x - 0.4 * y + 0.8 * z)


def shaded_color(color, a, b, c):
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    normal = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2],
              u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(n * n for n in normal))
    diffuse = abs(sum(n * light for n, light in zip(normal, LIGHT))) / max(length, 1e-12)
    brightness = min(1.12, 0.65 + 0.45 * diffuse)
    return tuple(max(0, min(255, round(channel * 255 * brightness))) for channel in color[:3])


def main():
    data = json.loads((OUT / "Construction-Crew-Frames.json").read_text())
    phases = data["phases"]
    points = []
    for phase in phases:
        for frame in phase["frames"]:
            values = frame["vertices"]
            points.extend(projection(values[i:i + 3]) for i in range(0, len(values), 3))
    low = (min(p[0] for p in points), min(p[1] for p in points))
    high = (max(p[0] for p in points), max(p[1] for p in points))
    scale = min((WIDTH - 68) / (high[0] - low[0]), (HEIGHT - TOP - 130) / (high[1] - low[1]))
    center = ((low[0] + high[0]) / 2, (low[1] + high[1]) / 2)
    panels = []
    for index in range(min(len(phase["frames"]) for phase in phases)):
        image = Image.new("RGB", (WIDTH * 3, HEIGHT), "#153044")
        draw = ImageDraw.Draw(image)
        for column, phase in enumerate(phases):
            left = column * WIDTH
            draw.rectangle((left + 8, TOP, left + WIDTH - 8, HEIGHT - 60), fill="#d4caba")
            label(draw, (left + 18, 20, left + WIDTH - 18, TOP), CAPTIONS[column], 30, "#f4e9d5")
            values = phase["frames"][index]["vertices"]
            vertices = [values[i:i + 3] for i in range(0, len(values), 3)]
            projected = [projection(point) for point in vertices]
            faces = []
            for material_index, indices in enumerate(phase["trianglesByMaterial"]):
                color = phase["materialColors"][material_index]
                for offset in range(0, len(indices), 3):
                    tri = indices[offset:offset + 3]
                    faces.append((sum(projected[i][2] for i in tri) / 3, tri, color))
            for _, tri, color in sorted(faces, reverse=True):
                polygon = [
                    (left + WIDTH / 2 + (projected[i][0] - center[0]) * scale,
                     TOP + (HEIGHT - TOP - 60) / 2 + (projected[i][1] - center[1]) * scale)
                    for i in tri
                ]
                draw.polygon(polygon, fill=shaded_color(color, *(vertices[i] for i in tri)))
        label(draw, (20, HEIGHT - 43, WIDTH * 3 - 22, HEIGHT),
              "حركة المجسّمات من مصدر اللعبة — معاينة CPU، وليست تسجيلًا من Unity", 25, "#c1d5de")
        panels.append(image)
    panels[0].save(OUT / "Construction-Workers-Source.png")
    panels[0].save(OUT / "Construction-Workers-Source.gif", save_all=True,
                   append_images=panels[1:], duration=250, loop=0, optimize=False)
    print(f"Rendered {len(panels)} source-executed frames, three phase-aware crews per frame.")


if __name__ == "__main__":
    main()