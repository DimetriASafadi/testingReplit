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
        private bool insetting;
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
            InsetContainerEdges();
            Reflow();
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            InsetContainerEdges();
            Reflow();
        }

        private void InsetContainerEdges()
        {
            if (insetting || rectTransform.parent == null) return;
            var parent = rectTransform.parent as RectTransform;
            if (parent == null || parent.rect.width < 100 || parent.rect.height < 40 ||
                parent.GetComponent<Image>() == null || parent.GetComponent<Button>() != null) return;
            var rect = rectTransform;
            var minimum = rect.offsetMin; var maximum = rect.offsetMax;
            if (rect.anchorMin.x == 0 && rect.anchorMax.x == 1)
            { minimum.x = Mathf.Max(minimum.x, 12); maximum.x = Mathf.Min(maximum.x, -12); }
            if (rect.anchorMin.y == 0 && rect.anchorMax.y == 1)
            { minimum.y = Mathf.Max(minimum.y, 8); maximum.y = Mathf.Min(maximum.y, -8); }
            if (minimum == rect.offsetMin && maximum == rect.offsetMax) return;
            insetting = true;
            try { rect.offsetMin = minimum; rect.offsetMax = maximum; }
            finally { insetting = false; }
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
                if (resizeTextForBestFit && rectTransform.rect.height > 0)
                {
                    // Fit BEFORE manual Arabic wrapping. Fitting already-wrapped lines would
                    // leave unnecessary line breaks and clipped short button captions.
                    var fitSettings = GetGenerationSettings(rectTransform.rect.size);
                    fitSettings.resizeTextForBestFit = false;
                    fitSettings.horizontalOverflow = HorizontalWrapMode.Wrap;
                    fitSettings.verticalOverflow = VerticalWrapMode.Overflow;
                    string shaped = ArabicText.Shape(source);
                    int chosen = resizeTextMinSize;
                    for (int size = resizeTextMaxSize; size >= resizeTextMinSize; size--)
                    {
                        fitSettings.fontSize = size;
                        float height = measure.GetPreferredHeight(shaped, fitSettings) / pixelsPerUnit;
                        if (height <= rectTransform.rect.height) { chosen = size; break; }
                    }
                    settings.resizeTextForBestFit = false;
                    settings.fontSize = chosen;
                }
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