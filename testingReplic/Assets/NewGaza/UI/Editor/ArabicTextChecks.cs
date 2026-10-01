#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace NewGaza.UI
{
    internal static class ArabicTextChecks
    {
        [MenuItem("New Gaza/Validate Arabic shaping")]
        public static void Validate()
        {
            string[] failures = ArabicText.Validate();
            if (failures.Length > 0)
                throw new InvalidOperationException("Arabic shaping checks failed: " + string.Join(", ", failures));
            Font font = Resources.Load<Font>("NewGazaArabic");
            if (font == null) throw new InvalidOperationException("Missing Resources/NewGazaArabic.ttf");
            string glyphs = ArabicText.Shape("غزة الجديدة الشجاعية الزيتون إعادة إعمار 50,000 Unity 6 · % / × + = ( )");
            font.RequestCharactersInTexture(glyphs, 28, FontStyle.Normal);
            foreach (char glyph in glyphs)
                if (!char.IsWhiteSpace(glyph) && !font.HasCharacter(glyph))
                    throw new InvalidOperationException("Arabic font missing glyph U+" + ((int)glyph).ToString("X4"));
            Debug.Log("New Gaza: 13 Arabic joining/bidi checks and font glyph checks passed.");
        }
    }
}
#endif