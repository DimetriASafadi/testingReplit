using System;
using System.Collections.Generic;

namespace NewGaza
{
    /// <summary>Stable ID-based housing architecture and logical-height presentation metadata.</summary>
    public static class CityHousingProfiles
    {
        public sealed class Profile
        {
            public readonly string districtId;
            public readonly string masterKey;
            public readonly int floorCount;
            public readonly float foundationHeightMeters;
            public readonly float frameHeightMeters;
            public readonly float finishingHeightMeters;
            public readonly float logicalHeightMeters;

            internal Profile(string districtId, string masterKey, int floorCount,
                float foundationHeightMeters, float frameHeightMeters,
                float finishingHeightMeters, float logicalHeightMeters)
            {
                this.districtId = districtId;
                this.masterKey = masterKey;
                this.floorCount = floorCount;
                this.foundationHeightMeters = foundationHeightMeters;
                this.frameHeightMeters = frameHeightMeters;
                this.finishingHeightMeters = finishingHeightMeters;
                this.logicalHeightMeters = logicalHeightMeters;
            }

            public float MaxCityHeight { get { return logicalHeightMeters / 20f; } }

            public float HeightCapFor(CityConstructionPhase phase)
            {
                switch (phase)
                {
                    case CityConstructionPhase.Foundation: return foundationHeightMeters / 20f;
                    case CityConstructionPhase.Frame: return frameHeightMeters / 20f;
                    case CityConstructionPhase.Finishing: return finishingHeightMeters / 20f;
                    case CityConstructionPhase.Complete: return MaxCityHeight;
                    default:
                        throw new ArgumentException("Inactive is not a housing model phase.", nameof(phase));
                }
            }
        }

        private static readonly Profile[] profiles =
        {
            new Profile("shujaiya", "house_small_redtile", 1, 1.875f, 3.180000066757202f, 4.485000133514404f, 4.485000133514404f),
            new Profile("tuffah", "house_cream_family", 3, 1.875f, 8.579999923706055f, 9.369999885559082f, 10.350000381469727f),
            new Profile("sheikh-radwan", "house_compound", 3, 1.875f, 8.279999732971191f, 9.069999694824219f, 10.050000190734863f),
            new Profile("daraj", "apartment_4floor_balcony", 4, 1.875f, 11.210000038146973f, 12f, 12.979999542236328f),
            new Profile("karama", "apartment_6floor_balcony", 6, 1.875f, 16.40999984741211f, 17.200000762939453f, 18.18000030517578f),
            new Profile("old-city", "house_traditional_stonearches", 3, 1.875f, 8.579999923706055f, 9.369999885559082f, 10.350000381469727f),
            new Profile("nasr", "house_modern_villa", 3, 1.875f, 8.880000114440918f, 9.670000076293945f, 7.800000190734863f),
            new Profile("sabra", "apartment_blueglass_midrise", 6, 1.875f, 16.829999923706055f, 17.6200008392334f, 19.020000457763672f),
            new Profile("zeitoun", "house_small_redtile", 1, 1.875f, 3.180000066757202f, 4.485000133514404f, 4.485000133514404f),
            new Profile("rimal", "apartment_12floor_tower", 12, 1.875f, 31.770000457763672f, 32.560001373291016f, 33.540000915527344f),
            new Profile("tel-al-hawa", "apartment_blueglass_midrise", 6, 1.875f, 16.829999923706055f, 17.6200008392334f, 19.020000457763672f),
            new Profile("sheikh-ijlin", "house_coastal_white_pool", 3, 1.875f, 8.670000076293945f, 9.460000038146973f, 7.659999847412109f)
        };

        private static readonly string[] masters =
        {
            "house_small_redtile",
            "house_cream_family",
            "house_modern_villa",
            "apartment_4floor_balcony",
            "apartment_6floor_balcony",
            "house_compound",
            "apartment_blueglass_midrise",
            "house_traditional_stonearches",
            "apartment_12floor_tower",
            "house_coastal_white_pool"
        };

        private static readonly string[] phases =
        {
            "_foundation", "_frame", "_finishing", "_final"
        };

        private static readonly Dictionary<string, Profile> byDistrict = BuildLookup();

        public static string[] ModelKeys
        {
            get
            {
                var keys = new string[masters.Length * phases.Length];
                int index = 0;
                foreach (string master in masters)
                    foreach (string phase in phases)
                        keys[index++] = master + phase;
                return keys;
            }
        }

        public static string[] MasterKeys { get { return (string[])masters.Clone(); } }

        public static Profile ForDistrict(string districtId)
        {
            if (string.IsNullOrEmpty(districtId) ||
                !byDistrict.TryGetValue(districtId, out Profile profile))
                throw new ArgumentException("No housing architecture profile is defined for district ID: " +
                    (districtId ?? "(null)") + ".", nameof(districtId));
            return profile;
        }

        public static string ModelKey(string districtId, CityConstructionPhase phase)
        {
            Profile profile = ForDistrict(districtId);
            string suffix;
            switch (phase)
            {
                case CityConstructionPhase.Foundation: suffix = "_foundation"; break;
                case CityConstructionPhase.Frame: suffix = "_frame"; break;
                case CityConstructionPhase.Finishing: suffix = "_finishing"; break;
                case CityConstructionPhase.Complete: suffix = "_final"; break;
                default:
                    throw new ArgumentException("Inactive is not a housing model phase.", nameof(phase));
            }
            return profile.masterKey + suffix;
        }

        private static Dictionary<string, Profile> BuildLookup()
        {
            var result = new Dictionary<string, Profile>(profiles.Length, StringComparer.Ordinal);
            foreach (Profile profile in profiles)
            {
                if (result.ContainsKey(profile.districtId))
                    throw new InvalidOperationException("Duplicate housing district ID: " + profile.districtId);
                result.Add(profile.districtId, profile);
            }
            return result;
        }
    }
}