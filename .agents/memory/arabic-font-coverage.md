---
name: Arabic font coverage
description: Why font checks must include shaped Arabic and mixed-script interface symbols.
---

Verify the packaged font against the output of Arabic shaping and mixed-script UI strings, not just Arabic source characters.

**Why:** The Noto Naskh Arabic file obtained from the noto-fonts hinted Arabic family contained Arabic glyphs but omitted Latin letters and common UI symbols such as percent, slash and parentheses. Arabic text alone looked supported while the economy interface would have missing glyphs. A full-coverage DejaVu Sans font was selected instead.

**How to apply:** For future font changes, check cmap coverage with fc-query and run the Unity Arabic validation menu. Include connected presentation forms, lam-alef, diacritics, digits, punctuation, percentages and Latin labels. Keep the replacement font's redistribution license with it. Do not rely on a device-installed fallback font for shipping UI.