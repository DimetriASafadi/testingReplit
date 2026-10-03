"""Check actual shipped font binaries against the game's actual C# shaped output."""
import json
import pathlib
import re
import sys

from fontTools.ttLib import TTFont

ROOT = pathlib.Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "testingReplic/Assets/NewGaza/Resources"
samples = json.loads(pathlib.Path(sys.argv[1]).read_text())
source = (ROOT / "testingReplic/Assets/NewGaza/UI/ArabicText.cs").read_text()
forms = {int(value, 16) for value in re.findall(r"\\u([0-9A-Fa-f]{4})", source)
         if 0xFB50 <= int(value, 16) <= 0xFEFC}
assertions = 0
for name, weight in (("NewGazaArabic.ttf", 400), ("Typography/Button.ttf", 500), ("Typography/Heading.ttf", 700)):
    font = TTFont(RESOURCES / name)
    cmap = font.getBestCmap()
    for text in samples.values():
        for character in text:
            if character.isspace():
                continue
            assert ord(character) in cmap, (name, "missing", character, hex(ord(character)))
            assertions += 1
    for code in forms | set(range(0xFEF5, 0xFEFD)):
        assert code in cmap, (name, "missing joined form", hex(code))
        # Font sources supply supported Arabic themselves; DejaVu is only a
        # Unicode safety fallback, not a hidden replacement for the new font.
        if 0xFE80 <= code <= 0xFEFC:
            assert cmap[code].startswith("f0_"), (name, "Arabic unexpectedly fallback", hex(code))
        assertions += 1
    for character in "50,000 Unity 6 / 100%":
        assert cmap[ord(character)].startswith("f1_"), (name, "not Roboto", character)
        assertions += 1
    assert font["OS/2"].usWeightClass == weight, (name, "wrong weight")
    assert "fvar" not in font, (name, "variable font not flattened")
    assert font["head"].unitsPerEm == 2048
    for glyph_name in font.getGlyphOrder():
        glyph = font["glyf"][glyph_name]
        if glyph.isComposite():
            for component in glyph.components:
                assert component.glyphName in font["glyf"], (name, "broken composite")
                assertions += 1
    print("PASS", name, "weight", weight, "mapped Unicode scalars", len(cmap))
print("PASS shipping font coverage/provenance/composites /", assertions, "assertions")