using System;
using System.Collections.Generic;
using System.Text.Json;
using NewGaza;
using NewGaza.UI;

internal static class Program
{
    private static int assertions;
    private static void Check(bool ok, string message)
    { assertions++; if (!ok) throw new Exception(message); }
    private static void Main(string[] args)
    {
        Check(ArabicText.Validate().Length == 0, "joining and bidi regressions");
        foreach (CityTextRole role in Enum.GetValues(typeof(CityTextRole)))
        {
            var label = new UnityEngine.UI.Text();
            CityTypography.Apply(label, role);
            Check(label.font != null, "font assigned");
            Check(label.fontStyle == UnityEngine.FontStyle.Normal, "no synthetic bold");
            Check(label.fontSize == (role == CityTextRole.Small ? 16 : role == CityTextRole.Button ? 18 :
                role == CityTextRole.Heading ? 24 : role == CityTextRole.Display ? 30 : 20), "unified scale");
            Check(label.font.Path == (role == CityTextRole.Button ? "Typography/Button" :
                role == CityTextRole.Heading || role == CityTextRole.Display ? "Typography/Heading" : "NewGazaArabic"),
                "correct weight and face");
        }
        Check(CityTypography.RoleForSize(14) == CityTextRole.Small, "legacy small");
        Check(CityTypography.RoleForSize(21) == CityTextRole.Body, "legacy body");
        Check(CityTypography.RoleForSize(26) == CityTextRole.Heading, "legacy heading");
        Check(CityTypography.RoleForSize(36) == CityTextRole.Display, "legacy display");
        var samples = new Dictionary<string, string>();
        foreach (string logical in new[] {
            "غزة الجديدة", "إعادة الإعمار والبناء", "تأكيد البناء", "الرصيد 50,000 · الخرسانة ١٢٣",
            "الأحياء المفتوحة | Unity 6", "Unicode · 50,000 / 100%", "بَ بِ بُ لا لأ لإ لآ",
            "المتجر — أعمال وزراعة ومصانع", "★ New Gaza — Unity 6"
        }) samples[logical] = ArabicText.Shape(logical);
        if (args.Length > 0)
            System.IO.File.WriteAllText(args[0], JsonSerializer.Serialize(samples));
        Console.WriteLine("PASS typography rules and Arabic Unicode / " + assertions + " assertions");
        Console.WriteLine("Resource loading is a bounded test double; no Unity rendering was run.");
    }
}

namespace UnityEngine
{
    public enum FontStyle { Normal, Bold }
    public sealed class Font { public string Path; }
    public static class Resources
    {
        public static T Load<T>(string path) where T : class
        { return new Font { Path = path } as T; }
    }
}
namespace UnityEngine.UI
{
    public sealed class Text
    { public UnityEngine.Font font; public int fontSize; public UnityEngine.FontStyle fontStyle; public float lineSpacing; }
}