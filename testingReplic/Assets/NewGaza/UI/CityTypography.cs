using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza.UI
{
    public enum CityTextRole { Small, Body, Button, Heading, Display }

    /// <summary>One type scale and actual font weights for all native screens.
    /// Shipping faces contain Roboto Latin/numbers and Arabic Unicode joining.
    /// </summary>
    public static class CityTypography
    {
        public const int SmallSize = 16, BodySize = 20, ButtonSize = 18,
            HeadingSize = 24, DisplaySize = 30;
        private static Font body, button, heading;

        public static Font FontFor(CityTextRole role)
        {
            if (body == null) body = Required("NewGazaArabic");
            if (button == null) button = Required("Typography/Button");
            if (heading == null) heading = Required("Typography/Heading");
            return role == CityTextRole.Button ? button :
                role == CityTextRole.Heading || role == CityTextRole.Display ? heading : body;
        }

        private static Font Required(string path)
        {
            var font = Resources.Load<Font>(path);
            if (font == null) throw new InvalidOperationException("الخط المطلوب مفقود: Resources/" + path);
            return font;
        }

        public static CityTextRole RoleForSize(int requested)
        {
            return requested >= 30 ? CityTextRole.Display :
                requested >= 24 ? CityTextRole.Heading :
                requested <= 18 ? CityTextRole.Small : CityTextRole.Body;
        }

        public static void Apply(Text label, CityTextRole role)
        {
            label.font = FontFor(role);
            // Real Cairo Bold/Tajawal Medium are already in the font assets.
            // Synthetic Bold would thicken the outlines for a second time.
            label.fontStyle = FontStyle.Normal;
            label.fontSize = role == CityTextRole.Small ? SmallSize :
                role == CityTextRole.Button ? ButtonSize :
                role == CityTextRole.Heading ? HeadingSize :
                role == CityTextRole.Display ? DisplaySize : BodySize;
            label.lineSpacing = 1.04f;
        }
    }
}