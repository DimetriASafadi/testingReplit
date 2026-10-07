using System;
using System.Globalization;

namespace NewGaza.Core
{
    public static class RoadEconomy
    {
        public static int GetLevel(GameState state, string id)
        {
            if (state == null || state.roadSegments == null || string.IsNullOrEmpty(id)) return 0;
            foreach (var segment in state.roadSegments)
                if (segment != null && string.Equals(segment.id, id, StringComparison.Ordinal))
                    return segment.level;
            return 0;
        }

        public static RoadImprovementCost GetCost(RoadSegmentDefinition definition, int targetLevel)
        {
            ValidateDefinition(definition);
            if (targetLevel != 1 && targetLevel != 2)
                throw new ArgumentOutOfRangeException(nameof(targetLevel), "Road target level must be 1 or 2.");

            double kilometres = (double)definition.lengthMeters / 1000d;
            if (targetLevel == 1)
                return new RoadImprovementCost { coins = Math.Max(100L, CeilingToLong(3000d * kilometres)) };
            return new RoadImprovementCost
            {
                coins = Math.Max(250L, CeilingToLong(8000d * kilometres)),
                concrete = 0,
                iron = 0
            };
        }

        public static float SpeedMultiplier(int level)
        {
            switch (level)
            {
                case 0: return 0.5f;
                case 1: return 1f;
                case 2: return 2f;
                default: throw new ArgumentOutOfRangeException(nameof(level), "Road level must be between 0 and 2.");
            }
        }

        internal static bool IsCanonicalId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int separator = id.IndexOf(':');
            if (separator <= 0 || separator != id.LastIndexOf(':') || separator == id.Length - 1) return false;
            string feature = id.Substring(0, separator);
            string ordinal = id.Substring(separator + 1);
            long featureId;
            int kilometerOrdinal;
            return IsCanonicalPositiveInteger(feature, out featureId)
                && IsCanonicalNonnegativeInteger(ordinal, out kilometerOrdinal);
        }

        internal static void ValidateDefinition(RoadSegmentDefinition definition)
        {
            if (definition == null || !IsCanonicalId(definition.id)
                || float.IsNaN(definition.lengthMeters) || float.IsInfinity(definition.lengthMeters)
                || definition.lengthMeters <= 0f || definition.lengthMeters > 1000.01f)
                throw new ArgumentException("Road definition is invalid.", nameof(definition));
        }

        private static bool IsCanonicalPositiveInteger(string text, out long value)
        {
            value = 0;
            return IsAsciiDigits(text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
        }

        private static bool IsCanonicalNonnegativeInteger(string text, out int value)
        {
            value = 0;
            return IsAsciiDigits(text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && value >= 0 && value.ToString(CultureInfo.InvariantCulture) == text;
        }

        private static bool IsAsciiDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char character in text)
                if (character < '0' || character > '9') return false;
            return true;
        }

        private static long CeilingToLong(double value)
        {
            double rounded = Math.Ceiling(value);
            if (double.IsNaN(rounded) || double.IsInfinity(rounded) || rounded > long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "Road cost is outside the supported range.");
            return (long)rounded;
        }

        private static int CeilingToInt(double value)
        {
            double rounded = Math.Ceiling(value);
            if (double.IsNaN(rounded) || double.IsInfinity(rounded) || rounded > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "Road material cost is outside the supported range.");
            return (int)rounded;
        }
    }
}