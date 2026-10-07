using NewGaza.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza.UI
{
    internal static class CityButtonContent
    {
        internal static void Apply(RectTransform root, ArabicLabel[] labels, RectTransform icon)
        {
            if (root == null || labels == null || labels.Length == 0) return;
            // Composite home cards retain their title/subtitle geometry; all button copy is centered.
            foreach (var label in labels)
            {
                if (label == null) continue;
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = label.fontSize;
                label.lineSpacing = 1.08f;
            }
            if (labels.Length != 1) return;
            var box = ButtonContentMetrics.Calculate(root.rect.width, root.rect.height, icon != null);
            var text = labels[0];
            text.resizeTextMinSize = box.minimumFont;
            text.resizeTextMaxSize = Mathf.Max(box.minimumFont, text.fontSize);
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(box.left, box.bottom);
            rect.offsetMax = new Vector2(-box.right, -box.top);
            if (icon == null) return;
            if (icon.gameObject.activeSelf != box.showIcon) icon.gameObject.SetActive(box.showIcon);
            if (!box.showIcon) return;
            icon.anchorMin = icon.anchorMax = box.stacked ? new Vector2(.5f, 1) : new Vector2(1, .5f);
            icon.pivot = box.stacked ? new Vector2(.5f, 1) : new Vector2(1, .5f);
            icon.anchoredPosition = box.stacked ? new Vector2(0, -box.iconInset) : new Vector2(-box.iconInset, 0);
            icon.sizeDelta = new Vector2(box.iconSize, box.iconSize);
        }
    }
}