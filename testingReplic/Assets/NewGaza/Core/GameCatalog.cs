using System;

namespace NewGaza.Core
{
    public static class GameCatalog
    {
        public const long FactoryCost = 15000;
        public const long ExcavatorCost = 8000;
        public const long TruckCost = 6000;
        public const long BulldozerCost = 5000;

        // User-approved neighborhoods, unlocked east-to-west using mapped representative
        // positions. This is game progression, not real reconstruction priorities.
        public static readonly DistrictDefinition[] Districts = BuildDistricts();

        public static GameState CreateNew(long now)
        {
            if (now < 0) throw new ArgumentOutOfRangeException(nameof(now), "وقت الجهاز غير صالح");
            var state = new GameState { lastSeenUtc = now, districts = new DistrictState[Districts.Length] };
            for (int i = 0; i < Districts.Length; i++)
            {
                var definition = Districts[i];
                var district = new DistrictState
                {
                    unlocked = i == 0,
                    projects = new ProjectState[definition.projects.Length]
                };
                for (int p = 0; p < district.projects.Length; p++)
                    district.projects[p] = new ProjectState { id = definition.projects[p].id };
                state.districts[i] = district;
            }
            return state;
        }

        private static DistrictDefinition[] BuildDistricts()
        {
            var locations = GameGeography.Districts;
            string[] farms =
            {
                "مشتل الشجاعية", "بستان التفاح", "مزرعة الشيخ رضوان", "بستان الكرامة",
                "حديقة أعشاب البلدة القديمة", "مزرعة خضار الصبرة", "بستان الزيتون",
                "حديقة زهور الرمال", "مشتل تل الهوا", "مزرعة الشيخ عجلين", "مشتل الساحل"
            };
            string[] shops =
            {
                "سوق الشجاعية", "سوق التفاح", "سوق الشيخ رضوان", "متاجر الكرامة",
                "سوق البلدة القديمة", "مخبز الصبرة", "معصرة الزيتون", "مكتبة الرمال",
                "مقهى تل الهوا", "سوق الشيخ عجلين", "فندق الضيافة الساحلي"
            };
            var names = new string[locations.Length];
            for (int i = 0; i < locations.Length; i++) names[i] = locations[i].name;
            var result = new DistrictDefinition[names.Length];
            for (int i = 0; i < result.Length; i++)
            {
                bool finale = i == 10;
                result[i] = new DistrictDefinition
                {
                    name = names[i],
                    rarity = finale ? "ختامي" : i < 3 ? "عادي" : i < 6 ? "نادر" : i < 8 ? "ملحمي" : "أسطوري",
                    description = finale
                        ? "شارع الرشيد المطل على البحر، على امتداد ساحل المدينة؛ المشروع الختامي بعد الأحياء العشرة."
                        : "أعد بناء " + names[i] + " بالكامل، ثم استلم المكافأة لفتح الحي التالي.",
                    rubbleLoads = 4 + i,
                    completionReward = finale ? 1000000 : 100000 + i * 25000,
                    projects = new[]
                    {
                        Project("water", "شبكة مياه " + names[i], ProjectKind.Water, 18000, 3600, 60, 15,
                            null, "شبكة مياه نظيفة؛ مطلوبة للمنازل والطريق."),
                        Project("power", "شبكة كهرباء " + names[i], ProjectKind.Power, 22000, 5400, 40, 35,
                            null, "بنية كهربائية لتشغيل الخدمات والإنتاج."),
                        Project("housing", "منزل صغير في " + names[i], ProjectKind.Housing, 25000, 7200, 80, 25,
                            "water", "منزل صغير: ٢٥ ألف عملة، ساعتان حقيقيتان، ومواد من المخزون."),
                        Project("road", finale ? "طريق الكورنيش" : "طريق " + names[i], ProjectKind.Road, 150000, 64800, 300, 80,
                            "water", "طريق رئيسي: ١٥٠ ألف عملة و١٨ ساعة حقيقية."),
                        Project("park", finale ? "منتزه الكورنيش" : "حديقة " + names[i], ProjectKind.Park, 12000, 3600, 30, 5,
                            "road", "مساحة خضراء عامة مرتبطة بالطريق."),
                        Project("services", "مركز خدمات " + names[i], ProjectKind.Services, 30000, 10800, 100, 35,
                            "power", "مركز صحي وخدمات مجتمعية؛ يفتح التجارة."),
                        Project("farm", farms[i], ProjectKind.Investment, 500, 300, 0, 0,
                            null, "زراعة متكررة: ادفع ٥٠٠ للبذور، انتظر ٥ دقائق ثم احصد ١٥٠٠. إعادة الزراعة يدوية.",
                            1500, 0),
                        Project("commerce", shops[i], ProjectKind.Investment, finale ? 60000 : 10000, finale ? 14400 : 3600, 30, 10,
                            "services", "تجارة: دخل يتراكم كل ساعة بعد البناء؛ اجمعه دون إعادة البناء.",
                            finale ? 8000 : 3000, 3600),
                        Project("industry", "ورشة تدوير " + names[i], ProjectKind.Investment, 3000, 900, 10, 5,
                            "power", "دفعة صناعية متكررة: تستهلك ١٠ خرسانة و٥ حديد؛ تنتج ٨٠ خرسانة و٣٠ حديد و٢٠ خشب و١٠ أخرى، ودخل ٤٥٠٠.",
                            4500, 0)
                    }
                };
            }
            return result;
        }

        private static ProjectDefinition Project(string id, string name, ProjectKind kind, long cost,
            int duration, int concrete, int iron, string prerequisite, string description,
            long income = 0, int incomeSeconds = 0)
        {
            return new ProjectDefinition
            {
                id = id, name = name, kind = kind, cost = cost, durationSeconds = duration,
                concreteCost = concrete, ironCost = iron, prerequisite = prerequisite,
                description = description, income = income, incomeSeconds = incomeSeconds
            };
        }
    }
}