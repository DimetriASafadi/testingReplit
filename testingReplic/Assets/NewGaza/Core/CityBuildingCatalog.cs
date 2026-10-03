using System;
using System.Collections.Generic;

namespace NewGaza.Core
{
    public enum BuildingCategory { Equipment, Recycling, Economic, Residential, Agricultural, Commercial, Recreation }

    public sealed class CityBuildingDefinition
    {
        public string id, name;
        public BuildingCategory category;
        public float widthMeters, depthMeters;
        public int floors, concrete, iron, duration;
        public long cost, hourlyIncome;
        public bool EquipmentDepot => category == BuildingCategory.Equipment ||
            category == BuildingCategory.Recycling || category == BuildingCategory.Economic;
    }

    public static class CityBuildingCatalog
    {
        public static readonly CityBuildingDefinition[] All = Create();
        public static readonly string[] CategoryNames =
            { "العمل والمعدات", "إعادة التدوير", "اقتصادية", "سكنية", "مزارع", "تجارية", "ترفيهية" };

        public static CityBuildingDefinition Find(string id)
        {
            foreach (var item in All) if (item.id == id) return item;
            return null;
        }

        private static CityBuildingDefinition[] Create()
        {
            var items = new List<CityBuildingDefinition>();
            Add(items, BuildingCategory.Equipment, "work", "مبنى عمل", 10, 12, 2, 2000, 0, 0);
            Add(items, BuildingCategory.Equipment, "equipment_store", "مخزن معدات", 18, 24, 1, 4000, 0, 0);
            Add(items, BuildingCategory.Recycling, "recycling", "مصنع إعادة التدوير", 24, 30, 2, 15000, 0, 0);
            string[] industryIds = { "glass", "cement", "steel", "asphalt", "food_factory", "water_treatment" };
            string[] industryNames = { "مصنع زجاج", "مصنع اسمنت", "مصنع الحديد والصلب", "مصنع اسفلت", "مصنع الاغذية", "محطة معالجة مياه" };
            for (int i = 0; i < industryIds.Length; i++)
                Add(items, BuildingCategory.Economic, industryIds[i], industryNames[i], 24 + i * 2, 28 + i * 2, 2, 9000 + i * 1000, 15, 5);
            string[] housingIds = { "small_house", "medium_house", "villa", "housing_4", "housing_6",
                "housing_complex", "modern_housing", "traditional_housing", "housing_tower", "tourist_villa" };
            string[] housingNames = { "منزل صغير", "منزل متوسط", "فيلا سكنية", "عمارة سكنية 4 طوابق",
                "عمارة سكنية 6 طوابق", "مجمع سكني", "عمارة حديثة", "عمارة تقليدية", "برج سكني", "فيلا سياحية" };
            int[] floors = { 1, 2, 2, 4, 6, 6, 8, 3, 14, 3 };
            int[] widths = { 8, 12, 16, 14, 16, 32, 20, 12, 24, 22 };
            for (int i = 0; i < housingIds.Length; i++)
                Add(items, BuildingCategory.Residential, housingIds[i], housingNames[i], widths[i], widths[i] + 2,
                    floors[i], 2500 + i * 2500, 5 + i * 5, 1 + i * 2);
            string[] farmIds = { "wheat", "citrus", "olive", "vegetables", "strawberry", "cattle", "palms", "corn", "protective_trees", "ornamental_trees" };
            string[] farmNames = { "أرض قمح", "أرض حمضيات", "أرض زيتون", "أرض خضروات", "أرض فراولة", "مزارع أبقار",
                "أراضي نخيل", "أرض ذرة", "أشجار حماية", "أشجار زينة" };
            for (int i = 0; i < farmIds.Length; i++)
                Add(items, BuildingCategory.Agricultural, farmIds[i], farmNames[i], i >= 8 ? 8 : 24, i >= 8 ? 12 : 30, 1, 500 + i * 200, 0, 0);
            string[] tradeIds = { "food_shop", "clothes_shop", "shoe_shop", "traditional_mall", "modern_mall", "municipality" };
            string[] tradeNames = { "محل تجاري أغذية", "محل تجاري ملابس", "محل تجاري أحذية", "مجمع تجاري تقليدي",
                "مجمع تجاري حديث", "مبنى بلدية المنطقة" };
            for (int i = 0; i < tradeIds.Length; i++)
                Add(items, BuildingCategory.Commercial, tradeIds[i], tradeNames[i], i < 3 ? 10 : 24, i < 3 ? 12 : 28, i < 3 ? 1 : 3, 3000 + i * 3000, 5 + i * 3, 1 + i);
            string[] leisureIds = { "zoo", "falafel", "shawarma", "western_food", "luxury_restaurant", "cafe", "snack_kiosk" };
            string[] leisureNames = { "حديقة حيوان", "مطعم فلافل", "مطعم شاورما", "مطعم وجبات غربية", "مطعم فاخر", "كافيه", "كشك مسليات" };
            for (int i = 0; i < leisureIds.Length; i++)
                Add(items, BuildingCategory.Recreation, leisureIds[i], leisureNames[i], i == 0 ? 36 : i == 6 ? 5 : 12,
                    i == 0 ? 40 : i == 6 ? 6 : 16, 1, i == 0 ? 20000 : 1000 + i * 1000, i == 6 ? 0 : 5, i == 6 ? 0 : 1);
            return items.ToArray();
        }

        private static void Add(List<CityBuildingDefinition> items, BuildingCategory category, string id,
            string name, float width, float depth, int floors, long cost, int concrete, int iron)
        {
            items.Add(new CityBuildingDefinition { id = id, name = name, category = category,
                widthMeters = width, depthMeters = depth, floors = floors, cost = cost,
                concrete = concrete, iron = iron, duration = 60 + floors * 30,
                hourlyIncome = category == BuildingCategory.Equipment || category == BuildingCategory.Recycling ? 0 : Math.Max(100, cost / 12) });
        }
    }
}