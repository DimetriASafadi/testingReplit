using System;
using System.IO;
using System.Text.Json;
using NewGaza;
using UnityEngine;

internal static class Program
{
    private static readonly string[] InlandIds =
    {
        "shujaiya", "tuffah", "sheikh-radwan", "daraj", "karama", "old-city",
        "nasr", "sabra", "zeitoun", "rimal", "tel-al-hawa", "sheikh-ijlin"
    };

    private struct Box2
    {
        internal Vector3 center;
        internal Vector3 right;
        internal Vector3 forward;
        internal float halfWidth;
        internal float halfDepth;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--export-preview")
        {
            ExportPreviewLayout(args[1]);
            return 0;
        }
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: LayoutTests [--export-preview <json-path>]");
            return 2;
        }

        int layouts = 0;
        foreach (string id in InlandIds)
        {
            DistrictLayout officialNinePlot = DistrictLayout.Create(id, 9);
            VerifyLayout(id, 9, officialNinePlot);
            layouts++;

            for (int count = 6; count <= 10; count++)
            {
                DistrictLayout layout = DistrictLayout.Create(id, count);
                VerifyLayout(id, count, layout);
                layouts++;
            }
        }

        Console.WriteLine("Passed " + layouts + " deterministic layout fixtures across 12 catalog IDs.");
        return 0;
    }

    private static void ExportPreviewLayout(string outputPath)
    {
        DistrictLayout layout = DistrictLayout.Create("shujaiya", 9);
        var plots = new object[layout.Plots.Length];
        for (int i = 0; i < layout.Plots.Length; i++)
        {
            DistrictLayout.Plot plot = layout.Plots[i];
            Vector3 forward = plot.rotation * Vector3.forward;
            double yawDegrees = Math.Atan2(forward.x, forward.z) * (180.0 / Math.PI);
            plots[i] = new
            {
                position = new[] { plot.position.x, plot.position.y, plot.position.z },
                size = new[] { plot.size.x, plot.size.y, plot.size.z },
                yawDegrees
            };
        }

        var streets = new object[layout.Streets.Length];
        for (int i = 0; i < layout.Streets.Length; i++)
        {
            DistrictLayout.Street street = layout.Streets[i];
            streets[i] = new
            {
                start = new[] { street.start.x, street.start.y, street.start.z },
                end = new[] { street.end.x, street.end.y, street.end.z },
                width = street.width,
                sidewalkWidth = DistrictLayout.SidewalkWidth
            };
        }

        float[] utilityRadii = { 1.25f, .72f, 3.15f };
        string[] utilityIds = { "salvage", "badge", "crane" };
        var utilities = new object[utilityRadii.Length];
        for (int i = 0; i < utilityRadii.Length; i++)
        {
            Vector3 position = layout.FindRoadsidePosition(utilityRadii[i], i, out Quaternion rotation);
            Vector3 forward = rotation * Vector3.forward;
            double yawDegrees = Math.Atan2(forward.x, forward.z) * (180.0 / Math.PI);
            utilities[i] = new
            {
                id = utilityIds[i],
                model = i == 0 ? "rubble_heap" : null,
                position = new[] { position.x, position.y, position.z },
                yawDegrees,
                radius = utilityRadii[i]
            };
        }

        var document = new
        {
            formatVersion = 1,
            districtId = "shujaiya",
            districtIndex = 0,
            requestedPlotCount = 9,
            coordinateSystem = "Unity-local XYZ, Y-up; layout units",
            footprintRadius = layout.FootprintRadius,
            plots,
            streets,
            utilities
        };
        string fullPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(document,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine("Exported production DistrictLayout.Create(\"shujaiya\", 9) preview data to " +
            fullPath);
    }

    private static void VerifyLayout(string id, int requestedPlots, DistrictLayout layout)
    {
        Require(layout.Plots.Length == requestedPlots,
            id + ": requested " + requestedPlots + " plots, got " + layout.Plots.Length);
        Require(layout.VerifyNoPlotOverlaps(), id + ": plot SAT verification failed.");

        foreach (DistrictLayout.Plot plot in layout.Plots)
        {
            Box2 plotBox = PlotBox(plot);
            Require(BoxRadius(plotBox) <= DistrictLayout.MaximumFootprintRadius + .001f,
                id + ": a plot corner exceeds the footprint radius.");

            for (int i = 0; i < layout.Streets.Length; i++)
            {
                DistrictLayout.Street street = layout.Streets[i];
                Vector3 delta = street.end - street.start;
                float length = delta.magnitude;
                Vector3 tangent = delta / length;
                Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                Vector3 midpoint = (street.start + street.end) * .5f;
                var carriageway = MakeBox(midpoint, tangent, street.width * .5f,
                    length * .5f + .06f);
                Require(!Overlaps(plotBox, carriageway, .02f),
                    id + ": plot overlaps a carriageway.");

                for (int side = -1; side <= 1; side += 2)
                {
                    var sidewalk = MakeBox(midpoint + normal * side *
                        (street.width * .5f + DistrictLayout.SidewalkWidth * .5f),
                        tangent, DistrictLayout.SidewalkWidth * .5f, length * .5f + .07f);
                    Require(!Overlaps(plotBox, sidewalk, .02f),
                        id + ": plot overlaps a sidewalk.");
                    var curb = MakeBox(midpoint + normal * side * (street.width * .5f + .055f),
                        tangent, .055f, length * .5f + .04f);
                    Require(!Overlaps(plotBox, curb, .02f), id + ": plot overlaps a curb.");
                }
            }
        }

        float[] utilityRadii = { 1.25f, .72f, 3.15f };
        var utilityPositions = new Vector3[utilityRadii.Length];
        for (int i = 0; i < utilityRadii.Length; i++)
        {
            utilityPositions[i] = layout.FindRoadsidePosition(utilityRadii[i], i, out _);
            Require(utilityPositions[i].magnitude + utilityRadii[i] <=
                DistrictLayout.MaximumFootprintRadius + .001f,
                id + ": a roadside utility exceeds the footprint radius.");
            for (int p = 0; p < layout.Plots.Length; p++)
                Require(!CircleOverlapsPlot(utilityPositions[i], utilityRadii[i] + .16f,
                    layout.Plots[p]), id + ": roadside utility overlaps a plot.");
            for (int j = 0; j < i; j++)
                Require((utilityPositions[i] - utilityPositions[j]).magnitude >=
                    utilityRadii[i] + utilityRadii[j] + .35f - .001f,
                    id + ": utility reservations overlap.");
            VerifyUtilityStreetClearance(id, layout, utilityPositions[i], utilityRadii[i]);
        }
        Require(layout.FootprintRadius <= DistrictLayout.MaximumFootprintRadius + .001f,
            id + ": aggregate footprint radius exceeds its bound.");
    }

    private static void VerifyUtilityStreetClearance(string id, DistrictLayout layout,
        Vector3 position, float radius)
    {
        for (int i = 0; i < layout.Streets.Length; i++)
        {
            DistrictLayout.Street street = layout.Streets[i];
            Vector3 delta = street.end - street.start;
            float length = delta.magnitude;
            Vector3 tangent = delta / length;
            Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
            Vector3 midpoint = (street.start + street.end) * .5f;
            var road = MakeBox(midpoint, tangent, street.width * .5f, length * .5f + .06f);
            Require(!CircleOverlapsBox(position, radius + .02f, road),
                id + ": roadside prop overlaps a carriageway.");
            for (int side = -1; side <= 1; side += 2)
            {
                var sidewalk = MakeBox(midpoint + normal * side *
                    (street.width * .5f + DistrictLayout.SidewalkWidth * .5f), tangent,
                    DistrictLayout.SidewalkWidth * .5f, length * .5f + .07f);
                Require(!CircleOverlapsBox(position, radius + .02f, sidewalk),
                    id + ": roadside prop overlaps a sidewalk.");
                var curb = MakeBox(midpoint + normal * side * (street.width * .5f + .055f),
                    tangent, .055f, length * .5f + .04f);
                Require(!CircleOverlapsBox(position, radius + .02f, curb),
                    id + ": roadside prop overlaps a curb.");
            }
        }
    }

    private static Box2 PlotBox(DistrictLayout.Plot plot)
    {
        return new Box2
        {
            center = plot.position,
            right = plot.rotation * Vector3.right,
            forward = plot.rotation * Vector3.forward,
            halfWidth = plot.size.x * .5f,
            halfDepth = plot.size.z * .5f
        };
    }

    private static Box2 MakeBox(Vector3 center, Vector3 forward, float halfWidth, float halfDepth)
    {
        return new Box2
        {
            center = center,
            right = new Vector3(forward.z, 0f, -forward.x),
            forward = forward,
            halfWidth = halfWidth,
            halfDepth = halfDepth
        };
    }

    private static float BoxRadius(Box2 box)
    {
        float max = 0f;
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = box.center + box.right * (box.halfWidth * x) +
                    box.forward * (box.halfDepth * z);
                max = Math.Max(max, corner.magnitude);
            }
        return max;
    }

    private static bool Overlaps(Box2 a, Box2 b, float margin)
    {
        Vector3[] axes = { a.right, a.forward, b.right, b.forward };
        Vector3 delta = b.center - a.center;
        foreach (Vector3 axis in axes)
        {
            float distance = Math.Abs(Vector3.Dot(delta, axis));
            float radiusA = Math.Abs(Vector3.Dot(a.right, axis)) * a.halfWidth +
                Math.Abs(Vector3.Dot(a.forward, axis)) * a.halfDepth;
            float radiusB = Math.Abs(Vector3.Dot(b.right, axis)) * b.halfWidth +
                Math.Abs(Vector3.Dot(b.forward, axis)) * b.halfDepth;
            if (distance >= radiusA + radiusB + margin) return false;
        }
        return true;
    }

    private static bool CircleOverlapsBox(Vector3 center, float radius, Box2 box)
    {
        Vector3 delta = center - box.center;
        float x = Math.Max(0f, Math.Abs(Vector3.Dot(delta, box.right)) - box.halfWidth);
        float z = Math.Max(0f, Math.Abs(Vector3.Dot(delta, box.forward)) - box.halfDepth);
        return x * x + z * z < radius * radius;
    }

    private static bool CircleOverlapsPlot(Vector3 center, float radius, DistrictLayout.Plot plot)
    {
        return CircleOverlapsBox(center, radius, PlotBox(plot));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}