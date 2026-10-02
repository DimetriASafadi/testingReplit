using System;
using System.Collections.Generic;
using System.Globalization;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Immutable-by-convention street geometry and its authored-vertex routing graph.</summary>
    public sealed class CityRoadNetwork
    {
        private const float CityUnitsPerKilometre = 50f;
        private const float MetresPerCityUnit = 20f;
        private const float VertexQuantum = .001f;
        private const float AccessLimit = 5f;
        private const float PickCellSize = 24f;

        private struct VertexKey : IEquatable<VertexKey>
        {
            internal readonly int x, z;
            internal VertexKey(Vector3 point)
            {
                x = (int)Math.Round(point.x / VertexQuantum);
                z = (int)Math.Round(point.z / VertexQuantum);
            }
            public bool Equals(VertexKey other) { return x == other.x && z == other.z; }
            public override bool Equals(object obj) { return obj is VertexKey && Equals((VertexKey)obj); }
            public override int GetHashCode() { unchecked { return x * 397 ^ z; } }
        }

        private struct Cell : IEquatable<Cell>
        {
            internal readonly int x, z;
            internal Cell(int x, int z) { this.x = x; this.z = z; }
            public bool Equals(Cell other) { return x == other.x && z == other.z; }
            public override bool Equals(object obj) { return obj is Cell && Equals((Cell)obj); }
            public override int GetHashCode() { unchecked { return x * 397 ^ z; } }
        }

        private sealed class GraphNode
        {
            internal Vector3 point;
            internal readonly List<GraphEdge> edges = new List<GraphEdge>();
            internal GraphNode(Vector3 position) { point = position; }
        }

        private sealed class GraphEdge
        {
            internal GraphNode a, b;
            internal float length, baseSpeed;
            internal string roadId;
        }

        private struct QueueItem
        {
            internal GraphNode node;
            internal float cost;
        }

        private sealed class MinQueue
        {
            private readonly List<QueueItem> items = new List<QueueItem>();
            internal int Count { get { return items.Count; } }
            internal void Push(GraphNode node, float cost)
            {
                var item = new QueueItem { node = node, cost = cost };
                int index = items.Count;
                items.Add(item);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (items[parent].cost <= cost) break;
                    items[index] = items[parent];
                    index = parent;
                }
                items[index] = item;
            }
            internal QueueItem Pop()
            {
                QueueItem result = items[0];
                QueueItem last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0) return result;
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= items.Count) break;
                    int right = left + 1;
                    int child = right < items.Count && items[right].cost < items[left].cost
                        ? right : left;
                    if (items[child].cost >= last.cost) break;
                    items[index] = items[child];
                    index = child;
                }
                items[index] = last;
                return result;
            }
        }

        private readonly Dictionary<Cell, List<CityRoadSegment>> pickGrid =
            new Dictionary<Cell, List<CityRoadSegment>>();
        private readonly List<GraphEdge> graphEdges = new List<GraphEdge>();
        private readonly Dictionary<string, CityRoadSegment> byId =
            new Dictionary<string, CityRoadSegment>(StringComparer.Ordinal);

        public RoadSegmentDefinition[] Definitions { get; private set; }
        public CityRoadSegment[] Segments { get; private set; }

        public CityRoadNetwork(CityBasemap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.roads == null) throw new ArgumentException("Basemap has no road ways.", nameof(map));

            var segments = new List<CityRoadSegment>();
            var definitions = new List<RoadSegmentDefinition>();
            var authoredNodes = new Dictionary<VertexKey, GraphNode>();
            var wayLocalNodes = new Dictionary<string, Dictionary<VertexKey, GraphNode>>(StringComparer.Ordinal);
            var nextOrdinalByFeature = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < map.roads.Length; i++)
            {
                CityBasemapRoad road = map.roads[i];
                if (road == null || road.points == null || road.points.Length < 2) continue;
                int firstOrdinal;
                nextOrdinalByFeature.TryGetValue(road.FeatureId, out firstOrdinal);
                List<CityRoadSegment> chunks = SplitRoad(road, firstOrdinal);
                nextOrdinalByFeature[road.FeatureId] = firstOrdinal + chunks.Count;
                for (int c = 0; c < chunks.Count; c++)
                {
                    CityRoadSegment segment = chunks[c];
                    segments.Add(segment);
                    definitions.Add(segment.Definition);
                    byId.Add(segment.Definition.id, segment);
                    AddPickSegment(segment);
                    if (IsDrivable(segment.Kind))
                    {
                        Dictionary<VertexKey, GraphNode> localNodes;
                        if (!wayLocalNodes.TryGetValue(road.FeatureId, out localNodes))
                        {
                            localNodes = new Dictionary<VertexKey, GraphNode>();
                            wayLocalNodes.Add(road.FeatureId, localNodes);
                        }
                        AddGraphEdges(road, segment, authoredNodes, localNodes);
                    }
                }
            }
            Definitions = definitions.ToArray();
            Segments = segments.ToArray();
        }

        public CityRoadSegment FindSegment(string id)
        {
            CityRoadSegment segment;
            return id != null && byId.TryGetValue(id, out segment) ? segment : null;
        }

        public bool TryPick(Vector3 point, float tolerance, out CityRoadSegment selected)
        {
            selected = null;
            if (tolerance < 0f || float.IsNaN(tolerance) || float.IsInfinity(tolerance)) return false;
            int minX = CellCoordinate(point.x - tolerance), maxX = CellCoordinate(point.x + tolerance);
            int minZ = CellCoordinate(point.z - tolerance), maxZ = CellCoordinate(point.z + tolerance);
            var seen = new HashSet<CityRoadSegment>();
            float bestRibbonDistance = tolerance;
            float bestCenterlineDistance = float.PositiveInfinity;
            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    List<CityRoadSegment> bucket;
                    if (!pickGrid.TryGetValue(new Cell(x, z), out bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        CityRoadSegment segment = bucket[i];
                        if (!seen.Add(segment)) continue;
                        float centerlineDistance = (float)Math.Sqrt(segment.DistanceSquared(point));
                        float ribbonDistance = Math.Max(0f, centerlineDistance -
                            Mathf.Max(.12f, segment.Width) * .5f);
                        if (ribbonDistance <= tolerance &&
                            (ribbonDistance < bestRibbonDistance - .00001f ||
                             (Math.Abs(ribbonDistance - bestRibbonDistance) <= .00001f &&
                              centerlineDistance < bestCenterlineDistance)))
                        {
                            bestRibbonDistance = ribbonDistance;
                            bestCenterlineDistance = centerlineDistance;
                            selected = segment;
                        }
                    }
                }
            return selected != null;
        }

        /// <summary>Finds a shortest expected-time route. Null means explicitly unreachable.</summary>
        public CityRoadRoute FindRoute(Vector3 from, Vector3 to, Func<string, float> speedMultiplier = null)
        {
            if (graphEdges.Count == 0) return null;
            Projection start = NearestProjection(from);
            Projection finish = NearestProjection(to);
            if (start == null || finish == null ||
                start.distance > AccessLimit || finish.distance > AccessLimit) return null;

            var distances = new Dictionary<GraphNode, float>();
            var previous = new Dictionary<GraphNode, GraphEdge>();
            var origin = new Dictionary<GraphNode, GraphNode>();
            var visited = new HashSet<GraphNode>();
            var queue = new MinQueue();
            Initialize(start, speedMultiplier, distances, queue, origin);
            GraphNode destinationNode = null;
            GraphNode destinationOrigin = null;
            float best = float.PositiveInfinity;
            bool directSameEdge = false;

            // A route can stay on the same physical edge without visiting a graph vertex.
            if (ReferenceEquals(start.edge, finish.edge))
            {
                float direct = TravelCost(start.point, finish.point, start.edge, speedMultiplier);
                best = direct;
                directSameEdge = true;
            }

            while (queue.Count > 0)
            {
                QueueItem item = queue.Pop();
                GraphNode current = item.node;
                if (item.cost > distances[current] + .000001f) continue;
                if (!visited.Add(current)) continue;
                float currentCost = distances[current];
                if (currentCost >= best) continue;

                if (ReferenceEquals(current, finish.edge.a) || ReferenceEquals(current, finish.edge.b))
                {
                    float finishCost = currentCost + TravelCost(current.point, finish.point,
                        finish.edge, speedMultiplier);
                    if (finishCost < best)
                    {
                        best = finishCost;
                        destinationNode = current;
                        destinationOrigin = origin[current];
                        directSameEdge = false;
                    }
                }

                for (int e = 0; e < current.edges.Count; e++)
                {
                    GraphEdge edge = current.edges[e];
                    GraphNode next = ReferenceEquals(edge.a, current) ? edge.b : edge.a;
                    float nextCost = currentCost + EdgeCost(edge, speedMultiplier);
                    float old;
                    if (!distances.TryGetValue(next, out old) || nextCost < old)
                    {
                        distances[next] = nextCost;
                        previous[next] = edge;
                        origin[next] = origin[current];
                        if (!visited.Contains(next)) queue.Push(next, nextCost);
                    }
                }
            }
            if (float.IsPositiveInfinity(best) || (!directSameEdge && destinationNode == null)) return null;

            var routePoints = new List<Vector3>();
            var routeIds = new List<string>();
            routePoints.Add(from);
            if (Vector3.Distance(from, start.point) > .0001f)
            {
                routePoints.Add(start.point);
                routeIds.Add(string.Empty);
            }
            if (directSameEdge)
            {
                AddLeg(routePoints, routeIds, finish.point, start.edge.roadId);
            }
            else
            {
                GraphNode routeStart = destinationOrigin;
                AddLeg(routePoints, routeIds, routeStart.point, start.edge.roadId);
                var pathNodes = new List<GraphNode>();
                GraphNode walk = destinationNode;
                pathNodes.Add(walk);
                while (!ReferenceEquals(walk, routeStart) && previous.ContainsKey(walk))
                {
                    GraphEdge edge = previous[walk];
                    walk = ReferenceEquals(edge.a, walk) ? edge.b : edge.a;
                    pathNodes.Add(walk);
                }
                pathNodes.Reverse();
                for (int n = 1; n < pathNodes.Count; n++)
                {
                    GraphNode a = pathNodes[n - 1], b = pathNodes[n];
                    GraphEdge edge = FindConnectingEdge(a, b);
                    AddLeg(routePoints, routeIds, b.point, edge == null ? string.Empty : edge.roadId);
                }
                AddLeg(routePoints, routeIds, finish.point, finish.edge.roadId);
            }
            if (Vector3.Distance(routePoints[routePoints.Count - 1], to) > .0001f)
            {
                routePoints.Add(to);
                routeIds.Add(string.Empty);
            }
            // Remove accidental duplicates while preserving one provenance entry per leg.
            return new CityRoadRoute(routePoints, routeIds);
        }

        private sealed class Projection
        {
            internal GraphEdge edge;
            internal Vector3 point;
            internal float distance, t;
            internal bool forward;
        }

        private Projection NearestProjection(Vector3 point)
        {
            Projection result = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < graphEdges.Count; i++)
            {
                GraphEdge edge = graphEdges[i];
                float t;
                Vector3 projected = Project(point, edge.a.point, edge.b.point, out t);
                float distance = Vector3.Distance(point, projected);
                if (distance < best)
                {
                    best = distance;
                    result = new Projection { edge = edge, point = projected,
                        distance = distance, t = t, forward = t < .5f };
                }
            }
            return result;
        }

        private static void Initialize(Projection projection, Func<string, float> speedMultiplier,
            Dictionary<GraphNode, float> distances, MinQueue queue,
            Dictionary<GraphNode, GraphNode> origin)
        {
            float speed = SafeMultiplier(speedMultiplier, projection.edge.roadId);
            float a = Vector3.Distance(projection.point, projection.edge.a.point) /
                (projection.edge.baseSpeed * speed);
            float b = Vector3.Distance(projection.point, projection.edge.b.point) /
                (projection.edge.baseSpeed * speed);
            distances[projection.edge.a] = a;
            distances[projection.edge.b] = b;
            origin[projection.edge.a] = projection.edge.a;
            origin[projection.edge.b] = projection.edge.b;
            queue.Push(projection.edge.a, a);
            queue.Push(projection.edge.b, b);
        }

        private static float EdgeCost(GraphEdge edge, Func<string, float> multiplier)
        {
            return edge.length / (edge.baseSpeed * SafeMultiplier(multiplier, edge.roadId));
        }

        private static float TravelCost(Vector3 a, Vector3 b, GraphEdge edge,
            Func<string, float> multiplier)
        {
            if (edge == null) return Vector3.Distance(a, b) / 1.1f;
            return Vector3.Distance(a, b) / (edge.baseSpeed * SafeMultiplier(multiplier, edge.roadId));
        }

        private static float AccessCost(float distance, GraphEdge edge, Func<string, float> multiplier)
        {
            return distance / (edge.baseSpeed * SafeMultiplier(multiplier, edge.roadId));
        }

        private static float SafeMultiplier(Func<string, float> callback, string id)
        {
            if (callback == null) return 1f;
            float value = callback(id);
            if (value <= .0001f || float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Road speed multiplier must be finite and positive for " + id + ".");
            return value;
        }

        private GraphEdge FindConnectingEdge(GraphNode a, GraphNode b)
        {
            for (int i = 0; i < a.edges.Count; i++)
                if ((ReferenceEquals(a.edges[i].a, a) && ReferenceEquals(a.edges[i].b, b)) ||
                    (ReferenceEquals(a.edges[i].b, a) && ReferenceEquals(a.edges[i].a, b)))
                    return a.edges[i];
            return null;
        }

        private static void AddLeg(List<Vector3> points, List<string> roadIds, Vector3 point, string id)
        {
            if (Vector3.Distance(points[points.Count - 1], point) < .0001f) return;
            points.Add(point);
            roadIds.Add(id);
        }

        private List<CityRoadSegment> SplitRoad(CityBasemapRoad road, int firstOrdinal)
        {
            var result = new List<CityRoadSegment>();
            var current = new List<Vector3>();
            Vector3 first = ToVector(road.points[0]);
            current.Add(first);
            float inChunk = 0f;
            int ordinal = firstOrdinal;
            for (int p = 1; p < road.points.Length; p++)
            {
                Vector3 end = ToVector(road.points[p]);
                Vector3 start = ToVector(road.points[p - 1]);
                float length = Vector3.Distance(start, end);
                if (length <= .000001f) continue;
                float consumed = 0f;
                while (consumed < length - .000001f)
                {
                    float take = Math.Min(length - consumed, CityUnitsPerKilometre - inChunk);
                    consumed += take;
                    Vector3 point = Vector3.Lerp(start, end, consumed / length);
                    if (Vector3.Distance(current[current.Count - 1], point) > .000001f)
                        current.Add(point);
                    inChunk += take;
                    if (inChunk >= CityUnitsPerKilometre - .00001f)
                    {
                        result.Add(CreateSegment(road, ordinal++, current));
                        current = new List<Vector3> { point };
                        inChunk = 0f;
                    }
                }
            }
            if (current.Count > 1 && PolylineLength(current) > .000001f)
                result.Add(CreateSegment(road, ordinal, current));
            return result;
        }

        private static CityRoadSegment CreateSegment(CityBasemapRoad road, int ordinal,
            List<Vector3> points)
        {
            Vector3[] copy = points.ToArray();
            float length = PolylineLength(copy);
            string id = road.FeatureId + ":" + ordinal.ToString(CultureInfo.InvariantCulture);
            var definition = new RoadSegmentDefinition(id,
                string.IsNullOrWhiteSpace(road.name) ? road.kind : road.name, length * MetresPerCityUnit);
            return new CityRoadSegment(definition, copy, road.width, road.kind);
        }

        private void AddGraphEdges(CityBasemapRoad road, CityRoadSegment segment,
            Dictionary<VertexKey, GraphNode> authoredNodes,
            Dictionary<VertexKey, GraphNode> wayLocalNodes)
        {
            for (int i = 1; i < segment.Points.Length; i++)
            {
                Vector3 a = segment.Points[i - 1], b = segment.Points[i];
                if (Vector3.Distance(a, b) < .00001f) continue;
                bool aAuthored = FindAuthoredVertex(road.points, a, out _);
                bool bAuthored = FindAuthoredVertex(road.points, b, out _);
                GraphNode start = GetNode(a, aAuthored, authoredNodes, wayLocalNodes);
                GraphNode end = GetNode(b, bAuthored, authoredNodes, wayLocalNodes);
                var edge = new GraphEdge { a = start, b = end,
                    length = Vector3.Distance(a, b), roadId = segment.Definition.id,
                    baseSpeed = SpeedForKind(segment.Kind) };
                start.edges.Add(edge);
                end.edges.Add(edge);
                graphEdges.Add(edge);
            }
        }

        private static bool FindAuthoredVertex(CityBasemapPoint[] points, Vector3 value, out int index)
        {
            var key = new VertexKey(value);
            for (int i = 0; i < points.Length; i++)
                if (key.Equals(new VertexKey(ToVector(points[i]))))
                { index = i; return true; }
            index = -1;
            return false;
        }

        private static GraphNode GetNode(Vector3 point, bool authored,
            Dictionary<VertexKey, GraphNode> authoredNodes,
            Dictionary<VertexKey, GraphNode> wayLocalNodes)
        {
            var key = new VertexKey(point);
            if (authored)
            {
                GraphNode node;
                if (!authoredNodes.TryGetValue(key, out node))
                {
                    node = new GraphNode(point);
                    authoredNodes.Add(key, node);
                }
                return node;
            }
            // Interpolated points are shared only inside their own way: geometric road crossings
            // never create connections without matching authored vertices.
            GraphNode localNode;
            if (!wayLocalNodes.TryGetValue(key, out localNode))
            {
                localNode = new GraphNode(point);
                wayLocalNodes.Add(key, localNode);
            }
            return localNode;
        }

        private static float SpeedForKind(string kind)
        {
            string value = (kind ?? string.Empty).ToLowerInvariant();
            if (value == "motorway" || value == "trunk" || value == "primary") return 1.45f;
            if (value == "secondary" || value == "tertiary") return 1.2f;
            if (value == "residential" || value == "unclassified" || value == "living_street") return 1f;
            return .8f;
        }

        public static bool IsDrivable(string kind)
        {
            string value = (kind ?? string.Empty).ToLowerInvariant();
            return value != "footway" && value != "path" && value != "steps" &&
                value != "pedestrian" && value != "bridleway" && value != "cycleway" &&
                value != "corridor" && value != "platform";
        }

        private void AddPickSegment(CityRoadSegment segment)
        {
            for (int i = 1; i < segment.Points.Length; i++)
            {
                Vector3 a = segment.Points[i - 1], b = segment.Points[i];
                float halfWidth = Mathf.Max(.12f, segment.Width) * .5f;
                int minX = CellCoordinate(Math.Min(a.x, b.x) - halfWidth);
                int maxX = CellCoordinate(Math.Max(a.x, b.x) + halfWidth);
                int minZ = CellCoordinate(Math.Min(a.z, b.z) - halfWidth);
                int maxZ = CellCoordinate(Math.Max(a.z, b.z) + halfWidth);
                for (int x = minX; x <= maxX; x++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        var cell = new Cell(x, z);
                        List<CityRoadSegment> bucket;
                        if (!pickGrid.TryGetValue(cell, out bucket))
                        { bucket = new List<CityRoadSegment>(); pickGrid.Add(cell, bucket); }
                        if (bucket.Count == 0 || !ReferenceEquals(bucket[bucket.Count - 1], segment))
                            bucket.Add(segment);
                    }
            }
        }

        private static int CellCoordinate(float value) { return (int)Math.Floor(value / PickCellSize); }
        private static Vector3 ToVector(CityBasemapPoint point) { return new Vector3(point.x, 0f, point.z); }
        private static float PolylineLength(IList<Vector3> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
            return length;
        }
        private static float PolylineLength(Vector3[] points) { return PolylineLength((IList<Vector3>)points); }
        private static Vector3 Project(Vector3 point, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float denominator = dx * dx + dz * dz;
            t = denominator < .000001f ? 0f :
                Mathf.Clamp01(((point.x - a.x) * dx + (point.z - a.z) * dz) / denominator);
            return new Vector3(a.x + dx * t, point.y, a.z + dz * t);
        }
    }

    public sealed class CityRoadSegment
    {
        public RoadSegmentDefinition Definition { get; private set; }
        public Vector3[] Points { get; private set; }
        public float Width { get; private set; }
        public string Kind { get; private set; }

        internal CityRoadSegment(RoadSegmentDefinition definition, Vector3[] points, float width, string kind)
        { Definition = definition; Points = points; Width = width; Kind = kind; }

        internal float DistanceSquared(Vector3 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < Points.Length; i++)
            {
                Vector3 a = Points[i - 1], b = Points[i];
                float dx = b.x - a.x, dz = b.z - a.z;
                float denominator = dx * dx + dz * dz;
                float t = denominator < .000001f ? 0f :
                    Mathf.Clamp01(((point.x - a.x) * dx + (point.z - a.z) * dz) / denominator);
                float x = a.x + dx * t - point.x, z = a.z + dz * t - point.z;
                best = Math.Min(best, x * x + z * z);
            }
            return best;
        }
    }

    public sealed class CityRoadRoute
    {
        public Vector3[] Points { get; private set; }
        /// <summary>Road source ID for each consecutive point pair; empty string denotes access leg.</summary>
        public string[] RoadIds { get; private set; }
        public float Length { get; private set; }
        public bool IsReachable { get { return Points != null && Points.Length >= 2; } }

        internal CityRoadRoute(List<Vector3> points, List<string> roadIds)
        {
            Points = points.ToArray();
            RoadIds = roadIds.ToArray();
            for (int i = 1; i < Points.Length; i++) Length += Vector3.Distance(Points[i - 1], Points[i]);
        }

        public Vector3 PositionAtDistance(float distance)
        {
            int edge;
            float t;
            GetEdgeAtDistance(distance, out edge, out t);
            return Vector3.Lerp(Points[edge], Points[edge + 1], t);
        }

        public Vector3 DirectionAtDistance(float distance)
        {
            int edge;
            float t;
            GetEdgeAtDistance(distance, out edge, out t);
            Vector3 direction = Points[edge + 1] - Points[edge];
            return direction.sqrMagnitude > .000001f ? direction.normalized : Vector3.forward;
        }

        public string RoadIdAtDistance(float distance)
        {
            int edge;
            float t;
            GetEdgeAtDistance(distance, out edge, out t);
            return RoadIds[edge];
        }

        public int EdgeAtDistance(float distance)
        {
            int edge;
            float t;
            GetEdgeAtDistance(distance, out edge, out t);
            return edge;
        }

        private void GetEdgeAtDistance(float distance, out int edge, out float t)
        {
            if (Points == null || Points.Length < 2) throw new InvalidOperationException("Route has no travel legs.");
            float remaining = Mathf.Clamp(distance, 0f, Length);
            for (int i = 0; i < Points.Length - 1; i++)
            {
                float leg = Vector3.Distance(Points[i], Points[i + 1]);
                if (remaining <= leg || i == Points.Length - 2)
                {
                    edge = i;
                    t = leg > .000001f ? Mathf.Clamp01(remaining / leg) : 0f;
                    return;
                }
                remaining -= leg;
            }
            edge = Points.Length - 2;
            t = 1f;
        }
    }
}