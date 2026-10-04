namespace NewGaza.Core
{
    /// <summary>Reject invalid/outside-window device samples before Unity projection or UI raycasts.</summary>
    public static class ScreenInputValidation
    {
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool InViewport(float x, float y, float left, float bottom, float width, float height)
        {
            return Finite(x) && Finite(y) && Finite(left) && Finite(bottom) &&
                Finite(width) && Finite(height) && width > 0 && height > 0 &&
                x >= left && y >= bottom && (double)x < (double)left + width &&
                (double)y < (double)bottom + height;
        }
    }
}