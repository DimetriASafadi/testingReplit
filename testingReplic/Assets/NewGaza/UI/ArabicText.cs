using System;
using System.Collections.Generic;
using System.Text;

namespace NewGaza
{
    /// <summary>
    /// Visual Arabic for Unity's legacy UGUI Text (which does not perform Arabic
    /// joining or bidirectional layout). Input is always kept in logical order.
    /// No packages, platform APIs, or font fallbacks are required.
    /// </summary>
    public static class ArabicText
    {
        // isolated, final, initial, medial. A zero form means non-joining.
        private static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>
        {
            ['\u0621'] = new[] { '\uFE80', '\0', '\0', '\0' },
            ['\u0622'] = new[] { '\uFE81', '\uFE82', '\0', '\0' },
            ['\u0623'] = new[] { '\uFE83', '\uFE84', '\0', '\0' },
            ['\u0624'] = new[] { '\uFE85', '\uFE86', '\0', '\0' },
            ['\u0625'] = new[] { '\uFE87', '\uFE88', '\0', '\0' },
            ['\u0626'] = new[] { '\uFE89', '\uFE8A', '\uFE8B', '\uFE8C' },
            ['\u0627'] = new[] { '\uFE8D', '\uFE8E', '\0', '\0' },
            ['\u0628'] = new[] { '\uFE8F', '\uFE90', '\uFE91', '\uFE92' },
            ['\u0629'] = new[] { '\uFE93', '\uFE94', '\0', '\0' },
            ['\u062A'] = new[] { '\uFE95', '\uFE96', '\uFE97', '\uFE98' },
            ['\u062B'] = new[] { '\uFE99', '\uFE9A', '\uFE9B', '\uFE9C' },
            ['\u062C'] = new[] { '\uFE9D', '\uFE9E', '\uFE9F', '\uFEA0' },
            ['\u062D'] = new[] { '\uFEA1', '\uFEA2', '\uFEA3', '\uFEA4' },
            ['\u062E'] = new[] { '\uFEA5', '\uFEA6', '\uFEA7', '\uFEA8' },
            ['\u062F'] = new[] { '\uFEA9', '\uFEAA', '\0', '\0' },
            ['\u0630'] = new[] { '\uFEAB', '\uFEAC', '\0', '\0' },
            ['\u0631'] = new[] { '\uFEAD', '\uFEAE', '\0', '\0' },
            ['\u0632'] = new[] { '\uFEAF', '\uFEB0', '\0', '\0' },
            ['\u0633'] = new[] { '\uFEB1', '\uFEB2', '\uFEB3', '\uFEB4' },
            ['\u0634'] = new[] { '\uFEB5', '\uFEB6', '\uFEB7', '\uFEB8' },
            ['\u0635'] = new[] { '\uFEB9', '\uFEBA', '\uFEBB', '\uFEBC' },
            ['\u0636'] = new[] { '\uFEBD', '\uFEBE', '\uFEBF', '\uFEC0' },
            ['\u0637'] = new[] { '\uFEC1', '\uFEC2', '\uFEC3', '\uFEC4' },
            ['\u0638'] = new[] { '\uFEC5', '\uFEC6', '\uFEC7', '\uFEC8' },
            ['\u0639'] = new[] { '\uFEC9', '\uFECA', '\uFECB', '\uFECC' },
            ['\u063A'] = new[] { '\uFECD', '\uFECE', '\uFECF', '\uFED0' },
            ['\u0640'] = new[] { '\u0640', '\u0640', '\u0640', '\u0640' },
            ['\u0641'] = new[] { '\uFED1', '\uFED2', '\uFED3', '\uFED4' },
            ['\u0642'] = new[] { '\uFED5', '\uFED6', '\uFED7', '\uFED8' },
            ['\u0643'] = new[] { '\uFED9', '\uFEDA', '\uFEDB', '\uFEDC' },
            ['\u0644'] = new[] { '\uFEDD', '\uFEDE', '\uFEDF', '\uFEE0' },
            ['\u0645'] = new[] { '\uFEE1', '\uFEE2', '\uFEE3', '\uFEE4' },
            ['\u0646'] = new[] { '\uFEE5', '\uFEE6', '\uFEE7', '\uFEE8' },
            ['\u0647'] = new[] { '\uFEE9', '\uFEEA', '\uFEEB', '\uFEEC' },
            ['\u0648'] = new[] { '\uFEED', '\uFEEE', '\0', '\0' },
            ['\u0649'] = new[] { '\uFEEF', '\uFEF0', '\0', '\0' },
            ['\u064A'] = new[] { '\uFEF1', '\uFEF2', '\uFEF3', '\uFEF4' },
            ['\u0671'] = new[] { '\uFB50', '\uFB51', '\0', '\0' },
            ['\u067E'] = new[] { '\uFB56', '\uFB57', '\uFB58', '\uFB59' },
            ['\u0686'] = new[] { '\uFB7A', '\uFB7B', '\uFB7C', '\uFB7D' },
            ['\u0698'] = new[] { '\uFB8A', '\uFB8B', '\0', '\0' },
            ['\u06A9'] = new[] { '\uFB8E', '\uFB8F', '\uFB90', '\uFB91' },
            ['\u06AF'] = new[] { '\uFB92', '\uFB93', '\uFB94', '\uFB95' },
            ['\u06CC'] = new[] { '\uFBFC', '\uFBFD', '\uFBFE', '\uFBFF' }
        };

