using System;
using System.Collections.Generic;

namespace NewGaza
{
    /// <summary>
    /// Stable neighborhood-to-damaged-architecture mapping. This is presentation metadata only;
    /// it does not participate in progression or saved state.
    /// </summary>
    public static class CityRuinProfiles
    {
        internal sealed class Profile
        {
            internal readonly string districtId;
            internal readonly string modelKey;
            internal readonly float maxHeightCityUnits;

            internal Profile(string districtId, string modelKey, float maxHeightCityUnits)
            {
                this.districtId = districtId;
                this.modelKey = modelKey;
                this.maxHeightCityUnits = maxHeightCityUnits;
            }
        }

        // Absolute Unity world heights derived from each generated regional OBJ's measured Y
        // bound in meters / 20 meters per city unit. These metric caps are not parcel multipliers.
        private static readonly string[] modelKeys =
        {
            "ruin_shujaiya", "ruin_tuffah", "ruin_sheikh_radwan", "ruin_daraj",
            "ruin_karama", "ruin_old_city", "ruin_nasr", "ruin_sabra",
            "ruin_zeitoun", "ruin_rimal", "ruin_tel_al_hawa", "ruin_sheikh_ijlin",
            "ruin_rashid", "ruin_mosque", "ruin_school", "ruin_clinic", "ruin_civic",
            "ruin_wall", "ruin_car", "ruin_crater", "ruin_debris"
        };

        private static readonly Profile[] profiles =
        {
            new Profile("shujaiya", "ruin_shujaiya", .3628f),
            new Profile("tuffah", "ruin_tuffah", .5137f),
            new Profile("sheikh-radwan", "ruin_sheikh_radwan", .5261f),
            new Profile("daraj", "ruin_daraj", .6684f),
            new Profile("karama", "ruin_karama", .6663f),
            new Profile("old-city", "ruin_old_city", .5250f),
            new Profile("nasr", "ruin_nasr", .9770f),
            new Profile("sabra", "ruin_sabra", .6612f),
            new Profile("zeitoun", "ruin_zeitoun", .8112f),
            new Profile("rimal", "ruin_rimal", 1.1069f),
            new Profile("tel-al-hawa", "ruin_tel_al_hawa", 1.2649f),
            new Profile("sheikh-ijlin", "ruin_sheikh_ijlin", .9523f),
            new Profile("rashid", "ruin_rashid", 1.4088f)
        };

        private static readonly Dictionary<string, Profile> byDistrict =
            BuildDistrictLookup();

        internal static int Count { get { return profiles.Length; } }
        internal static IReadOnlyList<Profile> Profiles { get { return profiles; } }
        public static string[] ModelKeys { get { return (string[])modelKeys.Clone(); } }

        internal static Profile ForDistrict(string districtId)
        {
            if (string.IsNullOrEmpty(districtId) || !byDistrict.TryGetValue(districtId, out Profile profile))
                throw new ArgumentException("No destroyed-architecture profile is defined for district ID: " +
                    (districtId ?? "(null)") + ".", nameof(districtId));
            return profile;
        }

        /// <summary>
        /// Public-service plots use purpose-built destroyed civic assets; every other plot keeps
        /// its district's defining regional model as the dominant ruin.
        /// </summary>
        internal static string StageZeroModel(string districtId, string projectId)
        {
            Profile profile = ForDistrict(districtId);
            if (projectId != "services") return profile.modelKey;
            switch (districtId)
            {
                case "daraj":
                case "old-city": return "ruin_mosque";
                case "shujaiya":
                case "tuffah":
                case "sheikh-radwan":
                case "karama": return "ruin_school";
                case "nasr":
                case "sabra":
                case "zeitoun": return "ruin_clinic";
                case "rimal":
                case "tel-al-hawa":
                case "sheikh-ijlin":
                case "rashid": return "ruin_civic";
                default: throw new InvalidOperationException(
                    "District is missing a destroyed public-service model: " + districtId + ".");
            }
        }

        /// <summary>Small parcel-contained stage-zero damage detail, rotated deterministically.</summary>
        internal static string StageZeroDetail(string districtId, int plotIndex)
        {
            ForDistrict(districtId);
            string[] details = { "ruin_wall", "ruin_car", "ruin_crater", "ruin_debris" };
            int districtSlot = Array.FindIndex(profiles, profile => profile.districtId == districtId);
            return details[(districtSlot + plotIndex) % details.Length];
        }

        private static Dictionary<string, Profile> BuildDistrictLookup()
        {
            var result = new Dictionary<string, Profile>(profiles.Length, StringComparer.Ordinal);
            foreach (Profile profile in profiles)
                if (result.ContainsKey(profile.districtId))
                    throw new InvalidOperationException("Duplicate destroyed-architecture district ID: " +
                        profile.districtId + ".");
                else result.Add(profile.districtId, profile);
            return result;
        }
    }
}