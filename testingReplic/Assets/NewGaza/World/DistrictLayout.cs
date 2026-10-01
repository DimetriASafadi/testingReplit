using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewGaza
{
    /// <summary>
    /// Deterministic, illustrative street-and-block placement. The layout is a generated
    /// presentation policy, not a surveyed or official neighborhood boundary. Inland
    /// streets use a bent north-south spine and two staggered side branches, not a loop.
    /// </summary>
    internal sealed class DistrictLayout
    {
        internal const float SidewalkWidth = .42f;
        internal const float MaximumFootprintRadius = 15.6f;

        internal struct Street
        {
            internal Vector3 start;
            internal Vector3 end;
            internal float width;
        }

        internal struct Plot
        {
            internal Vector3 position;
            internal Vector3 size;
            internal Quaternion rotation;
        }

        private struct UtilityCandidate
        {
            internal Vector3 position;
            internal Quaternion rotation;
            internal int streetIndex;
            internal float side;
        }

        private struct UtilityReservation
        {
            internal Vector3 position;
            internal Quaternion rotation;
            internal float radius;
        }

        private struct PlotCandidate
        {
            internal Plot plot;
        }

        private readonly List<UtilityCandidate> utilityCandidates = new List<UtilityCandidate>();
        private readonly UtilityReservation[] utilityReservations = new UtilityReservation[3];
        private uint randomState;

        internal Street[] Streets { get; private set; }
        internal Plot[] Plots { get; private set; }
        internal float FootprintRadius { get; private set; }

        private DistrictLayout(uint seed)
        {
            randomState = seed == 0 ? 0x6D2B79F5u : seed;
        }

        internal static DistrictLayout Create(string districtId, int plotCount)
        {
            if (plotCount < 0) throw new ArgumentOutOfRangeException(nameof(plotCount));
            var layout = new DistrictLayout(StableSeed(districtId));
            layout.Build(plotCount);
            return layout;
        }

        private void Build(int plotCount)
        {
            float layoutYaw = Range(-15f, 15f);
            Vector3[] main = BuildMainStreet(layoutYaw);
            Vector3[] westBranch = BuildBranch(main[1],
                Rotate(new Vector2(-5.1f, -4.8f), layoutYaw),
                Rotate(new Vector2(-10f, -4.5f), layoutYaw));
            Vector3[] eastBranch = BuildBranch(main[2],
                Rotate(new Vector2(5.1f, 4.2f), layoutYaw),
                Rotate(new Vector2(10.5f, 5f), layoutYaw));
            var streets = new List<Street>();
            AddStreetPath(streets, main, 1.32f);
            AddStreetPath(streets, westBranch, 1.05f);
            AddStreetPath(streets, eastBranch, 1.05f);
            Streets = streets.ToArray();

            BuildUtilityCandidates();
            ReserveRoadsidePositions();

            Plots = PlacePlots(plotCount);
            FootprintRadius = MeasureFootprintRadius();
            if (FootprintRadius > MaximumFootprintRadius + .001f)
                throw new InvalidOperationException("District street, plot, or utility layout exceeds its local footprint clearance.");
            if (!VerifyNoPlotOverlaps())
                throw new InvalidOperationException("District layout contains intersecting project plots.");
        }

        private Vector3[] BuildMainStreet(float layoutYaw)
        {
            Vector2[] points =
            {
                new Vector2(0f, -12f),
                new Vector2(1f, -4f),
                new Vector2(-.6f, 4f),
                new Vector2(.6f, 12f)
            };
            var result = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 point = points[i];
                point.x += Range(i == 0 || i == points.Length - 1 ? -.18f : -.42f,
                    i == 0 || i == points.Length - 1 ? .18f : .42f);
                if (i > 0 && i < points.Length - 1)
                    point.y += Range(-.28f, .28f);
                point = Rotate(point, layoutYaw);
                result[i] = new Vector3(point.x, 0f, point.y);
            }
            return result;
        }

        private static Vector2 Rotate(Vector2 point, float degrees)
        {
            float angle = degrees * (float)Math.PI / 180f;
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return new Vector2(point.x * cosine + point.y * sine,
                -point.x * sine + point.y * cosine);
        }

        private Vector3[] BuildBranch(Vector3 start, params Vector2[] ends)
        {
            var result = new Vector3[ends.Length + 1];
            result[0] = start;
            for (int i = 0; i < ends.Length; i++)
            {
                Vector2 point = ends[i];
                point.x += Range(-.38f, .38f);
                point.y += Range(-.32f, .32f);
                result[i + 1] = new Vector3(point.x, 0f, point.y);
            }
            return result;
        }

        private static void AddStreetPath(List<Street> streets, Vector3[] points, float width)
        {
            for (int i = 1; i < points.Length; i++)
            {
                if ((points[i] - points[i - 1]).sqrMagnitude < .01f) continue;
                streets.Add(new Street { start = points[i - 1], end = points[i], width = width });
            }
        }

        private Plot[] PlacePlots(int plotCount)
        {
            float[] widthScales = { 1f, .96f, .92f, .88f, .84f, .8f, .76f, .72f, .68f };
            int bestCount = 0;
            int bestCandidates = 0;
            float bestScale = widthScales[0];
            string bestPositions = "";
            for (int sizePass = 0; sizePass < widthScales.Length; sizePass++)
            {
                var candidates = BuildPlotCandidates(widthScales[sizePass]);
                if (candidates.Count > bestCandidates)
                {
                    bestCandidates = candidates.Count;
                    bestScale = widthScales[sizePass];
                    bestPositions = "";
                    for (int c = 0; c < candidates.Count; c++)
                        bestPositions += candidates[c].plot.position + "; ";
                }
                for (int retry = 0; retry < 80; retry++)
                {
                    Shuffle(candidates);
                    var selected = new List<Plot>(plotCount);
                    for (int i = 0; i < candidates.Count && selected.Count < plotCount; i++)
                    {
                        Plot candidate = candidates[i].plot;
                        bool overlaps = false;
                        for (int p = 0; p < selected.Count; p++)
                            if (PlotsOverlap(candidate, selected[p], .18f))
                            {
                                overlaps = true;
                                break;
                            }
                        if (!overlaps) selected.Add(candidate);
                    }
                    if (selected.Count > bestCount) bestCount = selected.Count;
                    if (selected.Count == plotCount) return selected.ToArray();
                }
                var exactSelection = new List<Plot>(plotCount);
                int searchNodes = 0;
                Shuffle(candidates);
                if (TryFindPlots(candidates, 0, exactSelection, plotCount, ref searchNodes, 250000))
                    return exactSelection.ToArray();
            }
            throw new InvalidOperationException("Could not fit all " + plotCount +
                " project plots and reserved street-side utilities inside the district footprint; best was " +
                bestCount + "/" + plotCount + " from " + bestCandidates + " candidates at size " +
                bestScale + ": " + bestPositions);
        }

        private static bool TryFindPlots(List<PlotCandidate> candidates, int start,
            List<Plot> selected, int targetCount, ref int searchNodes, int nodeLimit)
        {
            if (selected.Count == targetCount) return true;
            if (candidates.Count - start < targetCount - selected.Count) return false;
            for (int i = start; i < candidates.Count; i++)
            {
                if (++searchNodes > nodeLimit) return false;
                Plot candidate = candidates[i].plot;
                bool overlaps = false;
                for (int p = 0; p < selected.Count; p++)
                    if (PlotsOverlap(candidate, selected[p], .18f))
                    {
                        overlaps = true;
                        break;
                    }
                if (overlaps) continue;
                selected.Add(candidate);
                if (TryFindPlots(candidates, i + 1, selected, targetCount,
                    ref searchNodes, nodeLimit))
                    return true;
                selected.RemoveAt(selected.Count - 1);
            }
            return false;
        }

        private List<PlotCandidate> BuildPlotCandidates(float sizeScale)
        {
            var candidates = new List<PlotCandidate>();
            for (int i = 0; i < Streets.Length; i++)
            {
                Street street = Streets[i];
                Vector3 delta = street.end - street.start;
                float length = delta.magnitude;
                if (length < 3.5f) continue;
                Vector3 tangent = delta / length;
                Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
                float[] stations = { .06f, .5f, .94f };

                for (int station = 0; station < stations.Length; station++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float depth = Range(2.5f, 2.8f) * sizeScale;
                        float frontage = Range(3.0f, 3.45f) * sizeScale;
                        float edgeInset = frontage * .5f + .08f;
                        if (length < edgeInset * 2f) continue;
                        float minT = edgeInset / length;
                        float maxT = 1f - minT;
                        float along = Mathf.Lerp(minT, maxT, stations[station]);
                        along = Mathf.Clamp(along + Range(-.012f, .012f), minT, maxT);
                        var plot = new Plot
                        {
                            position = Vector3.Lerp(street.start, street.end, along) +
                                normal * side * (street.width * .5f + SidewalkWidth + .2f + depth * .5f),
                            size = new Vector3(depth, 0f, frontage),
                            rotation = rotation
                        };
                        if (GetPlotRadius(plot) > MaximumFootprintRadius ||
                            !PlotClearsStreetNetwork(plot))
                            continue;
                        bool utilityOverlap = false;
                        for (int u = 0; u < utilityReservations.Length; u++)
                            if (CircleOverlapsPlot(utilityReservations[u].position,
                                utilityReservations[u].radius + .16f, plot))
                            {
                                utilityOverlap = true;
                                break;
                            }
                        if (!utilityOverlap)
                            candidates.Add(new PlotCandidate { plot = plot });
                    }
                }
            }
            return candidates;
        }

        private void BuildUtilityCandidates()
        {
            for (int i = 0; i < Streets.Length; i++)
            {
                Street street = Streets[i];
                Vector3 delta = street.end - street.start;
                if (delta.sqrMagnitude < .01f) continue;
                Vector3 tangent = delta.normalized;
                Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
                float[] stations = { .27f, .5f, .73f };
                for (int station = 0; station < stations.Length; station++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        utilityCandidates.Add(new UtilityCandidate
                        {
                            position = Vector3.Lerp(street.start, street.end, stations[station]),
                            rotation = rotation,
                            streetIndex = i,
                            side = side
                        });
                    }
                }
            }
        }

        private void ReserveRoadsidePositions()
        {
            float[] radii = { 1.25f, .72f, 3.15f };
            int[] placementOrder = { 2, 0, 1 };
            for (int order = 0; order < placementOrder.Length; order++)
            {
                int slot = placementOrder[order];
                float radius = radii[slot];
                int start = (slot * 19 + (int)(randomState % (uint)utilityCandidates.Count)) %
                    utilityCandidates.Count;
                bool placed = false;
                for (int i = 0; i < utilityCandidates.Count; i++)
                {
                    UtilityCandidate candidate = utilityCandidates[(start + i) % utilityCandidates.Count];
                    Street street = Streets[candidate.streetIndex];
                    Vector3 tangent = (street.end - street.start).normalized;
                    Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                    float shoulder = street.width * .5f + SidewalkWidth;
                    Vector3 position = candidate.position +
                        normal * candidate.side * (shoulder + radius + .24f);
                    if (position.magnitude + radius > MaximumFootprintRadius ||
                        !CircleClearsStreetNetwork(position, radius + .02f))
                        continue;

                    bool overlapsReservation = false;
                    for (int r = 0; r < utilityReservations.Length; r++)
                    {
                        UtilityReservation existing = utilityReservations[r];
                        if (existing.radius > 0f &&
                            (position - existing.position).sqrMagnitude <
                            Mathf.Pow(radius + existing.radius + .35f, 2f))
                        {
                            overlapsReservation = true;
                            break;
                        }
                    }
                    if (overlapsReservation) continue;

                    utilityReservations[slot] = new UtilityReservation
                    {
                        position = position,
                        rotation = candidate.rotation,
                        radius = radius
                    };
                    placed = true;
                    break;
                }
                if (!placed)
                    throw new InvalidOperationException("Could not reserve all collision-free roadside utilities inside the district footprint.");
            }
        }

        /// <summary>Returns the pre-reserved location: 0 salvage, 1 badge, 2 crane.</summary>
        internal Vector3 FindRoadsidePosition(float radius, int stableOffset,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (stableOffset < 0 || stableOffset >= utilityReservations.Length)
                throw new ArgumentOutOfRangeException(nameof(stableOffset));
            UtilityReservation reserved = utilityReservations[stableOffset];
            if (Mathf.Abs(reserved.radius - radius) > .001f)
                throw new ArgumentException("Roadside utility radius does not match its reserved slot.", nameof(radius));
            rotation = reserved.rotation;
            return reserved.position;
        }

        private bool PlotClearsStreetNetwork(Plot plot)
        {
            Vector3 plotRight = plot.rotation * Vector3.right;
            Vector3 plotForward = plot.rotation * Vector3.forward;
            for (int i = 0; i < Streets.Length; i++)
            {
                Street street = Streets[i];
                Vector3 delta = street.end - street.start;
                float length = delta.magnitude;
                Vector3 tangent = delta / length;
                Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                Vector3 midpoint = (street.start + street.end) * .5f;
                if (OverlapsBoxes(plot.position, plotRight, plotForward, plot.size.x * .5f,
                    plot.size.z * .5f, midpoint, normal, tangent, street.width * .5f,
                    length * .5f + .06f, .02f))
                    return false;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 sidewalkCenter = midpoint + normal * side *
                        (street.width * .5f + SidewalkWidth * .5f);
                    if (OverlapsBoxes(plot.position, plotRight, plotForward,
                        plot.size.x * .5f, plot.size.z * .5f, sidewalkCenter, normal, tangent,
                        SidewalkWidth * .5f, length * .5f + .07f, .02f))
                        return false;
                    Vector3 curbCenter = midpoint + normal * side * (street.width * .5f + .055f);
                    if (OverlapsBoxes(plot.position, plotRight, plotForward,
                        plot.size.x * .5f, plot.size.z * .5f, curbCenter, normal, tangent,
                        .055f, length * .5f + .04f, .02f))
                        return false;
                }
            }
            return true;
        }

        private bool CircleClearsStreetNetwork(Vector3 center, float radius)
        {
            for (int i = 0; i < Streets.Length; i++)
            {
                Street street = Streets[i];
                Vector3 delta = street.end - street.start;
                float length = delta.magnitude;
                Vector3 tangent = delta / length;
                Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                Vector3 midpoint = (street.start + street.end) * .5f;
                if (CircleOverlapsBox(center, radius, midpoint, normal, tangent,
                    street.width * .5f, length * .5f + .06f))
                    return false;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 sidewalkCenter = midpoint + normal * side *
                        (street.width * .5f + SidewalkWidth * .5f);
                    if (CircleOverlapsBox(center, radius, sidewalkCenter, normal, tangent,
                        SidewalkWidth * .5f, length * .5f + .07f))
                        return false;
                    Vector3 curbCenter = midpoint + normal * side * (street.width * .5f + .055f);
                    if (CircleOverlapsBox(center, radius, curbCenter, normal, tangent,
                        .055f, length * .5f + .04f))
                        return false;
                }
            }
            return true;
        }

        private static bool CircleOverlapsBox(Vector3 center, float radius, Vector3 boxCenter,
            Vector3 right, Vector3 forward, float halfWidth, float halfDepth)
        {
            Vector3 delta = center - boxCenter;
            float x = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, right)) - halfWidth);
            float z = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, forward)) - halfDepth);
            return x * x + z * z < radius * radius;
        }

        private static bool OverlapsBoxes(Vector3 centerA, Vector3 rightA, Vector3 forwardA,
            float halfWidthA, float halfDepthA, Vector3 centerB, Vector3 rightB,
            Vector3 forwardB, float halfWidthB, float halfDepthB, float margin)
        {
            Vector3[] axes = { rightA, forwardA, rightB, forwardB };
            Vector3 delta = centerB - centerA;
            for (int i = 0; i < axes.Length; i++)
            {
                Vector3 axis = axes[i];
                float distance = Mathf.Abs(Vector3.Dot(delta, axis));
                float radiusA = Mathf.Abs(Vector3.Dot(rightA, axis)) * halfWidthA +
                    Mathf.Abs(Vector3.Dot(forwardA, axis)) * halfDepthA;
                float radiusB = Mathf.Abs(Vector3.Dot(rightB, axis)) * halfWidthB +
                    Mathf.Abs(Vector3.Dot(forwardB, axis)) * halfDepthB;
                if (distance >= radiusA + radiusB + margin) return false;
            }
            return true;
        }

        private static bool CircleOverlapsPlot(Vector3 center, float radius, Plot plot)
        {
            Vector3 right = plot.rotation * Vector3.right;
            Vector3 forward = plot.rotation * Vector3.forward;
            Vector3 delta = center - plot.position;
            float x = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, right)) - plot.size.x * .5f);
            float z = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, forward)) - plot.size.z * .5f);
            return x * x + z * z < radius * radius;
        }

        private float MeasureFootprintRadius()
        {
            float radius = 0f;
            foreach (Street street in Streets)
            {
                Vector3 delta = street.end - street.start;
                float length = delta.magnitude;
                if (length < .001f) continue;
                Vector3 tangent = delta / length;
                Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                float edge = street.width * .5f + SidewalkWidth;
                Vector3 extension = tangent * .08f;
                radius = Mathf.Max(radius,
                    (street.start + extension + normal * edge).magnitude,
                    (street.start + extension - normal * edge).magnitude,
                    (street.end - extension + normal * edge).magnitude,
                    (street.end - extension - normal * edge).magnitude);
            }
            foreach (Plot plot in Plots)
                radius = Mathf.Max(radius, GetPlotRadius(plot));
            foreach (UtilityReservation utility in utilityReservations)
                radius = Mathf.Max(radius, utility.position.magnitude + utility.radius);
            return radius;
        }

        private static float GetPlotRadius(Plot plot)
        {
            Vector3 right = plot.rotation * Vector3.right * (plot.size.x * .5f);
            Vector3 forward = plot.rotation * Vector3.forward * (plot.size.z * .5f);
            float radius = 0f;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    radius = Mathf.Max(radius, (plot.position + right * x + forward * z).magnitude);
            return radius;
        }

        /// <summary>Oriented-box collision check with a small frontage clearance margin.</summary>
        internal bool VerifyNoPlotOverlaps()
        {
            if (Plots == null) return true;
            for (int i = 0; i < Plots.Length; i++)
                for (int j = i + 1; j < Plots.Length; j++)
                    if (PlotsOverlap(Plots[i], Plots[j], .18f)) return false;
            return true;
        }

        private static bool PlotsOverlap(Plot a, Plot b, float margin)
        {
            Vector3[] axes =
            {
                a.rotation * Vector3.right, a.rotation * Vector3.forward,
                b.rotation * Vector3.right, b.rotation * Vector3.forward
            };
            Vector3 delta = b.position - a.position;
            for (int i = 0; i < axes.Length; i++)
            {
                Vector3 axis = axes[i];
                float centerDistance = Mathf.Abs(Vector3.Dot(delta, axis));
                float radiusA = Mathf.Abs(Vector3.Dot(a.rotation * Vector3.right, axis)) *
                    a.size.x * .5f + Mathf.Abs(Vector3.Dot(a.rotation * Vector3.forward, axis)) *
                    a.size.z * .5f;
                float radiusB = Mathf.Abs(Vector3.Dot(b.rotation * Vector3.right, axis)) *
                    b.size.x * .5f + Mathf.Abs(Vector3.Dot(b.rotation * Vector3.forward, axis)) *
                    b.size.z * .5f;
                if (centerDistance >= radiusA + radiusB + margin) return false;
            }
            return true;
        }

        private float Range(float min, float max)
        {
            return Mathf.Lerp(min, max, Next01());
        }

        private float Next01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00FFFFFFu) / 16777216f;
        }

        private void Shuffle(List<PlotCandidate> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = (int)(Next01() * (i + 1));
                PlotCandidate swap = values[i];
                values[i] = values[j];
                values[j] = swap;
            }
        }

        private static uint StableSeed(string id)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(id))
                    for (int i = 0; i < id.Length; i++)
                    {
                        hash ^= id[i];
                        hash *= 16777619u;
                    }
                return hash;
            }
        }
    }
}