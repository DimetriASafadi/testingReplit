using System;

namespace NewGaza.Core
{
    public struct GeoPoint
    {
        public float x, z;
        public GeoPoint(float x, float z) { this.x = x; this.z = z; }
    }

    public struct GeoCoordinate
    {
        public double latitude, longitude;
        public GeoCoordinate(double latitude, double longitude)
        { this.latitude = latitude; this.longitude = longitude; }
    }

    public sealed class DistrictLocation
    {
        public readonly string id, name, sourceUrl;
        public readonly double latitude, longitude;
        public DistrictLocation(string id, string name, double latitude, double longitude, string sourceUrl)
        {
            this.id = id; this.name = name; this.latitude = latitude;
            this.longitude = longitude; this.sourceUrl = sourceUrl;
        }
    }

    /// <summary>
    /// Offline representative locations, not district boundaries. +X east, +Z north.
    /// OSM-derived data © OpenStreetMap contributors, ODbL 1.0; see GEOGRAPHY.md.
    /// Source snapshot checked 2026-10-01. No live map or device location requests.
    /// </summary>
    public static class GameGeography
    {
        public const double OriginLatitude = 31.515;
        public const double OriginLongitude = 34.45;
        public const float UnitsPerKilometre = 50;
        public const string Attribution = "© OpenStreetMap contributors · ODbL 1.0";

        // Game progression is east-to-west by representative longitude, not a driving route.
        // Legacy saves retain milestone indices and project IDs, not provisional names.
        public static readonly DistrictLocation[] Districts =
        {
            new DistrictLocation("shujaiya", "الشجاعية", 31.4979729, 34.4726451, "https://www.openstreetmap.org/node/13354378801"),
            new DistrictLocation("tuffah", "التفاح", 31.5158861, 34.4693028, "https://en.wikipedia.org/wiki/Tuffah"),
            new DistrictLocation("sheikh-radwan", "الشيخ رضوان", 31.5321920, 34.4667695, "https://www.openstreetmap.org/relation/3935884"),
            new DistrictLocation("karama", "الكرامة", 31.5487639, 34.4648283, "https://www.openstreetmap.org/way/1557437193"),
            new DistrictLocation("old-city", "البلدة القديمة", 31.5050311, 34.4641381, "https://www.openstreetmap.org/node/11300155754"),
            new DistrictLocation("sabra", "الصبرة", 31.5071510, 34.4519234, "https://www.openstreetmap.org/node/11300200739"),
            new DistrictLocation("zeitoun", "الزيتون", 31.4879035, 34.4445419, "https://www.openstreetmap.org/node/11298850625"),
            new DistrictLocation("rimal", "الرمال", 31.5200000, 34.4431000, "https://en.wikipedia.org/wiki/Rimal"),
            new DistrictLocation("tel-al-hawa", "تل الهوا", 31.5046790, 34.4349021, "https://www.openstreetmap.org/way/1558937383"),
            new DistrictLocation("sheikh-ijlin", "الشيخ عجلين", 31.5044915, 34.4237360, "https://www.openstreetmap.org/node/11300345272"),
            // A representative point on the route, not the location of all its projects.
            new DistrictLocation("rashid", "شارع الرشيد", 31.5250000, 34.4351403, "https://www.openstreetmap.org/way/1008649842")
        };