        private sealed class Glyph
        {
            public string text;
            public int direction; // -1 neutral, 0 RTL, 1 Latin, 2 numeral
            public int level;
            public int neutralContext;
        }

        public static string Shape(string logical)
        {
            if (string.IsNullOrEmpty(logical)) return string.Empty;
            var lines = logical.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = ShapeLine(lines[i]);
            return string.Join("\n", lines);
        }

        private static string ShapeLine(string line)
        {
            bool hasRtl = false;
            foreach (char character in line) if (Direction(character) == 0) { hasRtl = true; break; }
            if (!hasRtl)
            {
                // English-only lines stay LTR, including leading UI symbols.
                var clean = new StringBuilder();
                foreach (char character in line) if (!IsBidiControl(character)) clean.Append(character);
                return clean.ToString();
            }
            var glyphs = new List<Glyph>();
            for (int i = 0; i < line.Length; i++)
            {
                char original = line[i];
                if (char.IsHighSurrogate(original) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
                {
                    // Treat a supplementary Unicode scalar as one bidi unit.
                    glyphs.Add(new Glyph { text = line.Substring(i, 2), direction = -1 });
                    i++;
                    continue;
                }
                // Do not accept user-supplied directional overrides or leave joiners visible.
                if (IsBidiControl(original) || original == '\u200C' || original == '\u200D') continue;
                if (IsMark(original) && glyphs.Count > 0)
                {
                    glyphs[glyphs.Count - 1].text += original;
                    continue;
                }
                char shaped = original;
                if (Forms.TryGetValue(original, out char[] forms))
                {
                    int prev = Neighbor(line, i, -1);
                    int next = Neighbor(line, i, 1);
                    bool joinPrev = prev >= 0 && CanForward(line[prev]) && forms[1] != '\0';
                    bool joinNext = next >= 0 && forms[2] != '\0' && CanBackward(line[next]);
                    // Lam-alef ligatures, including hamza/madda variants.
                    if (original == '\u0644' && next >= 0 && IsAlef(line[next]))
                    {
                        int code = line[next] == '\u0622' ? 0xFEF5 :
                            line[next] == '\u0623' ? 0xFEF7 :
                            line[next] == '\u0625' ? 0xFEF9 : 0xFEFB;
                        var marks = new StringBuilder();
                        for (int m = i + 1; m < next; m++) if (IsMark(line[m])) marks.Append(line[m]);
                        glyphs.Add(new Glyph { text = ((char)(code + (joinPrev ? 1 : 0))).ToString() + marks, direction = 0 });
                        i = next;
                        continue;
                    }
                    shaped = joinPrev && joinNext ? forms[3] : joinPrev ? forms[1] : joinNext ? forms[2] : forms[0];
                }
                glyphs.Add(new Glyph { text = shaped.ToString(), direction = Direction(original) });
            }

            // Resolve numeric separators and runs before neutral punctuation.
            for (int i = 1; i + 1 < glyphs.Count; i++)
            {
                char c = glyphs[i].text[0];
                if ((c == ',' || c == '.' || c == ':' || c == '/' || c == '-' || c == '\u066B' || c == '\u066C') &&
                    glyphs[i - 1].direction == 2 && glyphs[i + 1].direction == 2)
                    glyphs[i].direction = 2;
            }
            // UAX#9 weak/neutral context: numbers remain visually LTR but inherit
            // the preceding strong letter for neutral resolution. In an Arabic
            // paragraph "123 · Unity" is TWO runs, not one reordered Latin phrase.
            int strongContext = 0;
            foreach (var glyph in glyphs)
            {
                if (glyph.direction == 0 || glyph.direction == 1) strongContext = glyph.direction;
                glyph.neutralContext = glyph.direction == 2 ? strongContext : glyph.direction;
            }
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (glyphs[i].direction != -1) continue;
                int left = i - 1;
                int right = i + 1;
                while (left >= 0 && glyphs[left].direction == -1) left--;
                while (right < glyphs.Count && glyphs[right].direction == -1) right++;
                int before = left >= 0 ? glyphs[left].neutralContext : 0;
                int after = right < glyphs.Count ? glyphs[right].neutralContext : 0;
                // Numbers are LTR for visual order; whitespace between Latin words is retained.
                bool ltr = before > 0 && after > 0;
                if ((glyphs[i].text == "%" || glyphs[i].text == "\u066A") &&
                    left >= 0 && glyphs[left].direction == 2) ltr = true;
                glyphs[i].level = ltr ? 2 : 1;
            }
            foreach (var glyph in glyphs)
                if (glyph.direction != -1) glyph.level = glyph.direction == 0 ? 1 : 2;
            // Unicode bidi L2: reverse higher-level runs, then the RTL paragraph.
            for (int level = 2; level >= 1; level--)
            {
                int start = 0;
                while (start < glyphs.Count)
                {
                    if (glyphs[start].level < level) { start++; continue; }
                    int end = start + 1;
                    while (end < glyphs.Count && glyphs[end].level >= level) end++;
                    glyphs.Reverse(start, end - start);
                    start = end;
                }
            }
            var output = new StringBuilder();
            foreach (var glyph in glyphs)
            {
                string value = glyph.text;
                if ((glyph.level & 1) != 0)
                {
                    char c = value[0];
                    char mirrored = c == '(' ? ')' : c == ')' ? '(' : c == '[' ? ']' :
                        c == ']' ? '[' : c == '{' ? '}' : c == '}' ? '{' : c;
                    if (mirrored != c) value = mirrored + value.Substring(1);
                }
                output.Append(value);
            }
            return output.ToString();
        }

