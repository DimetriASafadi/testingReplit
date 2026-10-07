"""Build shipping Unicode UGUI fonts from licensed, checked-in font sources.

UGUI uses ArabicText's Unicode presentation forms, not OpenType joining.
Missing presentation-form cmap entries are aliases to HarfBuzz's actual GSUB
glyphs, never guessed private-use mappings. ASCII/Latin uses Roboto; remaining
missing characters use packaged DejaVu. No Unity .meta files are generated.
"""
import copy
import hashlib
import json
import pathlib
import unicodedata

import uharfbuzz as hb
from fontTools.fontBuilder import FontBuilder
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCES = ROOT / "tools/font_sources"
RESOURCES = ROOT / "testingReplic/Assets/NewGaza/Resources"
UPM = 2048


def load(name, weight=None):
    font = TTFont(SOURCES / name, recalcTimestamp=False)
    if "fvar" in font:
        axes = {axis.axisTag: axis.defaultValue for axis in font["fvar"].axes}
        axes["wght"] = weight
        font = instantiateVariableFont(font, axes, inplace=True)
    scale_upem(font, UPM)
    return font


def add_joined_unicode(font):
    """Derive connected glyph aliases from documented Unicode decompositions."""
    import io
    stream = io.BytesIO()
    font.save(stream)
    shape_font = hb.Font(hb.Face(stream.getvalue()))
    cmap = dict(font.getBestCmap())
    glyph_order = font.getGlyphOrder()
    additions = {}
    for codepoint in list(range(0xFE80, 0xFEFD)) + list(range(0xFB50, 0xFC00)):
        if codepoint in cmap:
            continue
        decomposition = unicodedata.decomposition(chr(codepoint)).split()
        if not decomposition or decomposition[0] not in (
            "<isolated>", "<final>", "<initial>", "<medial>"
        ):
            continue
        form = decomposition.pop(0)
        letters = "".join(chr(int(value, 16)) for value in decomposition)
        if not all(ord(letter) in cmap for letter in letters):
            continue
        prefix = "\u0640" if form in ("<final>", "<medial>") else ""
        suffix = "\u0640" if form in ("<initial>", "<medial>") else ""
        buffer = hb.Buffer()
        buffer.add_codepoints([ord(c) for c in prefix + letters + suffix])
        buffer.direction, buffer.script, buffer.language = "rtl", "arab", "ar"
        hb.shape(shape_font, buffer)
        selected = [info for info in buffer.glyph_infos
                    if len(prefix) <= info.cluster < len(prefix) + len(letters)]
        if len(selected) == 1 and selected[0].codepoint != 0:
            additions[codepoint] = glyph_order[selected[0].codepoint]
    for table in font["cmap"].tables:
        if table.isUnicode() and table.format in (4, 12):
            table.cmap.update(additions)
    return additions


def build(primary, latin, fallback, arabic_fallback, destination, family):
    fonts = [primary, latin, fallback, arabic_fallback]
    maps = [font.getBestCmap() for font in fonts]
    selected = {}
    selected.update({code: (2, glyph) for code, glyph in maps[2].items()})
    # Tajawal/DejaVu omit Quranic alef-wasla. Use the matching Cairo weight,
    # whose genuine Unicode/GSUB glyphs include this shaper-supported letter.
    for code, glyph in maps[3].items():
        if code not in selected:
            selected[code] = (3, glyph)
    for index in (0,):
        selected.update({code: (index, glyph) for code, glyph in maps[index].items()})
    # Preserve Arabic digits from the Arabic family; Latin text and Western
    # numbers use the requested Roboto, including in mixed Arabic labels.
    selected.update({code: (1, glyph) for code, glyph in maps[1].items()
                     if 0x20 <= code <= 0x24F or 0x1E00 <= code <= 0x1EFF})
    glyphs, metrics = {}, {}

    def include(index, name):
        key = f"f{index}_{name}"
        if key in glyphs:
            return key
        glyph = copy.deepcopy(fonts[index]["glyf"][name])
        glyph.removeHinting()
        glyphs[key] = glyph
        metrics[key] = fonts[index]["hmtx"][name]
        if glyph.isComposite():
            for component in glyph.components:
                component.glyphName = include(index, component.glyphName)
        return key

    cmap = {code: include(index, name) for code, (index, name) in sorted(selected.items())}
    missing = include(0, ".notdef")
    glyphs[".notdef"] = glyphs.pop(missing)
    metrics[".notdef"] = metrics.pop(missing)
    builder = FontBuilder(UPM, isTTF=True)
    builder.setupGlyphOrder([".notdef"] + sorted(glyphs.keys() - {".notdef"}))
    builder.setupCharacterMap(cmap)
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics(metrics)
    # Common line metrics across faces keep existing HUD rectangles stable.
    builder.setupHorizontalHeader(ascent=1900, descent=-520, lineGap=0)
    builder.setupNameTable({
        "familyName": family, "styleName": "Regular", "uniqueFontIdentifier": family + " Unicode UI",
        "fullName": family, "psName": family.replace(" ", ""),
        "version": "Version 1.000",
        "copyright": "Derived from Cairo, Tajawal, Roboto and DejaVu Sans. See accompanying licenses.",
        "licenseDescription": "Source components retain their OFL/DejaVu licenses. See Typography licenses.",
    })
    builder.setupOS2(sTypoAscender=1900, sTypoDescender=-520, sTypoLineGap=0,
                     usWinAscent=2300, usWinDescent=800,
                     usWeightClass=primary["OS/2"].usWeightClass)
    builder.setupPost()
    builder.setupMaxp()
    builder.font["head"].created = builder.font["head"].modified = primary["head"].created
    destination.parent.mkdir(parents=True, exist_ok=True)
    builder.font.recalcTimestamp = False
    builder.save(destination)
    # Reopen the actual shipping asset, not an in-memory intermediate.
    shipped = TTFont(destination)
    actual = shipped.getBestCmap()
    required = "غزة الجديدة إعادة إعمار 50,000 Unity 6 · % / × + = ( ) → ⚠ ★ ٠١٢٣٤٥٦٧٨٩"
    absent = [f"U+{ord(c):04X}" for c in required if not c.isspace() and ord(c) not in actual]
    if absent:
        raise ValueError(f"{destination.name}: missing {absent}")
    return {"file": str(destination.relative_to(ROOT)), "codepoints": len(actual),
            "sha256": hashlib.sha256(destination.read_bytes()).hexdigest()}


def main():
    fallback = load("DejaVuSans.ttf")
    report = {"sources": {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in sorted(SOURCES.glob("*.ttf"))}, "fonts": []}
    for source, weight, path, family in (
        ("Tajawal-Regular.ttf", 400, "NewGazaArabic.ttf", "New Gaza UI Body"),
        ("Tajawal-Medium.ttf", 500, "Typography/Button.ttf", "New Gaza UI Button"),
        ("Cairo.ttf", 700, "Typography/Heading.ttf", "New Gaza UI Heading"),
    ):
        primary = load(source, weight)
        aliases = add_joined_unicode(primary)
        arabic_fallback = load("Cairo.ttf", weight)
        add_joined_unicode(arabic_fallback)
        result = build(primary, load("Roboto.ttf", weight), fallback, arabic_fallback, RESOURCES / path, family)
        result["joiningAliases"] = len(aliases)
        report["fonts"].append(result)
    (SOURCES / "BUILD.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report["fonts"], indent=2))


if __name__ == "__main__":
    main()