        // Generalized WGS84 polylines sampled from OSM way segments every ~457m latitude.
        // Coast uses the mainland-side envelope; small piers are intentionally omitted.
        // Rashid uses the seaward carriageway; side streets with the same name are excluded.
        public static readonly GeoCoordinate[] CoastCoordinates =
        {
            new GeoCoordinate(31.4880000, 34.3985337),
            new GeoCoordinate(31.4921111, 34.4026467),
            new GeoCoordinate(31.4962222, 34.4067769),
            new GeoCoordinate(31.5003333, 34.4105112),
            new GeoCoordinate(31.5044444, 34.4142577),
            new GeoCoordinate(31.5085556, 34.4178181),
            new GeoCoordinate(31.5126667, 34.4213124),
            new GeoCoordinate(31.5167778, 34.4247145),
            new GeoCoordinate(31.5208889, 34.4277012),
            new GeoCoordinate(31.5250000, 34.4336774),
            new GeoCoordinate(31.5291111, 34.4372386),
            new GeoCoordinate(31.5332222, 34.4410903),
            new GeoCoordinate(31.5373333, 34.4449421),
            new GeoCoordinate(31.5414444, 34.4487942),
            new GeoCoordinate(31.5455556, 34.4527600),
            new GeoCoordinate(31.5496667, 34.4567603),
            new GeoCoordinate(31.5537778, 34.4598542),
            new GeoCoordinate(31.5578889, 34.4629716),
            new GeoCoordinate(31.5620000, 34.4665950)
        };

        public static readonly GeoCoordinate[] RashidCoordinates =
        {
            new GeoCoordinate(31.4880000, 34.4010294),
            new GeoCoordinate(31.4921111, 34.4048925),
            new GeoCoordinate(31.4962222, 34.4083361),
            new GeoCoordinate(31.5003333, 34.4121969),
            new GeoCoordinate(31.5044444, 34.4161607),
            new GeoCoordinate(31.5085556, 34.4201229),
            new GeoCoordinate(31.5126667, 34.4239717),
            new GeoCoordinate(31.5167778, 34.4279593),
            new GeoCoordinate(31.5208889, 34.4314444),
            new GeoCoordinate(31.5250000, 34.4351403),
            new GeoCoordinate(31.5291111, 34.4390172),
            new GeoCoordinate(31.5332222, 34.4419339),
            new GeoCoordinate(31.5373333, 34.4454065),
            new GeoCoordinate(31.5414444, 34.4495233),
            new GeoCoordinate(31.5455556, 34.4543359),
            new GeoCoordinate(31.5496667, 34.4579207),
            new GeoCoordinate(31.5537778, 34.4628355),
            new GeoCoordinate(31.5578889, 34.4640241),
            new GeoCoordinate(31.5620000, 34.4691981)
        };

        public static readonly GeoPoint[] Coastline = ProjectAll(CoastCoordinates);
        public static readonly GeoPoint[] RashidRoute = ProjectAll(RashidCoordinates);

        public static GeoPoint Project(double latitude, double longitude)
        {
            const double kmPerDegree = 111.32;
            return new GeoPoint(
                (float)((longitude - OriginLongitude) * kmPerDegree *
                    Math.Cos(OriginLatitude * Math.PI / 180) * UnitsPerKilometre),
                (float)((latitude - OriginLatitude) * kmPerDegree * UnitsPerKilometre));
        }

        public static GeoPoint DistrictPoint(int index)
        {
            var district = Districts[index];
            return Project(district.latitude, district.longitude);
        }

        private static GeoPoint[] ProjectAll(GeoCoordinate[] coordinates)
        {
            var result = new GeoPoint[coordinates.Length];
            for (int i = 0; i < coordinates.Length; i++)
                result[i] = Project(coordinates[i].latitude, coordinates[i].longitude);
            return result;
        }

        public static float MapMinX { get { return Extreme(true, false) - 35; } }
        public static float MapMaxX { get { return Extreme(true, true) + 25; } }
        public static float MapMinZ { get { return Extreme(false, false) - 20; } }
        public static float MapMaxZ { get { return Extreme(false, true) + 20; } }

        private static float Extreme(bool x, bool maximum)
        {
            float result = maximum ? float.MinValue : float.MaxValue;
            for (int i = 0; i < Districts.Length + Coastline.Length + RashidRoute.Length; i++)
            {
                GeoPoint p = i < Districts.Length ? DistrictPoint(i) :
                    i < Districts.Length + Coastline.Length ? Coastline[i - Districts.Length] :
                    RashidRoute[i - Districts.Length - Coastline.Length];
                float value = x ? p.x : p.z;
                result = maximum ? Math.Max(result, value) : Math.Min(result, value);
            }
            return result;
        }
    }
}