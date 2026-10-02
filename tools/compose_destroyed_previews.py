#!/usr/bin/env python3
"""Compose labelled contact sheets from the real FBX offline previews."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, features
import arabic_reshaper
from bidi.algorithm import get_display

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "exports/destroyed-assets"
FONT = ROOT / "testingReplic/Assets/NewGaza/Resources/NewGazaArabic.ttf"
LABELS = {
    "shujaiya": "الشجاعية — منزل عائلي", "tuffah": "التفاح — بيت متلاصق",
    "sheikh_radwan": "الشيخ رضوان — سكن عائلي", "daraj": "الدرج — واجهة سكنية وتجارية",
    "karama": "الكرامة — عمارة سكنية", "old_city": "البلدة القديمة — مبنى بأقواس",
    "nasr": "النصر — هيكل عمارة", "sabra": "الصبرة — سكن متلاصق",
    "zeitoun": "الزيتون — مبنى سكني", "rimal": "الرمال — عمارة بشرفات",
    "tel_al_hawa": "تل الهوا — برج متضرر", "sheikh_ijlin": "الشيخ عجلين — مبنى ساحلي",
    "rashid": "الرشيد — مبنى سياحي ساحلي", "mosque": "مسجد مهدّم",
    "school": "مدرسة مدمّرة", "clinic": "مبنى صحي مدمّر", "civic": "مبنى خدمات مهدّم",
    "wall": "جدار مكسور", "car": "مركبة محترقة", "crater": "حفرة وركام", "debris": "أنقاض وخرسانة مكسورة",
}
GROUPS = (
    ("Regional-East-Central.png", "أطلال الأحياء — السكن العائلي والمناطق الوسطى",
     ("shujaiya", "tuffah", "sheikh_radwan", "daraj", "karama", "old_city")),
    ("Regional-Urban-Coast.png", "أطلال الأحياء — العمران والمناطق الغربية والساحل",
     ("nasr", "sabra", "zeitoun", "rimal", "tel_al_hawa", "sheikh_ijlin", "rashid")),
    ("Destroyed-Objects.png", "المباني الخدمية والعناصر المدمّرة",
     ("mosque", "school", "clinic", "civic", "wall", "car", "crater", "debris")),
)


def label(draw, box, text, size, color):
    font = ImageFont.truetype(str(FONT), size)
    options = {"direction": "rtl", "language": "ar"} if features.check_feature("raqm") else {}
    if not options:
        text = get_display(arabic_reshaper.reshape(text))
    draw.text((box[2], box[1]), text, font=font, fill=color, anchor="rt", **options)


for name, title, keys in GROUPS:
    columns = 3
    width, card_height, top, bottom = 1440, 455, 90, 66
    rows = (len(keys) + columns - 1) // columns
    canvas = Image.new("RGB", (width, top + rows * card_height + bottom), "#142332")
    draw = ImageDraw.Draw(canvas)
    label(draw, (20, 22, width - 24, 60), title, 32, "#f1e6ce")
    for index, key in enumerate(keys):
        x, y = (index % columns) * 480 + 10, top + (index // columns) * card_height
        source = OUTPUT / "previews" / ("ruin_" + key + ".png")
        image = Image.open(source).convert("RGB")
        image.thumbnail((460, 394), Image.Resampling.LANCZOS)
        canvas.paste(image, (x + (460 - image.width) // 2, y))
        label(draw, (x + 8, y + 398, x + 450, y + 450), LABELS[key], 25, "#f1e6ce")
    label(draw, (20, canvas.height - 48, width - 24, canvas.height),
          "معاينة خارج Unity ببرنامج Blender — هذه المجسّمات مدمّرة فقط", 23, "#a9bdc8")
    canvas.save(OUTPUT / name)
    print(OUTPUT / name)