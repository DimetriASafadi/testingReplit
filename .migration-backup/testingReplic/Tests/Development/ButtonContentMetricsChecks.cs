using System;
using NewGaza.Core;

internal static class ButtonContentMetricsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (float width in new[] { 30f, 40f, 72f, 90f, 96f, 132f, 160f, 180f, 200f, 240f, 320f, 720f })
        foreach (float height in new[] { 24f, 30f, 40f, 44f, 64f, 72f, 100f, 120f })
        foreach (bool icon in new[] { false, true })
        {
            var box = ButtonContentMetrics.Calculate(width, height, icon);
            check(box.left == box.right && box.left > 0 && box.top > 0 && box.bottom > 0,
                "button captions centered with symmetric insets, never edge-to-edge");
            check(box.left + box.right < width && box.top + box.bottom < height,
                "usable text rectangle for compact, touch, navigation and wide card sizes");
            check(!box.showIcon || box.stacked || width >= 240 && height >= 52,
                "icons never consume the only caption line in short/compact controls");
            check(box.minimumFont >= 12 && box.minimumFont <= 14, "bounded readable fitting floor");
        }
        var shortButton = ButtonContentMetrics.Calculate(96, 40, true);
        var wide = ButtonContentMetrics.Calculate(320, 64, true);
        var nav = ButtonContentMetrics.Calculate(96, 72, true);
        check(!shortButton.showIcon && wide.showIcon && !wide.stacked && nav.stacked,
            "compact locator, inline primary action and stacked navigation each use appropriate layouts");
        check(ButtonContentMetrics.Calculate(float.NaN, float.PositiveInfinity, true).left > 0,
            "transient invalid layout dimensions have finite insets");
    }
}