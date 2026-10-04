using System;
using NewGaza.Core;

internal static class InterfaceViewportChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var size in new[] {
            new[] { 1601f, 733f }, new[] { 1920f, 1080f }, new[] { 800f, 600f },
            new[] { 320f, 240f }, new[] { 390f, 844f }, new[] { 1080f, 1920f },
            new[] { 2560f, 1080f }, new[] { 720f, 1280f }, new[] { 1000f, 1000f } })
        {
            foreach (bool insets in new[] { false, true })
            {
                float x = insets ? 12 : 0, y = insets ? 24 : 0;
                var view = InterfaceViewport.Calculate(size[0], size[1], x, y,
                    size[0] - x * 2, size[1] - y * 2);
                bool portrait = view.height > view.width;
                float w = view.LogicalWidth, h = view.LogicalHeight;
                check(ScreenInputValidation.Finite(view.scale) && view.scale > 0 &&
                    w >= (portrait ? 720 : 1280) - .01f && h >= (portrait ? 1280 : 720) - .01f,
                    "Both canvases retain a usable shared logical viewport");
                Action<float, float, float, float, string> box = (left, top, width, height, name) =>
                {
                    check(left >= -.01f && top >= -.01f && width > 0 && height > 0 &&
                        left + width <= w + .02f && top + height <= h + .02f, name + " within logical safe area");
                    check(view.x + left * view.scale >= x - .02f &&
                        view.y + (h - top - height) * view.scale >= y - .02f &&
                        view.x + (left + width) * view.scale <= size[0] - x + .05f &&
                        view.y + (h - top) * view.scale <= size[1] - y + .05f,
                        name + " within actual screen/notch bounds");
                };
                float head = portrait ? 170 : 132;
                box(12, 12, w - 24, head, "HUD header");
                box(12, head + 24, portrait ? w - 24 : 355, portrait ? 102 : 116, "job panel");
                box(12, h - 104, w - 24, 92, "bottom navigation");
                box(w - 352, portrait ? 308 : 156, 340, 44, "world tool buttons");
                box(w - 432, (portrait ? 308 : 156) + 52, 420, 142, "optional district panel");
                box(12, h - 288, portrait ? w - 24 : Math.Min(420, w * .38f), 180, "dismissible guide");
                float actionWidth = Math.Min(w * (portrait ? 1 : .47f) - (portrait ? 24 : 0), 620);
                box(portrait ? (w - actionWidth) / 2 : 12, h - 328, actionWidth, 212, "work panel and reachable close");
                float pageWidth = Math.Min(w - 24, 850), pageHeight = Math.Min(h - 24, 900);
                box((w - pageWidth) / 2, (h - pageHeight) / 2, pageWidth, pageHeight, "page/store");
                box((w - 620) / 2, (h - 500) / 2, 620, 500, "confirmation and footer actions");
                box((w - 620) / 2, 4, 620, 104, "notification does not cover confirmation buttons");
                float margin = portrait ? 10 : 14, header = portrait ? 166 : 92, actions = portrait ? 140 : 126;
                float contentTop = portrait ? margin + header + 66 + 18 : margin + header + 14;
                float districtTop = portrait ? contentTop + 224 : contentTop;
                float districtHeight = Math.Max(portrait ? 260 : 270, h - actions - margin - districtTop - 14);
                float districtWidth = portrait ? w - margin * 2 : Math.Min(334, w * .29f);
                box(portrait ? margin : w - margin - districtWidth, districtTop, districtWidth, districtHeight, "home district card");
            }
        }
        foreach (var safe in new[] { new[] { 0f, 0f, 0f, 0f }, new[] { float.NaN, 0f, 100f, 100f },
            new[] { -20f, -30f, 900f, 700f }, new[] { 0f, 0f, float.Epsilon, float.Epsilon } })
        {
            var view = InterfaceViewport.Calculate(800, 600, safe[0], safe[1], safe[2], safe[3]);
            check(view.scale > 0 && view.x >= 0 && view.y >= 0 && view.x + view.width <= 800 &&
                view.y + view.height <= 600, "invalid/transient safe area normalized without division by zero");
        }
    }
}