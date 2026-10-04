using System;

namespace NewGaza.Core
{
    /// <summary>One logical coordinate system for all native UI canvases.</summary>
    public sealed class InterfaceViewport
    {
        public float x, y, width, height, scale;
        public float LogicalWidth => width / scale;
        public float LogicalHeight => height / scale;

        public static InterfaceViewport Calculate(float screenWidth, float screenHeight,
            float safeX, float safeY, float safeWidth, float safeHeight)
        {
            float w = Math.Max(1, ScreenInputValidation.Finite(screenWidth) ? screenWidth : 1);
            float h = Math.Max(1, ScreenInputValidation.Finite(screenHeight) ? screenHeight : 1);
            var result = new InterfaceViewport { width = w, height = h };
            if (ScreenInputValidation.Finite(safeX) && ScreenInputValidation.Finite(safeY) &&
                ScreenInputValidation.Finite(safeWidth) && ScreenInputValidation.Finite(safeHeight) &&
                safeWidth > 0 && safeHeight > 0)
            {
                float left = Math.Max(0, safeX), bottom = Math.Max(0, safeY);
                float right = Math.Min(w, safeX + safeWidth), top = Math.Min(h, safeY + safeHeight);
                if (right - left >= 1 && top - bottom >= 1)
                { result.x = left; result.y = bottom; result.width = right - left; result.height = top - bottom; }
            }
            bool portrait = result.height > result.width;
            result.scale = Math.Min(result.width / (portrait ? 720 : 1280),
                result.height / (portrait ? 1280 : 720));
            return result;
        }
    }
}