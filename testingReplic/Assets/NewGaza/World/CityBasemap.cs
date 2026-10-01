using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NewGaza
{
    /// <summary>
    /// Offline Gaza City context exported from OpenStreetMap. Coordinates are city-local
    /// Unity units (+X east, +Z north); the geographic origin and scale are source metadata.
    /// © OpenStreetMap contributors, ODbL 1.0.
    /// </summary>
    [Serializable]
    public sealed class CityBasemap
    {
        public int schema;
        public int schemaVersion;
        public CityBasemapOrigin origin;
        public CityBasemapMetadata metadata;
        public double originLatitude;
        public double originLongitude;
        public float unitsPerKm;
        public float unitsPerKilometre;
        public string attribution;
        public string source;
        public string sourceUrl;
        public string license;
        public CityBasemapRoad[] roads;
        public CityBasemapBuilding[] buildings;
        public CityBasemapArea[] areas;
        /// <summary>Measured feature extent in source world units, not a political boundary.</summary>
        public CityBasemapExtent actualBounds;

        public const double ExpectedOriginLatitude = 31.515;
        public const double ExpectedOriginLongitude = 34.45;
        public const float ExpectedUnitsPerKilometre = 50f;
        public const string OpenStreetMapAttribution =
            "© OpenStreetMap contributors · ODbL 1.0";

        public int EffectiveSchema { get { return schemaVersion != 0 ? schemaVersion : schema; } }

        /// <summary>Loads the required Resources TextAsset; missing or invalid data is fatal.</summary>
        public static CityBasemap LoadFromResources(string resourcePath = "GazaBasemap")
        {
            if (string.IsNullOrEmpty(resourcePath))
                throw new ArgumentException("A basemap Resources path is required.", nameof(resourcePath));
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
                throw new InvalidOperationException("Missing required city basemap Resources/" +
                    resourcePath + ".json. Install the sourced GazaBasemap.json; no illustrative fallback is used.");
            return ParseAndValidate(asset.text, resourcePath);
        }

        public static CityBasemap ParseAndValidate(string json, string sourceName = "GazaBasemap.json")
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("City basemap " + sourceName + " is empty.");
            CityBasemap map;
            try { map = JsonUtility.FromJson<CityBasemap>(json); }
            catch (Exception error)
            {
                throw new InvalidOperationException("Could not parse city basemap " + sourceName + ": " +
                    error.Message, error);
            }
            if (map == null)
                throw new InvalidOperationException("City basemap " + sourceName + " contains no JSON object.");
            map.Validate(sourceName);
            return map;
        }

        public double EffectiveOriginLatitude
        {
            get
            {
                if (origin != null) return origin.latitude;
                if (metadata != null && metadata.projection != null) return metadata.projection.originLatitude;
                return originLatitude;
            }
        }

        public double EffectiveOriginLongitude
        {
            get
            {
                if (origin != null) return origin.longitude;
                if (metadata != null && metadata.projection != null) return metadata.projection.originLongitude;
                return originLongitude;
            }
        }

        public float EffectiveUnitsPerKilometre
        {
            get
            {
                if (unitsPerKm > 0f) return unitsPerKm;
                if (unitsPerKilometre > 0f) return unitsPerKilometre;
                if (metadata != null && metadata.projection != null)
                    return metadata.projection.unitsPerKilometre;
                return 0f;
            }
        }

        public string EffectiveAttribution
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(attribution)) return attribution;
                return metadata != null && !string.IsNullOrWhiteSpace(metadata.attribution)
                    ? metadata.attribution : OpenStreetMapAttribution;
            }
        }

        public string EffectiveSourceUrl
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(sourceUrl)) return sourceUrl;
                return metadata == null ? source : metadata.sourceUrl;
            }
        }

        private void Validate(string sourceName)
        {
            if (EffectiveSchema != 1)
                Fail(sourceName, "schemaVersion must be 1 (found " + EffectiveSchema + ").");
            double latitude = EffectiveOriginLatitude;
            double longitude = EffectiveOriginLongitude;
            float scale = EffectiveUnitsPerKilometre;
            if (!Finite(latitude) || !Finite(longitude) ||
                Math.Abs(latitude - ExpectedOriginLatitude) > .00001 ||
                Math.Abs(longitude - ExpectedOriginLongitude) > .00001)
                Fail(sourceName, "origin must be latitude 31.515, longitude 34.45.");
            if (!Finite(scale) || Math.Abs(scale - ExpectedUnitsPerKilometre) > .001f)
                Fail(sourceName, "unitsPerKm must be 50.");
            if (roads == null || roads.Length == 0)
                Fail(sourceName, "roads is missing or empty.");
            if (buildings == null || buildings.Length == 0)
                Fail(sourceName, "buildings is missing or empty.");
            if (areas == null || areas.Length == 0)
                Fail(sourceName, "areas is missing or empty.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < roads.Length; i++)
            {
                CityBasemapRoad road = roads[i];
                string path = "roads[" + i + "]";
                RequireId(sourceName, ids, road == null ? 0L : road.id,
                    road == null ? null : road.idText, path, false);
                if (string.IsNullOrWhiteSpace(road.kind))
                    Fail(sourceName, path + " requires a road kind.");
                if (!Finite(road.width) || road.width <= 0f || road.width > 100f)
                    Fail(sourceName, path + ".width must be finite and in (0, 100] world units.");
                ValidateLine(sourceName, road.points, path + ".points", 2);
            }
            ids.Clear();
            for (int i = 0; i < buildings.Length; i++)
            {
                CityBasemapBuilding building = buildings[i];
                string path = "buildings[" + i + "]";
                RequireId(sourceName, ids, building == null ? 0L : building.id,
                    building == null ? null : building.idText, path);
                if (building.center == null)
                    Fail(sourceName, path + ".center is missing.");
                ValidatePoint(sourceName, building.center, path + ".center");
                if (building.size == null)
                    Fail(sourceName, path + ".size is missing.");
                ValidatePoint(sourceName, building.size, path + ".size");
                if (building.size.x <= 0f || building.size.z <= 0f ||
                    building.size.x > 250f || building.size.z > 250f)
                    Fail(sourceName, path + ".size must be positive and at most 250 units per axis.");
                if (!Finite(building.yaw) || !Finite(building.height) ||
                    building.yaw < -360f || building.yaw > 360f ||
                    building.height <= 0f || building.height > 250f ||
                    building.levels < 0 || building.levels > 100)
                    Fail(sourceName, path + " has invalid yaw, height, or levels.");
                ValidatePolygon(sourceName, building.outline, path + ".outline");
            }
            ids.Clear();
            for (int i = 0; i < areas.Length; i++)
            {
                CityBasemapArea area = areas[i];
                string path = "areas[" + i + "]";
                RequireId(sourceName, ids, area == null ? 0L : area.id,
                    area == null ? null : area.idText, path);
                if (string.IsNullOrWhiteSpace(area.kind))
                    Fail(sourceName, path + ".kind is required.");
                ValidatePolygon(sourceName, area.points, path + ".points");
            }
            actualBounds = MeasureActualBounds();
        }

        private CityBasemapExtent MeasureActualBounds()
        {
            var bounds = new CityBasemapExtent
            {
                minX = float.MaxValue, minZ = float.MaxValue,
                maxX = float.MinValue, maxZ = float.MinValue
            };
            for (int i = 0; i < roads.Length; i++)
                for (int p = 0; p < roads[i].points.Length; p++) bounds.Include(roads[i].points[p]);
            for (int i = 0; i < buildings.Length; i++)
            {
                bounds.Include(buildings[i].center);
                for (int p = 0; p < buildings[i].outline.Length; p++) bounds.Include(buildings[i].outline[p]);
            }
            for (int i = 0; i < areas.Length; i++)
                for (int p = 0; p < areas[i].points.Length; p++) bounds.Include(areas[i].points[p]);
            if (bounds.minX == float.MaxValue || bounds.maxX == float.MinValue)
                Fail("GazaBasemap.json", "roads, building footprints, and areas produced no map extent.");
            return bounds;
        }

        private static void ValidateLine(string source, CityBasemapPoint[] points, string path, int minimum)
        {
            if (points == null || points.Length < minimum || points.Length > 100000)
                Fail(source, path + " must contain between " + minimum + " and 100000 points.");
            for (int i = 0; i < points.Length; i++) ValidatePoint(source, points[i], path + "[" + i + "]");
        }

        private static void ValidatePolygon(string source, CityBasemapPoint[] points, string path)
        {
            ValidateLine(source, points, path, 3);
            double area = 0;
            CityBasemapPoint origin = points[0];
            for (int i = 0; i < points.Length; i++)
            {
                CityBasemapPoint a = points[i], b = points[(i + 1) % points.Length];
                area += ((double)a.x - origin.x) * (b.z - origin.z) -
                    ((double)b.x - origin.x) * (a.z - origin.z);
            }
            if (Math.Abs(area) < .000000001)
                Fail(source, path + " must enclose a non-zero area.");
        }

        private static void ValidatePoint(string source, CityBasemapPoint point, string path)
        {
            if (point == null || !Finite(point.x) || !Finite(point.z) ||
                Math.Abs(point.x) > 10000f || Math.Abs(point.z) > 10000f)
                Fail(source, path + " must contain finite city-local x/z coordinates within 10000 units.");
        }

        private static void RequireId(string source, HashSet<string> ids, long id, string idText,
            string path, bool mustBeUnique = true)
        {
            string key = !string.IsNullOrWhiteSpace(idText)
                ? idText : id > 0 ? id.ToString(CultureInfo.InvariantCulture) : null;
            if (string.IsNullOrWhiteSpace(key) || (mustBeUnique && !ids.Add(key)))
                Fail(source, path + ".id must be nonempty" +
                    (mustBeUnique ? " and unique within its feature type." : "."));
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Fail(string source, string message)
        {
            throw new InvalidOperationException("Invalid city basemap " + source + ": " + message);
        }
    }

    [Serializable]
    public sealed class CityBasemapOrigin
    {
        public double latitude;
        public double longitude;
    }

    [Serializable]
    public sealed class CityBasemapPoint
    {
        public float x;
        public float z;
        public CityBasemapPoint() { }
        public CityBasemapPoint(float x, float z) { this.x = x; this.z = z; }
    }

    [Serializable]
    public sealed class CityBasemapRoad
    {
        public long id;
        public string idText;
        public string name;
        public string kind;
        public float width;
        public CityBasemapPoint[] points;
        public string FeatureId
        {
            get { return !string.IsNullOrWhiteSpace(idText) ? idText : id.ToString(CultureInfo.InvariantCulture); }
        }
    }

    [Serializable]
    public sealed class CityBasemapBuilding
    {
        public long id;
        public string idText;
        public CityBasemapPoint center;
        public CityBasemapPoint size;
        public float yaw;
        public float height;
        public int levels;
        public CityBasemapPoint[] outline;
        public string FeatureId
        {
            get { return !string.IsNullOrWhiteSpace(idText) ? idText : id.ToString(CultureInfo.InvariantCulture); }
        }
    }

    [Serializable]
    public sealed class CityBasemapArea
    {
        public long id;
        public string idText;
        public string kind;
        public CityBasemapPoint[] points;
        public string FeatureId
        {
            get { return !string.IsNullOrWhiteSpace(idText) ? idText : id.ToString(CultureInfo.InvariantCulture); }
        }
    }

    [Serializable]
    public sealed class CityBasemapMetadata
    {
        public int schemaVersion;
        public string attribution;
        public string license;
        public string source;
        public string sourceUrl;
        public CityBasemapProjection projection;
    }

    [Serializable]
    public sealed class CityBasemapExtent
    {
        public float minX, maxX, minZ, maxZ;
        public void Include(CityBasemapPoint point)
        {
            minX = Math.Min(minX, point.x);
            maxX = Math.Max(maxX, point.x);
            minZ = Math.Min(minZ, point.z);
            maxZ = Math.Max(maxZ, point.z);
        }
    }

    [Serializable]
    public sealed class CityBasemapProjection
    {
        public string type;
        public double originLatitude;
        public double originLongitude;
        public float unitsPerKilometre;
    }
}