        private static int Neighbor(string s, int index, int direction)
        {
            for (int i = index + direction; i >= 0 && i < s.Length; i += direction)
            {
                if (IsMark(s[i]) || s[i] == '\u200D') continue;
                if (s[i] == '\u200C') return -1;
                return i;
            }
            return -1;
        }

        private static bool CanForward(char c) { return Forms.TryGetValue(c, out char[] f) && f[2] != '\0'; }
        private static bool CanBackward(char c) { return Forms.TryGetValue(c, out char[] f) && f[1] != '\0'; }
        private static bool IsAlef(char c) { return c == '\u0622' || c == '\u0623' || c == '\u0625' || c == '\u0627'; }
        private static bool IsMark(char c)
        {
            var category = char.GetUnicodeCategory(c);
            return category == System.Globalization.UnicodeCategory.NonSpacingMark ||
                category == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }
        private static bool IsBidiControl(char c)
        {
            return c == '\u061C' || c == '\u200E' || c == '\u200F' ||
                (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069');
        }
        private static int Direction(char c)
        {
            if (char.IsDigit(c)) return 2;
            if ((c >= '\u0600' && c <= '\u06FF' && char.IsLetter(c)) ||
                (c >= '\u0750' && c <= '\u08FF') || (c >= '\u0590' && c <= '\u05FF')) return 0;
            if (char.IsLetter(c)) return 1;
            return -1;
        }

        /// <summary>Deterministic checks; callable without a Unity player.</summary>
        public static string[] Validate()
        {
            var errors = new List<string>();
            Check(errors, Shape("بب") == "\uFE90\uFE91", "dual joining");
            Check(errors, Shape("باب") == "\uFE8F\uFE8E\uFE91", "alef stops forward joining");
            Check(errors, Shape("لا") == "\uFEFB", "lam alef");
            Check(errors, Shape("بلا") == "\uFEFC\uFE91", "joined lam alef");
            Check(errors, Shape("نقود 50,000").Contains("50,000"), "Latin numeric grouping");
            Check(errors, Shape("الوقت 02:35").Contains("02:35"), "timer order");
            Check(errors, Shape("نسخة Unity 6").Contains("Unity 6"), "Latin run");
            Check(errors, Shape("نسبة ١٢٣").Contains("١٢٣"), "Arabic Indic numbers");
            Check(errors, Shape("بَ").Contains("\uFE8F\u064E"), "combining mark stays on base");
            Check(errors, Shape("أ\nب") == "\uFE83\n\uFE8F", "paragraph order");
            Check(errors, Shape("ب\u200Cب") == "\uFE8F\uFE8F", "non-joiner");
            Check(errors, !Shape("اسم\u202E").Contains("\u202E"), "untrusted bidi overrides");
            Check(errors, Shape("أ 123 · Unity 6") == "Unity 6 · 123 \uFE83", "separate numeric and Latin runs");
            Check(errors, Shape("★ New Gaza — Unity 6") == "★ New Gaza — Unity 6", "English-only LTR");
            Check(errors, Shape("غزة \U0001F3E2").Contains("\U0001F3E2"), "Unicode surrogate remains intact");
            return errors.ToArray();
        }
        private static void Check(List<string> errors, bool valid, string label) { if (!valid) errors.Add(label); }
    }

}