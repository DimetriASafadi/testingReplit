---
name: Arabic font coverage
description: Why font checks must include shaped Arabic and mixed-script interface symbols.
---

Distinguish native browser shaping from the legacy Unity text pipeline when applying the checks below.

The standalone 2D game should use Cairo Black with a white outline, a light black shadow and a raised text effect.

**Why:** The user explicitly replaced the previously supplied Elgharib font with Cairo Black and requested this treatment; “Black” refers to the actual heavy face, not black-colored text. After seeing the initial styling, the user requested slightly smaller text for visual comfort, then clarified that numbers must retain their earlier size.

**How to apply:** Use a genuine 900-weight face for both browser UI and canvas labels. Reduce written labels only; preserve the original numeral size in balances, prices, timers, percentages and mixed text, including canvas labels. Compare actual digit glyph measurements rather than assuming a smaller container font preserves them. Keep icons and touch targets unchanged, along with the navy/white palette and Western digits. Scale outline and shadow strength down for small counters so the effect does not obscure characters or make phone controls harder to read. The visual depth is typography, not a change to the 2D rendering pipeline.

**Why:** The Unity checks concern text already converted to Unicode presentation forms. A browser can correctly join a Unicode Arabic font through its OpenType GSUB tables even when its cmap contains very few presentation-form code points. Requiring Unity's compatibility aliases for a browser font would reject a valid font unnecessarily.

**How to apply:** Keep the Unity-specific presentation-form checks for Unity. For web fonts, inspect ordinary Arabic, digits and punctuation plus joining tables, then verify the actual loaded face and connected text in the browser. Do not remap a valid Unicode font to private-use characters.

Verify the packaged font against the output of Arabic shaping and mixed-script UI strings, not just Arabic source characters.

**Why:** The Noto Naskh Arabic file obtained from the noto-fonts hinted Arabic family contained Arabic glyphs but omitted Latin letters and common UI symbols such as percent, slash and parentheses. Arabic text alone looked supported while the economy interface would have missing glyphs. DejaVu was originally used for full coverage; the user's later typography requirement selected Cairo titles, Tajawal text/buttons and Roboto Latin/numbers, keeping DejaVu only as a glyph fallback.

**How to apply:** For future font changes, check cmap coverage with fc-query and run the Unity Arabic validation menu. Include connected presentation forms, lam-alef, diacritics, digits, punctuation, percentages and Latin labels. Keep the replacement font's redistribution license with it. Do not rely on a device-installed fallback font for shipping UI.

Retain the existing UGUI/RTL text system for font-only work rather than replacing the whole text stack. Upstream Arabic OpenType support alone does not guarantee compatibility with its manually shaped Unicode presentation forms.

**Why:** The supplied Tajawal lacked some presentation mappings and alef-wasla; Cairo also needed joining aliases. The shipping faces deliberately use aliases derived from the font's actual HarfBuzz/GSUB output, with Roboto Latin incorporated into the same face so mixed labels do not require UI reconstruction.

**How to apply:** Do not swap in an unprocessed upstream TTF without checking the game's shaped output first. Use actual joining glyphs, not guessed private-use mappings. Include a matching-weight Arabic fallback for rare letters; distinguish tested Unicode coverage from unsupported color emoji or all of Unicode.

Do not infer Unicode compatibility from a font preview, or invent an Arabic-to-private-use conversion from unlabeled glyph artwork.

**Why:** A legacy Arabic font contained recognizable connected letter artwork but mapped it only to private-use code points; it had neither semantic Arabic glyph names nor OpenType joining tables. Its appearance did not establish a reliable mapping for the game's shaped text.

**How to apply:** Inspect the font's character mappings before replacing a working font. For private-use-only fonts, require a Unicode-encoded version or an authoritative, version-matched encoding table. Clearly report that font adoption is blocked instead of silently substituting another face.

Check Pillow's `raqm` capability before composing Arabic proof sheets.

**Why:** The managed Pillow installation in this workspace lacked RAQM. Drawing Arabic directly produced disconnected, left-to-right labels despite using the game's Arabic font.

**How to apply:** Where RAQM is absent, reshape Arabic and apply bidirectional layout before drawing. Inspect the finished exported sheet, including mixed Arabic/Latin footnotes.

The 2D game's displayed numbers must use Western digits (0–9), while labels remain Arabic.

**Why:** The user explicitly requested replacing Arabic-Indic digits throughout the game, not translating the interface.

**How to apply:** Force the Latin numbering system regardless of browser locale. Include balances, timers, percentages, compact income badges, page counters and digits inside building names or help text. Keep the chosen Arabic font and check its Western digit glyphs; this is presentation only, not a change to saved amounts or gameplay.