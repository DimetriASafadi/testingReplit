using System;

namespace NewGaza.Core
{
    public struct ButtonContentBox
    {
        public float left, right, top, bottom, iconSize, iconInset;
        public bool showIcon, stacked;
        public int minimumFont;
    }
    public static class ButtonContentMetrics
    {
        public static ButtonContentBox Calculate(float width, float height, bool hasIcon)
        {
            if (!Finite(width) || width <= 0) width = 100;
            if (!Finite(height) || height <= 0) height = 44;
            float horizontal = Math.Min(width * .16f, width < 110 ? 10 : 16);
            float vertical = Math.Min(height * .18f, height < 40 ? 5 : 9);
            var box = new ButtonContentBox { left = horizontal, right = horizontal,
                top = vertical, bottom = vertical, minimumFont = width < 132 || height < 40 ? 12 : 14 };
            box.stacked = hasIcon && width < 200 && height >= 64;
            box.showIcon = hasIcon && (box.stacked || width >= 240 && height >= 52);
            if (box.showIcon)
            {
                box.iconSize = box.stacked ? 24 : Math.Min(28, height - vertical * 2);
                box.iconInset = horizontal;
                if (box.stacked) box.top = vertical + box.iconSize + 6;
                // Equal reservations keep the TEXT centered in the whole button.
                else box.left = box.right = horizontal + box.iconSize + 8;
            }
            return box;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}