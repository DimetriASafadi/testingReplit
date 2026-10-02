---
name: Arabic font coverage
description: Why font checks must include shaped Arabic and mixed-script interface symbols.
---

Verify the packaged font against the output of Arabic shaping and mixed-script UI strings, not just Arabic source characters.

**Why:** The Noto Naskh Arabic file obtained from the noto-fonts hinted Arabic family contained Arabic glyphs but omitted Latin letters and common UI symbols such as percent, slash and parentheses. Arabic text alone looked supported while the economy interface would have missing glyphs. A full-coverage DejaVu Sans font was selected instead.

**How to apply:** For future font changes, check cmap coverage with fc-query and run the Unity Arabic validation menu. Include connected presentation forms, lam-alef, diacritics, digits, punctuation, percentages and Latin labels. Keep the replacement font's redistribution license with it. Do not rely on a device-installed fallback font for shipping UI.

Do not infer Unicode compatibility from a font preview, or invent an Arabic-to-private-use conversion from unlabeled glyph artwork.

**Why:** A legacy Arabic font contained recognizable connected letter artwork but mapped it only to private-use code points; it had neither semantic Arabic glyph names nor OpenType joining tables. Its appearance did not establish a reliable mapping for the game's shaped text.

**How to apply:** Inspect the font's character mappings before replacing a working font. For private-use-only fonts, require a Unicode-encoded version or an authoritative, version-matched encoding table. Clearly report that font adoption is blocked instead of silently substituting another face.

Check Pillow's `raqm` capability before composing Arabic proof sheets.

**Why:** The managed Pillow installation in this workspace lacked RAQM. Drawing Arabic directly produced disconnected, left-to-right labels despite using the game's Arabic font.

**How to apply:** Where RAQM is absent, reshape Arabic and apply bidirectional layout before drawing. Inspect the finished exported sheet, including mixed Arabic/Latin footnotes.