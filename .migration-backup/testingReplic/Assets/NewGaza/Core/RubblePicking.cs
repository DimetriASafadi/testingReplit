using System;

namespace NewGaza.Core
{
    // Oriented ray/parcel test: dense static city meshes need no per-building colliders.
    public static class RubblePicking
    {
        public static bool Hit(RubbleSiteState site, float ox, float oy, float oz,
            float dx, float dy, float dz, out float distance)
        {
            distance = 0;
            double radians = site.yaw * Math.PI / 180, c = Math.Cos(radians), s = Math.Sin(radians);
            double x = ox - site.x, z = oz - site.z, near = 0, far = 1600;
            if (!Slab(x * c - z * s, dx * c - dz * s, -site.width / 2, site.width / 2, ref near, ref far) ||
                !Slab(oy, dy, -.10, (site.cleared ? .04 : Math.Max(.04, site.height)) - .09, ref near, ref far) ||
                !Slab(x * s + z * c, dx * s + dz * c, -site.depth / 2, site.depth / 2, ref near, ref far)) return false;
            distance = (float)near;
            return true;
        }

        private static bool Slab(double origin, double direction, double min, double max, ref double near, ref double far)
        {
            if (Math.Abs(direction) < 1e-8) return origin >= min && origin <= max;
            double a = (min - origin) / direction, b = (max - origin) / direction;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            return near <= far;
        }
    }
}