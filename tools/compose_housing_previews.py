#!/usr/bin/env python3
"""Label the actual delivered FBX renders with connected Arabic text."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, features
import arabic_reshaper
from bidi.algorithm import get_display
from render_housing_assets import MASTERS, STAGES, STAGE_EXAMPLES

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "exports/housing-assets"
FONT = ROOT / "testingReplic/Assets/NewGaza/Resources/NewGazaArabic.ttf"
LABELS = (
    "منزل صغير بسقف قرميدي", "منزل عائلي متوسط", "فيلا سكنية حديثة",
    "عمارة بأربعة طوابق", "عمارة بستة طوابق", "مجمع سكني",
    "عمارة حديثة بواجهات زجاجية", "عمارة حجرية تقليدية",
    "برج سكني", "فيلا ساحلية",
)
STAGE_LABELS = ("التأسيس", "الهيكل والبناء", "التشطيب", "المبنى المكتمل")

def label(draw, box, text, size, color):
    font = ImageFont.truetype(str(FONT), size)
    options = {"direction": "rtl", "language": "ar"} if features.check_feature("raqm") else {}
    if not options:
        text = get_display(arabic_reshaper.reshape(text))
    draw.text((box[2], box[1]), text, font=font, fill=color, anchor="rt", **options)


def sheet(filename, title, keys, captions, columns, card_width=400, card_height=392):
    width = columns * card_width
    rows = (len(keys) + columns - 1) // columns
    canvas = Image.new("RGB", (width, 94 + rows * card_height + 62), "#152c3d")
    draw = ImageDraw.Draw(canvas)
    label(draw, (20, 24, width - 24, 70), title, 32, "#f7ebd6")
    for index, (key, caption) in enumerate(zip(keys, captions)):
        x = index % columns * card_width
        y = 94 + index // columns * card_height
        image = Image.open(OUTPUT / "previews" / (key + ".png")).convert("RGB")
        image.thumbnail((card_width - 16, card_height - 53), Image.Resampling.LANCZOS)
        canvas.paste(image, (x + (card_width - image.width) // 2, y))
        label(draw, (x + 12, y + card_height - 46, x + card_width - 12, y + card_height),
              caption, 24, "#f7ebd6")
    label(draw, (20, canvas.height - 48, width - 24, canvas.height),
          "معاينات ملفات FBX الفعلية باستخدام Blender — ليست لقطات من Unity", 23, "#b9cdda")
    canvas.save(OUTPUT / filename)
    print(OUTPUT / filename)


if __name__ == "__main__":
    sheet("Housing-Reference-Families.png", "مباني نيو غزة — مستوحاة من المرجع",
          [master + "_final" for master in MASTERS], LABELS, 5)
    sheet("Housing-Construction-Stages.png", "من التأسيس إلى المبنى المكتمل",
          [master + "_" + stage for master in STAGE_EXAMPLES for stage in STAGES],
          [caption for _ in STAGE_EXAMPLES for caption in STAGE_LABELS], 4)