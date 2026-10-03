# Native UI font build inputs

These inputs live outside Unity Assets; only generated shipping faces are imported.

- `Tajawal-Regular.ttf`, `Tajawal-Medium.ttf`: user-provided Google Fonts archive.
- `Cairo.ttf`: https://raw.githubusercontent.com/google/fonts/main/ofl/cairo/Cairo%5Bslnt%2Cwght%5D.ttf
- `Roboto.ttf`: https://raw.githubusercontent.com/google/fonts/main/ofl/roboto/Roboto%5Bwdth%2Cwght%5D.ttf
- `DejaVuSans.ttf`: the previously packaged NewGazaArabic.ttf, retained as a Unicode symbol fallback.

Source hashes and generated asset hashes are in BUILD.json. Sources are checked in:
rebuilding does not fetch changing remote fonts. License copies ship in
testingReplic/Assets/NewGaza/Resources/Typography (OFL for Cairo/Tajawal/Roboto;
DejaVu license for the fallback). Generated families are renamed New Gaza UI.

Run `uv run python tools/build_ui_fonts.py` from the repository root.
The build copies actual primary Arabic outlines, derives missing joining cmap
entries through HarfBuzz/GSUB, incorporates Roboto Latin outlines and includes
remaining Unicode from DejaVu. Font tables are static TrueType, not variable/color.