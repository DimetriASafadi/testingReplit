using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza.UI
{
    /// <summary>Wrap logical words first, then shape EACH rendered line, never reverse a whole wrapped paragraph.</summary>
    public sealed class ArabicLabel : Text
    {
        private string source = "";
        private bool reflowing;
        private readonly TextGenerator measure = new TextGenerator();
        public string LogicalText => source;

        public void SetText(string value)
        {
            value = value ?? "";
            if (source == value && !string.IsNullOrEmpty(text)) return;
            source = value;
            Reflow();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Reflow();
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            Reflow();
        }

        private void Reflow()
        {
            if (reflowing || font == null) return;
            reflowing = true;
            try
            {
                float available = rectTransform.rect.width;
                if (horizontalOverflow == HorizontalWrapMode.Overflow || available < 4)
                {
                    text = ArabicText.Shape(source);
                    return;
                }
                var output = new List<string>();
                var settings = GetGenerationSettings(Vector2.zero);
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                settings.generateOutOfBounds = true;
                foreach (string paragraph in source.Replace("\r", "").Split('\n'))
                {
                    string line = "";
                    foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string candidate = line.Length == 0 ? word : line + " " + word;
                        float width = measure.GetPreferredWidth(ArabicText.Shape(candidate), settings) / pixelsPerUnit;
                        if (line.Length > 0 && width > available)
                        {
                            output.Add(ArabicText.Shape(line));
                            line = word;
                        }
                        else line = candidate;
                    }
                    output.Add(ArabicText.Shape(line));
                }
                text = string.Join("\n", output);
            }
            finally { reflowing = false; }
        }
    }
}