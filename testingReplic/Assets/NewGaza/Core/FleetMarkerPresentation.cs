using System;

namespace NewGaza.Core
{
    public static class FleetMarkerPresentation
    {
        public static float Opacity(float orthographicSize)
        {
            if (float.IsNaN(orthographicSize) || float.IsInfinity(orthographicSize) || orthographicSize <= 0) return 0;
            float t = Math.Max(0, Math.Min(1, (orthographicSize - 1.25f) / 3.75f));
            return .95f * t * t * (3 - 2 * t);
        }
    }
}