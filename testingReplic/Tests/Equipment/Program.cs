using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NewGaza;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza.Core
{
    // Refresh needs only the authored district load cap; all vehicle and mesh builders are
    // production sources linked into this executable.
    internal static class GameCatalog
    {
        internal static readonly DistrictDefinition[] Districts =
        {
            new DistrictDefinition { rubbleLoads = 4 }
        };
    }
}

internal static class Program
{
    private const float VehicleScale = .07f;
    private const float MetersPerCityUnit = 20f;
    private const float FixtureGroundY = -.09f;
    private static int assertions;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        IncludeFields = true,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        try
        {
            bool skipExport = args.Contains("--no-export", StringComparer.Ordinal);
            Console.WriteLine("PASS equipment dust: " + EquipmentDustChecks.Run() + " assertions.");
            Console.WriteLine("PASS equipment steering: " + EquipmentTrackSteeringChecks.Run() + " assertions.");
            MotionMetrics motion = CheckMotionContracts();
            using (var geometry = new CityGeometry())
            {
                CheckTrackShoeLoopGeometry(geometry);
                var fleetObject = new GameObject("Captured production CityFleet");
                CityFleet fleet = fleetObject.AddComponent<CityFleet>();
                Material baseMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                baseMaterial.SetColor("_BaseColor", Color.white);
                fleet.Initialize(geometry, baseMaterial, baseMaterial, baseMaterial,
                    baseMaterial, baseMaterial, baseMaterial);

                SetFleetStage(fleet, JobStage.Idle, -1);
                UpdateFleet(fleet, 0f);
                var export = NewExport();
                CapturePose(export, fleet, "baseline-parked", "Idle", 0f, 0f);

                CheckFleetGroundSupportClearance(fleet);
                SetFleetStage(fleet, JobStage.Clearing, 0);
                PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
                    new Vector3(0f, FixtureGroundY, 0f));
                float actualSweep = CaptureActualExcavatorSweep(fleet);
                Console.WriteLine("Measured production excavator mesh sweep=" +
                    actualSweep.ToString("F4", CultureInfo.InvariantCulture) + " city units.");
                motion.ActualProductionExcavatorSweepCityUnits = actualSweep;
                motion.GroundContact = CheckDigGroundContact(fleet);
                motion.ReleaseContact = CheckReleaseBedContact(fleet);
                motion.TrackMotion = CheckTrackedTravelAnimation(fleet);
                motion.TruckRoutes = CheckTruckRouteSpeed(fleet);
                float loadingPhase = FindBucketLoadPhase(.8f);
                PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
                    new Vector3(0f, FixtureGroundY, 0f));
                AdvanceClearingPhase(fleet, loadingPhase);
                float actualLoadingPhase = (float)GetField(fleet, "phase");
                float bucketLoad = EquipmentMotion.Dig(actualLoadingPhase).bucketLoad;
                Check(Math.Abs(bucketLoad - .8f) <= .03f,
                    "The clearing fixture must capture the production bucket at approximately 0.8 fill.");
                CapturePose(export, fleet, "clearing-bucket-load-0.8",
                    "Clearing", actualLoadingPhase, bucketLoad);
                motion.HaulCycle = CheckTruckHaulCycle(fleet, export);
                CheckConfiguredFleetRoadTravel(geometry, baseMaterial);
                CheckFleetWithoutRoads(geometry, baseMaterial);
                CaptureWorkAnimation(export, fleet);

                ValidateCapturedFleet(export);
                export.Motion = motion;
                export.Summary = new CaptureSummary
                {
                    UniqueMeshCount = export.Meshes.Count,
                    UniqueMeshVertices = export.Meshes.Sum(mesh => mesh.Vertices.Length / 3),
                    PoseCount = export.Poses.Count,
                    PeakSingleMachineVertices = export.Poses.SelectMany(pose => pose.Machines)
                        .Max(machine => machine.VertexCount),
                    PeakSingleMachineTriangles = export.Poses.SelectMany(pose => pose.Machines)
                        .Max(machine => machine.TriangleCount)
                };
                string root = Directory.GetParent(FindProjectRoot()).FullName;
                string destination = Path.Combine(root, "exports", "equipment");
                string jsonPath = Path.Combine(destination, "Equipment-Production-Meshes.json");
                if (!skipExport)
                {
                    Directory.CreateDirectory(destination);
                    Directory.CreateDirectory(Path.Combine(destination, "textures"));
                    WriteTextures(export, destination);
                    var exportOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = false };
                    File.WriteAllText(jsonPath, JsonSerializer.Serialize(export, exportOptions),
                        new UTF8Encoding(false));
                }
                Console.WriteLine("PASS production equipment fixture: " + assertions +
                    " assertions; " + export.Meshes.Count + " source/combined meshes; " +
                    export.Meshes.Sum(mesh => mesh.Vertices.Length / 3) +
                    " unique mesh vertices; " + export.Poses.Count + " actual fleet poses.");
                Console.WriteLine(skipExport
                    ? "Export generation skipped by --no-export."
                    : "Exported " + Path.GetRelativePath(root, jsonPath) +
                        " with production CityFleet, CityGeometry, EquipmentGeometry, EquipmentMotion and EquipmentHydraulicLink compiled directly.");
                Console.WriteLine("Unity API is represented by a bounded System.Numerics test double; PNGs are captured from production Texture2D.SetPixels data.");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Production equipment fixture FAILED: " + exception);
            return 1;
        }
    }

    private static void CheckConfiguredFleetRoadTravel(CityGeometry geometry, Material material)
    {
        Resources.RootDirectory = AppContext.BaseDirectory;
        CityBasemap basemap = CityBasemap.LoadFromResources();
        var roads = new CityRoadNetwork(basemap);
        string fixturePath = Path.Combine(AppContext.BaseDirectory,
            "Gaza-City-Production-Placement.json");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        JsonElement depotJson = fixture.RootElement.GetProperty("depotPosition");
        Vector3 depot = new Vector3(depotJson.GetProperty("x").GetSingle(), FixtureGroundY,
            depotJson.GetProperty("z").GetSingle());
        GeoPoint jobPoint = GameGeography.DistrictPoint(0);
        Vector3 job = new Vector3(jobPoint.x, FixtureGroundY, jobPoint.z);

        var state = new GameState
        {
            excavators = 1, trucks = 1, bulldozers = 1,
            jobStage = JobStage.Idle, jobDistrict = -1,
            districts = new[] { new DistrictState() }
        };
        var objectRoot = new GameObject("Configured production road fleet");
        CityFleet fleet = objectRoot.AddComponent<CityFleet>();
        fleet.Initialize(geometry, material, material, material, material, material, material);
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        fleet.ConfigureRoads(roads, id =>
        {
            int level;
            return RoadEconomy.SpeedMultiplier(levels.TryGetValue(id, out level) ? level : 2);
        });
        fleet.Refresh(state, job, depot);
        UpdateFleet(fleet, 0f);

        state.jobStage = JobStage.Clearing;
        state.jobDistrict = 0;
        fleet.Refresh(state, job, depot);
        UpdateFleet(fleet, 0f);
        CityRoadRoute truckRoute = (CityRoadRoute)GetField(fleet, "roadTripRoute");
        CityRoadRoute excavatorRoute = (CityRoadRoute)GetField(fleet, "excavatorRoadRoute");
        CityRoadRoute dozerRoute = (CityRoadRoute)GetField(fleet, "bulldozerRoadRoute");
        Check(truckRoute != null && excavatorRoute != null && dozerRoute != null,
            "configured fleet plans truck, excavator, and dozer on the real street graph");
        Check(HasSourceRoadLeg(truckRoute) && HasSourceRoadLeg(excavatorRoute) &&
            HasSourceRoadLeg(dozerRoute), "all three dispatch routes carry real road-segment provenance");
        Check(!fleet.RouteStatus.Contains("تعذّر"),
            "configured production fleet has reachable street dispatches");

        string currentTruckRoad = null;
        for (int frame = 0; frame < 120 && string.IsNullOrEmpty(currentTruckRoad); frame++)
        {
            UpdateFleet(fleet, .5f);
            currentTruckRoad = truckRoute.RoadIdAtDistance(Math.Max(0f,
                truckRoute.Length - (float)GetField(fleet, "tripDistance")));
        }
        if (!string.IsNullOrEmpty(currentTruckRoad))
        {
            levels[currentTruckRoad] = 0;
            Vector3 before = ((Transform)GetField(fleet, "truck")).localPosition;
            UpdateFleet(fleet, .05f);
            float damagedSpeed = fleet.ActualWorldSpeed;
            Check(Math.Abs(damagedSpeed - CityFleet.HaulingSpeed * .5f) < .02f,
                "truck measures damaged-road speed from its current edge only");
            levels[currentTruckRoad] = 2;
            before = ((Transform)GetField(fleet, "truck")).localPosition;
            UpdateFleet(fleet, .05f);
            float pavedSpeed = fleet.ActualWorldSpeed;
            Check(Math.Abs(pavedSpeed - CityFleet.HaulingSpeed * 2f) < .03f,
                "live current-edge upgrade changes physical speed without route teleport");
            Check(Vector3.Distance(before,
                ((Transform)GetField(fleet, "truck")).localPosition) < .03f,
                "live road speed update advances continuously without a position jump");
        }
        else
        {
            Check(false, "truck reaches a source-road edge after its short depot access leg");
        }

        Transform[] roots =
        {
            (Transform)GetField(fleet, "excavator"),
            (Transform)GetField(fleet, "truck"),
            (Transform)GetField(fleet, "bulldozer")
        };
        Vector3[] previous = { roots[0].localPosition, roots[1].localPosition, roots[2].localPosition };
        bool arrived = false;
        for (int frame = 0; frame < 2200; frame++)
        {
            UpdateFleet(fleet, .5f);
            for (int machine = 0; machine < roots.Length; machine++)
            {
                Vector3 current = roots[machine].localPosition;
                float maximumStep = (machine == 1 ? CityFleet.HaulingSpeed * 2f : .20f) * .5f + .04f;
                Check(Vector3.Distance(previous[machine], current) <= maximumStep,
                    "configured fleet machine advances continuously rather than hiding a target jump");
                CityRoadRoute activeRoute = machine == 1 ? truckRoute :
                    (machine == 0 ? excavatorRoute : dozerRoute);
                Check(activeRoute.Points.All(Finite) &&
                    activeRoute.RoadIds.Length == activeRoute.Points.Length - 1,
                    "configured road/off-road travel retains valid segment provenance");
                previous[machine] = current;
            }
            if ((bool)GetField(fleet, "excavatorDockedAtWork") &&
                (bool)GetField(fleet, "bulldozerDockedAtWork") &&
                GetField(fleet, "truckTripState").ToString() == "ParkedAtWork")
            {
                arrived = true;
                break;
            }
        }
        Check(arrived, "crew and truck physically dock at the real sourced work-site route");
        Check(!fleet.RouteStatus.Contains("تعذّر"),
            "reachable production fleet reports no unreachable-route warning");
        CheckConfiguredReleaseContact(fleet);
    }

    private static void CheckFleetWithoutRoads(CityGeometry geometry, Material material)
    {
        var fleetObject = new GameObject("Direct-arrival fleet fixture");
        CityFleet fleet = fleetObject.AddComponent<CityFleet>();
        fleet.Initialize(geometry, material, material, material, material, material, material);
        var network = new CityRoadNetwork(new CityBasemap { roads = new CityBasemapRoad[0] });
        fleet.ConfigureRoads(network, _ => 1f);
        SetFleetStage(fleet, JobStage.Idle, -1, Vector3.zero, Vector3.zero);
        UpdateFleet(fleet, 0f);
        SetFleetStage(fleet, JobStage.Clearing, 0,
            new Vector3(0f, FixtureGroundY, 3f), new Vector3(0f, FixtureGroundY, 0f));
        Transform[] roots = { (Transform)GetField(fleet, "excavator"),
            (Transform)GetField(fleet, "truck"), (Transform)GetField(fleet, "bulldozer") };
        Vector3[] previous = roots.Select(root => root.localPosition).ToArray();
        bool arrived = false;
        for (int frame = 0; frame < 400; frame++)
        {
            UpdateFleet(fleet, .5f);
            for (int machine = 0; machine < roots.Length; machine++)
            {
                Check(Vector3.Distance(previous[machine], roots[machine].localPosition) < .18f,
                    "No-road fallback must move every owned machine continuously, without teleporting.");
                previous[machine] = roots[machine].localPosition;
            }
            if ((bool)GetField(fleet, "excavatorDockedAtWork") &&
                (bool)GetField(fleet, "bulldozerDockedAtWork") &&
                GetField(fleet, "truckTripState").ToString() == "ParkedAtWork")
            {
                arrived = true;
                break;
            }
        }
        Check(arrived, "All three owned machines must dock at the actual job when no roads exist.");
        Check(!fleet.RouteStatus.Contains("تعذّر"), "No-road fallback must not report permanent blockage.");
        UnityEngine.Object.Destroy(fleetObject);
    }

    private static void CheckConfiguredReleaseContact(CityFleet fleet)
    {
        float releasePhase = EquipmentMotion.DigCycleSeconds * .721f;
        int frames = 0;
        while ((int)GetField(fleet, "transferredCargoPieces") == 0 && frames++ < 1600)
        {
            float phase = (float)GetField(fleet, "phase");
            if (phase >= releasePhase) break;
            UpdateFleet(fleet, Math.Min(.01f, releasePhase - phase));
        }
        Check((int)GetField(fleet, "transferredCargoPieces") > 0,
            "configured crew waits for actual truck dock before loading a release payload");
        List<VertexReference> tips = FindActualBucketToothTipVertices((Transform)GetField(fleet, "bucket"));
        Transform bed = (Transform)GetField(fleet, "truckBed");
        List<Vector3> points = ActualTipPointsInBed(tips, bed);
        BedInterior interior = MeasureActualTruckBedInterior(bed);
        bool clear = points.Count == tips.Count;
        foreach (Vector3 point in points)
            clear &= point.x > interior.InnerXMin + .02f && point.x < interior.InnerXMax - .02f &&
                point.z > interior.InnerZMin + .02f && point.z < interior.InnerZMax - .02f &&
                point.y > interior.FloorTop + .02f && point.y > interior.WallTop + .02f;
        Check(clear,
            "configured production truck stays at the authored release dock with every bucket tooth clear of its actual bed");
    }

    private static bool HasSourceRoadLeg(CityRoadRoute route)
    {
        if (route == null) return false;
        foreach (string roadId in route.RoadIds)
            if (!string.IsNullOrEmpty(roadId)) return true;
        return false;
    }

    private static float NearestSourceRoadDistance(CityRoadNetwork roads, Vector3 point)
    {
        float best = float.MaxValue;
        foreach (CityRoadSegment segment in roads.Segments)
        {
            string kind = segment.Kind.ToLowerInvariant();
            if (kind == "footway" || kind == "path" || kind == "steps" ||
                kind == "pedestrian" || kind == "bridleway" || kind == "cycleway" ||
                kind == "corridor" || kind == "platform") continue;
            for (int i = 1; i < segment.Points.Length; i++)
            {
                Vector3 a = segment.Points[i - 1], b = segment.Points[i];
                Vector3 edge = b - a;
                float denominator = edge.x * edge.x + edge.z * edge.z;
                float t = denominator < .000001f ? 0f :
                    Mathf.Clamp01(((point.x - a.x) * edge.x + (point.z - a.z) * edge.z) /
                        denominator);
                Vector3 nearest = a + edge * t;
                best = Mathf.Min(best, Vector3.Distance(point, nearest));
            }
        }
        return best;
    }

    private static ExportFile NewExport()
    {
        return new ExportFile
        {
            SchemaVersion = 1,
            Source = new[]
            {
                "Assets/NewGaza/World/CityFleet.cs",
                "Assets/NewGaza/World/CityGeometry.cs",
                "Assets/NewGaza/World/EquipmentGeometry.cs",
                "Assets/NewGaza/World/EquipmentMotion.cs",
                "Assets/NewGaza/World/EquipmentHydraulicLink.cs",
                "Assets/NewGaza/World/EquipmentTrackRig.cs"
            },
            CaptureMethod = "Production CityFleet.Initialize/Refresh/Update builders; MeshFilter.sharedMesh, authored transform hierarchy, baked CityMeshBatch meshes and live hydraulic links.",
            CoordinateConvention = "Mesh vertices use production model-space metres; node matrices use System.Numerics row-major TRS. CityFleet model scale is .07 city units/model metre; Gaza city coordinates convert to metres at 20 m per city unit.",
            UnityMathStub = "Unity API test double backed by System.Numerics float vectors/quaternions/matrices. Texture pixel arrays preserve production Texture2D.SetPixels input at native authored 64x64 resolution; PerlinNoise uses a deterministic bounded gradient-noise substitute.",
            Meshes = new List<MeshRecord>(),
            Materials = new List<MaterialRecord>(),
            Poses = new List<PoseRecord>(),
            Summary = null,
            Motion = null
        };
    }

    private static MotionMetrics CheckMotionContracts()
    {
        for (int i = 0; i <= 1400; i++)
        {
            float seconds = EquipmentMotion.DigCycleSeconds * i / 1400f;
            EquipmentMotion.ExcavationPose pose = EquipmentMotion.Dig(seconds);
            Check(Finite(pose.turretYaw) && Finite(pose.boomAngle) &&
                Finite(pose.stickAngle) && Finite(pose.bucketAngle) &&
                Math.Abs(pose.turretYaw) <= 180f &&
                Math.Abs(pose.boomAngle) <= 180f &&
                Math.Abs(pose.stickAngle) <= 180f &&
                Math.Abs(pose.bucketAngle) <= 180f &&
                Unit(pose.bucketLoad) && Unit(pose.hydraulicEffort),
                "The production dig cycle must stay inside its authored joint ranges with normalized load/effort.");
            Check(Unit(EquipmentMotion.Unload(seconds)) &&
                Unit(EquipmentMotion.DumpedAmount(seconds)) &&
                EquipmentMotion.VisibleCargoPieces(1f - EquipmentMotion.DumpedAmount(seconds), 9) >= 0 &&
                EquipmentMotion.VisibleCargoPieces(1f - EquipmentMotion.DumpedAmount(seconds), 9) <= 9,
                "Production dump/cargo articulation must stay finite and within its physical cargo count.");
            Check(Finite(EquipmentMotion.PushBlade(seconds)),
                "Production dozer blade stroke must stay finite.");
        }
        float digEnvelope = EquipmentMotion.MaximumDigEnvelope(Vector3.zero) * VehicleScale;
        float normalEnvelope = EquipmentMotion.MaximumFleetWorkEnvelope(Vector3.zero) * VehicleScale;
        float depotEnvelope = EquipmentMotion.MaximumFleetWorkEnvelope(
            new Vector3(.3f, 0f, 5.6f)) * VehicleScale;
        Console.WriteLine("Fixture analytic fleet envelopes: normal=" +
            normalEnvelope.ToString("F4", CultureInfo.InvariantCulture) + " city units (" +
            (normalEnvelope * MetersPerCityUnit).ToString("F2", CultureInfo.InvariantCulture) +
            " m), depot=" + depotEnvelope.ToString("F4", CultureInfo.InvariantCulture) +
            " city units (" + (depotEnvelope * MetersPerCityUnit).ToString("F2", CultureInfo.InvariantCulture) +
            " m).");
        Check(Finite(digEnvelope) && digEnvelope > 0f && digEnvelope <= .30f,
            "Production articulated dig sweep must remain inside the 0.30 city-unit local-work envelope.");
        Check(Finite(normalEnvelope) && normalEnvelope > 0f && normalEnvelope <= .30f,
            "The full three-machine normal-work envelope must stay within the 0.30 city-unit circle; got " +
            normalEnvelope.ToString("F4", CultureInfo.InvariantCulture) + " city units.");
        Check(Finite(depotEnvelope) && depotEnvelope > 0f && depotEnvelope <= 1.5f,
            "Production imported/depot work sweep must stay within the 1.5 city-unit depot envelope; got " +
            depotEnvelope.ToString("F4", CultureInfo.InvariantCulture) + " city units.");
        Check(Math.Abs(normalEnvelope - .273f) <= .015f,
            "Production normal-job fleet envelope should remain near its authored 0.273 city-unit maximum; got " +
            normalEnvelope.ToString("F4", CultureInfo.InvariantCulture) + " city units.");
        Check(Math.Abs(depotEnvelope - .59f) <= .08f,
            "Production imported/depot work sweep should remain near its authored 0.59 city-unit maximum; got " +
            depotEnvelope.ToString("F4", CultureInfo.InvariantCulture) + " city units.");
        return new MotionMetrics
        {
            DigSweepCityUnits = digEnvelope,
            NormalFleetEnvelopeCityUnits = normalEnvelope,
            DepotFleetEnvelopeCityUnits = depotEnvelope,
            DigSweepMeters = digEnvelope * MetersPerCityUnit,
            NormalFleetEnvelopeMeters = normalEnvelope * MetersPerCityUnit,
            DepotFleetEnvelopeMeters = depotEnvelope * MetersPerCityUnit,
            CityUnitsToMeters = MetersPerCityUnit,
            NormalLimitCityUnits = .30f,
            DepotLimitCityUnits = 1.5f
        };
    }

    private static float FindBucketLoadPhase(float desired)
    {
        float bestPhase = 0f, bestError = float.PositiveInfinity;
        const int samples = 10000;
        for (int i = 0; i < samples; i++)
        {
            float phase = EquipmentMotion.DigCycleSeconds * i / samples;
            float error = Math.Abs(EquipmentMotion.Dig(phase).bucketLoad - desired);
            if (error < bestError) { bestError = error; bestPhase = phase; }
        }
        return bestPhase;
    }

    private static float AdvanceClearingPhase(CityFleet fleet, float targetPhase)
    {
        float phase = (float)GetField(fleet, "phase");
        int frames = 0;
        while (phase < targetPhase - .00001f && frames++ < 2000)
        {
            float deltaTime = Math.Min(.01f, targetPhase - phase);
            UpdateFleet(fleet, deltaTime);
            phase = (float)GetField(fleet, "phase");
        }
        Check(frames < 2000 && Math.Abs(phase - targetPhase) < .001f,
            "The active docked excavation state must reach the requested phase by stepping production Update with Time.deltaTime.");
        return phase;
    }

    private static float CaptureActualExcavatorSweep(CityFleet fleet)
    {
        PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
            new Vector3(0f, FixtureGroundY, 0f));
        Transform excavator = (Transform)GetField(fleet, "excavator");
        MeshFilter[] filters = excavator.gameObject.GetComponentsInChildren<MeshFilter>(true);
        float maximum = 0f;
        const int samples = 1400;
        float sampleDelta = EquipmentMotion.DigCycleSeconds * .70f / samples;
        for (int sample = 0; sample <= samples; sample++)
        {
            UpdateFleet(fleet, sample == 0 ? 0f : sampleDelta);
            float actualPhase = (float)GetField(fleet, "phase");
            float expectedPhase = EquipmentMotion.DigCycleSeconds * .70f * sample / samples;
            Check(Math.Abs(actualPhase - expectedPhase) < .002f,
                "Actual excavator sweep samples must advance the active production dig clock through Time.deltaTime.");
            for (int f = 0; f < filters.Length; f++)
            {
                Mesh mesh = filters[f].sharedMesh;
                for (int v = 0; v < mesh.vertices.Length; v++)
                {
                    Vector3 model = excavator.InverseTransformPoint(
                        filters[f].transform.TransformPoint(mesh.vertices[v]));
                    float reach = (float)Math.Sqrt(model.x * model.x + model.z * model.z) * VehicleScale;
                    maximum = Math.Max(maximum, reach);
                }
            }
        }
        Check(Finite(maximum) && maximum <= .30f,
            "Actual production mesh sweep must remain inside the 0.30 city-unit normal-work circle; got " +
            maximum.ToString("F4", CultureInfo.InvariantCulture) + " city units.");
        return maximum;
    }

    private static GroundContactMetrics CheckDigGroundContact(CityFleet fleet)
    {
        PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
            new Vector3(0f, FixtureGroundY, 0f));
        Transform bucket = (Transform)GetField(fleet, "bucket");
        List<VertexReference> tips = FindActualBucketToothTipVertices(bucket);
        Check(tips.Count >= 4,
            "Actual five-tooth production cutting edge must expose transformed render-mesh vertices.");
        float groundY = (fleet.transform.parent == null
            ? fleet.transform.position.y : fleet.transform.parent.position.y) - .09f;
        const int frames = 64;
        float minimumDigClearance = float.PositiveInfinity;
        float minimumLiftedClearance = float.PositiveInfinity;
        float contactDistance = float.PositiveInfinity;
        const float sampledCycleFraction = .70f;
        float sampleDelta = EquipmentMotion.DigCycleSeconds * sampledCycleFraction / (frames - 1);
        for (int frame = 0; frame < frames; frame++)
        {
            UpdateFleet(fleet, frame == 0 ? 0f : sampleDelta);
            float phase = (float)GetField(fleet, "phase");
            float expectedPhase = EquipmentMotion.DigCycleSeconds * sampledCycleFraction *
                frame / (frames - 1);
            Check(Math.Abs(phase - expectedPhase) < .002f,
                "The 64 ground-contact poses must come from live fleet updates and active Unity delta time.");
            float minimumFrameClearance = float.PositiveInfinity;
            float closestFrameClearance = float.PositiveInfinity;
            for (int i = 0; i < tips.Count; i++)
            {
                Vector3 point = ActualWorldVertex(tips[i]);
                Check(Finite(point), "Actual production tooth-tip vertices must remain finite through the dig cycle.");
                float clearance = (point.y - groundY) / VehicleScale;
                minimumFrameClearance = Math.Min(minimumFrameClearance, clearance);
                closestFrameClearance = Math.Min(closestFrameClearance, Math.Abs(clearance));
            }

            float cycle = phase / EquipmentMotion.DigCycleSeconds;
            if (cycle >= .20f && cycle <= .31f)
            {
                minimumDigClearance = Math.Min(minimumDigClearance, minimumFrameClearance);
                contactDistance = Math.Min(contactDistance, closestFrameClearance);
                Check(minimumFrameClearance >= -.02f,
                    "Actual transformed cutting-edge mesh may enter the intentional dig arc by at most 0.02 model metres.");
            }
            else if (cycle > .31f)
            {
                minimumLiftedClearance = Math.Min(minimumLiftedClearance, minimumFrameClearance);
                Check(minimumFrameClearance >= -.001f,
                    "Actual production teeth must remain above ground after lift through release and return; cycle=" +
                    cycle.ToString("F4", CultureInfo.InvariantCulture) + ", clearance=" +
                    minimumFrameClearance.ToString("F4", CultureInfo.InvariantCulture) + " model m.");
            }

        }
        Check(contactDistance <= .05f,
            "At the intended contact pose, an actual tooth-leading-edge vertex must meet ground within 0.05 model metres; got " +
            contactDistance.ToString("F4", CultureInfo.InvariantCulture) + ".");
        Check(minimumDigClearance >= -.02f && minimumLiftedClearance >= -.001f,
            "The sampled production tooth mesh must preserve dig-arc clearance and post-lift ground clearance.");
        Console.WriteLine("Actual tooth-vertex ground contact=" +
            contactDistance.ToString("F4", CultureInfo.InvariantCulture) +
            " model m; dig/lift minimum clearances=" +
            minimumDigClearance.ToString("F4", CultureInfo.InvariantCulture) + "/" +
            minimumLiftedClearance.ToString("F4", CultureInfo.InvariantCulture) + " model m.");
        return new GroundContactMetrics
        {
            ContactDistanceModelMeters = contactDistance,
            MinimumDigArcClearanceModelMeters = minimumDigClearance,
            MinimumLiftedClearanceModelMeters = minimumLiftedClearance,
            SampledFrames = frames
        };
    }

    private static List<VertexReference> FindActualBucketToothTipVertices(Transform bucket)
    {
        var candidates = new List<(VertexReference vertex, Vector3 point)>();
        MeshFilter[] filters = bucket.gameObject.GetComponentsInChildren<MeshFilter>(true);
        float maximumLeadingZ = float.NegativeInfinity;
        for (int f = 0; f < filters.Length; f++)
        {
            if (!HasNamedAncestor(filters[f].transform, bucket,
                    "Replaceable beveled cutting teeth / heel cheeks")) continue;
            Mesh mesh = filters[f].sharedMesh;
            if (mesh == null) continue;
            for (int v = 0; v < mesh.vertices.Length; v++)
            {
                Vector3 point = bucket.InverseTransformPoint(
                    filters[f].transform.TransformPoint(mesh.vertices[v]));
                maximumLeadingZ = Math.Max(maximumLeadingZ, point.z);
                candidates.Add((new VertexReference(filters[f], v), point));
            }
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException("Missing actual render mesh for production bucket teeth.");
        float leadingTolerance = .002f;
        const float authoredApexY = -.1964f;
        return candidates.Where(item => item.point.z >= maximumLeadingZ - leadingTolerance &&
                Math.Abs(item.point.y - authoredApexY) <= leadingTolerance)
            .Select(item => item.vertex).ToList();
    }

    private static bool HasNamedAncestor(Transform node, Transform stopAt, string name)
    {
        for (Transform current = node; current != null; current = current.parent)
        {
            if (current.gameObject.name == name) return true;
            if (current == stopAt) break;
        }
        return false;
    }

    private static Vector3 ActualWorldVertex(VertexReference reference)
    {
        return reference.Filter.transform.TransformPoint(
            reference.Filter.sharedMesh.vertices[reference.Index]);
    }

    private static BedContactMetrics CheckReleaseBedContact(CityFleet fleet)
    {
        PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
            new Vector3(0f, FixtureGroundY, 0f));
        int releaseFrames = 0;
        float releasePhase = EquipmentMotion.DigCycleSeconds * .721f;
        while ((int)GetField(fleet, "transferredCargoPieces") == 0 &&
            releaseFrames++ < 1600)
        {
            float currentPhase = (float)GetField(fleet, "phase");
            if (currentPhase >= releasePhase) break;
            UpdateFleet(fleet, Math.Min(.01f, releasePhase - currentPhase));
        }
        Check((int)GetField(fleet, "transferredCargoPieces") == 3,
            "The production dig clock must reach the live docked-truck release before measuring clearance.");
        Transform bucket = (Transform)GetField(fleet, "bucket");
        Transform bed = (Transform)GetField(fleet, "truckBed");
        List<VertexReference> tips = FindActualBucketToothTipVertices(bucket);
        List<Vector3> bedPoints = ActualTipPointsInBed(tips, bed);
        BedInterior interior = MeasureActualTruckBedInterior(bed);
        Check(interior.InnerXMin < interior.InnerXMax &&
            interior.InnerZMin < interior.InnerZMax &&
            interior.WallTop > interior.FloorTop,
            "Truck-bed floor, inner walls, and wall top must be measured from its production mesh vertices.");
        bool everyTipClear = bedPoints.Count == tips.Count;
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < bedPoints.Count; i++)
        {
            Vector3 point = bedPoints[i];
            minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
            minY = Math.Min(minY, point.y); maxY = Math.Max(maxY, point.y);
            minZ = Math.Min(minZ, point.z); maxZ = Math.Max(maxZ, point.z);
            bool insideBed = point.x > interior.InnerXMin + .02f &&
                point.x < interior.InnerXMax - .02f &&
                point.z > interior.InnerZMin + .02f &&
                point.z < interior.InnerZMax - .02f;
            bool aboveFloor = point.y > interior.FloorTop + .02f;
            bool clearWallTop = point.y > interior.WallTop + .02f;
            everyTipClear &= insideBed && aboveFloor && clearWallTop;
        }
        Check(everyTipClear,
            "At actual bucket-release phase, every transformed tooth-leading-edge vertex must sit inside the measured bed interior XZ, above its floor, and clear of its wall tops; tips=" +
            minX.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maxX.ToString("F3", CultureInfo.InvariantCulture) + "/" +
            minY.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maxY.ToString("F3", CultureInfo.InvariantCulture) + "/" +
            minZ.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maxZ.ToString("F3", CultureInfo.InvariantCulture) + ", interior=" +
            interior.InnerXMin.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            interior.InnerXMax.ToString("F3", CultureInfo.InvariantCulture) + "/" +
            interior.FloorTop.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            interior.WallTop.ToString("F3", CultureInfo.InvariantCulture) + "/" +
            interior.InnerZMin.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            interior.InnerZMax.ToString("F3", CultureInfo.InvariantCulture) + ".");
        var bounds = new[]
        {
            minX, maxX, minY, maxY, minZ, maxZ
        };
        Console.WriteLine("Actual release tooth-tip bed-local bounds X/Y/Z: " +
            minX.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maxX.ToString("F3", CultureInfo.InvariantCulture) + " / " +
            minY.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maxY.ToString("F3", CultureInfo.InvariantCulture) + " / " +
            minZ.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maxZ.ToString("F3", CultureInfo.InvariantCulture) + ".");
        return new BedContactMetrics
        {
            FloorTopModelY = interior.FloorTop,
            WallTopModelY = interior.WallTop,
            InnerXMinModel = interior.InnerXMin,
            InnerXMaxModel = interior.InnerXMax,
            InnerZMinModel = interior.InnerZMin,
            InnerZMaxModel = interior.InnerZMax,
            ToothTipBedLocalBounds = bounds,
            ToothTipVertexCount = bedPoints.Count
        };
    }

    private static List<Vector3> ActualTipPointsInBed(List<VertexReference> tips, Transform bed)
    {
        var points = new List<Vector3>(tips.Count);
        for (int i = 0; i < tips.Count; i++)
            points.Add(bed.InverseTransformPoint(ActualWorldVertex(tips[i])));
        return points;
    }

    private static BedInterior MeasureActualTruckBedInterior(Transform bed)
    {
        Transform bedMeshRoot = bed.gameObject.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.gameObject.name ==
                "Reinforced steel subframe / lined tipper box");
        if (bedMeshRoot == null)
            throw new InvalidOperationException("Missing production truck-bed mesh group.");
        var points = new List<Vector3>();
        MeshFilter[] filters = bedMeshRoot.gameObject.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null) continue;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
                points.Add(bed.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
        }
        float floorTop = points.Where(point => Math.Abs(point.x) < .8f &&
                point.z > .20f && point.z < 2.10f && point.y < .20f)
            .Max(point => point.y);
        float innerXMin = points.Where(point => point.x < 0f && point.x > -.8f &&
                Math.Abs(point.z - .9f) < .8f)
            .Max(point => point.x);
        float innerXMax = points.Where(point => point.x > 0f && point.x < .8f &&
                Math.Abs(point.z - .9f) < .8f)
            .Min(point => point.x);
        float innerZMin = points.Where(point => point.z >= 0f && point.z < .2f &&
                Math.Abs(point.x) < .8f)
            .Max(point => point.z);
        float innerZMax = points.Where(point => point.z > 2.1f && point.z < 2.5f &&
                Math.Abs(point.x) < .8f)
            .Min(point => point.z);
        float wallTop = points.Where(point => point.y > floorTop + .08f &&
                ((Math.Abs(point.x) > .52f && point.z > .05f && point.z < 2.25f) ||
                 ((point.z < .16f || point.z > 2.13f) && Math.Abs(point.x) < .7f)))
            .Max(point => point.y);
        return new BedInterior(floorTop, wallTop, innerXMin, innerXMax, innerZMin, innerZMax);
    }

    private static void CheckFleetGroundSupportClearance(CityFleet fleet)
    {
        Vector3 gradeOrigin = new Vector3(0f, FixtureGroundY, 0f);
        SetFleetStage(fleet, JobStage.Idle, -1, gradeOrigin, gradeOrigin);
        for (int frame = 0; frame < 24; frame++) UpdateFleet(fleet, .05f);
        CheckGroundContactPose(fleet, "PARK", gradeOrigin.y);

        Vector3 workCenter = new Vector3(0f, FixtureGroundY, 10f);
        PrepareFleetAtWorkDock(fleet, workCenter, gradeOrigin);
        CheckGroundContactPose(fleet, "NORMALWORK", gradeOrigin.y);

        PrepareFleetAtWorkDock(fleet, workCenter, gradeOrigin, importedJob: true);
        CheckGroundContactPose(fleet, "IMPORTEDWORK", gradeOrigin.y);
    }

    private static void CheckTrackShoeLoopGeometry(CityGeometry geometry)
    {
        const float length = 1.95f;
        const float height = .44f;
        const float width = .34f;
        const int phaseSamples = 128;
        const float tangentNormalTolerance = .00001f;
        var vertices = new List<Vector3>(EquipmentGeometry.TrackShoeCountPerSide * 8);
        Mesh mesh = EquipmentGeometry.TrackShoeLoop(geometry,
            "Fixture track-shoe nondegenerate loop", length, height, width, vertices);
        int[] triangles = mesh.triangles;
        Check(vertices.Count == EquipmentGeometry.TrackShoeCountPerSide * 8 &&
            triangles.Length == EquipmentGeometry.TrackShoeCountPerSide * 36,
            "A production track loop must expose all shoe frames and box triangles.");

        float radius = height * .5f;
        float straightLength = (length * .5f - radius) * 2f;
        float perimeter = straightLength * 2f + Mathf.PI * radius * 2f;
        for (int phase = 0; phase <= phaseSamples; phase++)
        {
            EquipmentGeometry.UpdateTrackShoeLoop(mesh, vertices, length, height, width,
                perimeter * phase / phaseSamples);
            bool everyTriangleHasArea = true;
            for (int shoe = 0; shoe < EquipmentGeometry.TrackShoeCountPerSide; shoe++)
            {
                int firstVertex = shoe * 8;
                Vector3 tangent = vertices[firstVertex + 4] - vertices[firstVertex];
                Vector3 normal = vertices[firstVertex + 3] - vertices[firstVertex];
                float tangentNormalDot = Math.Abs(Vector3.Dot(tangent.normalized, normal.normalized));
                Check(tangent.sqrMagnitude > 1e-8f && normal.sqrMagnitude > 1e-8f &&
                    tangentNormalDot <= tangentNormalTolerance,
                    "Track-shoe tangent and thickness normal must remain nonzero and perpendicular at every phase.");

                double signedVolume = 0d;
                int firstTriangle = shoe * 36;
                int lastTriangle = firstTriangle + 36;
                for (int triangle = firstTriangle; triangle < lastTriangle; triangle += 3)
                {
                    Vector3 a = vertices[triangles[triangle]];
                    Vector3 b = vertices[triangles[triangle + 1]];
                    Vector3 c = vertices[triangles[triangle + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.sqrMagnitude <= 1e-12f) everyTriangleHasArea = false;
                    signedVolume += Vector3.Dot(a, cross) / 6d;
                }
                Check(Math.Abs(signedVolume) > 1e-8d,
                    "Every steel shoe must retain nonzero enclosed volume at every track phase.");
            }
            Check(everyTriangleHasArea,
                "Every production track-shoe triangle must retain nonzero area at every phase.");
        }
        Console.WriteLine("Track-shoe frame/volume contracts passed across " +
            (phaseSamples + 1) + " travel phases.");
    }

    private static void CheckGroundContactPose(CityFleet fleet, string pose, float gradeY)
    {
        const float surfaceTolerance = .005f;
        const float penetrationTolerance = .002f;
        float nativeSurfaceRise = pose == "PARK" ? .003f : .006f;
        float maximumGroundGap = nativeSurfaceRise + surfaceTolerance;
        Transform excavator = (Transform)GetField(fleet, "excavator");
        Transform bulldozer = (Transform)GetField(fleet, "bulldozer");
        Transform truck = (Transform)GetField(fleet, "truck");
        foreach (var machine in new[] { ("excavator", excavator), ("bulldozer", bulldozer) })
        {
            MeshFilter[] shoes = machine.Item2.gameObject.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.gameObject.name.IndexOf("track shoes",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                    filter.gameObject.activeInHierarchy).ToArray();
            Check(shoes.Length >= 2, "Ground-contact check needs both actual " +
                machine.Item1 + " steel-shoe loops, not its bucket/blade geometry.");
            AssertLowestTreadPoint(fleet, machine.Item1 + " track shoes", shoes,
                pose, gradeY, maximumGroundGap, penetrationTolerance);
        }
        Transform[] wheels = (Transform[])GetField(fleet, "wheels");
        MeshFilter[] tires = wheels.SelectMany(wheel =>
                wheel.gameObject.GetComponentsInChildren<MeshFilter>(true))
            .Where(filter => filter.gameObject.activeInHierarchy).ToArray();
        Check(wheels.Length == 6 && tires.Length >= 6,
            "Ground-contact check needs all six actual truck wheel/tire meshes.");
        AssertLowestTreadPoint(fleet, "truck tires", tires,
            pose, gradeY, maximumGroundGap, penetrationTolerance);
        Check(IsVisibleMachine(excavator) && IsVisibleMachine(bulldozer) &&
            IsVisibleMachine(truck),
            pose + " ground-support meshes must be sampled only after production vehicle fade/reveal.");
    }

    private static void AssertLowestTreadPoint(CityFleet fleet, string component,
        MeshFilter[] filters, string pose, float gradeY, float maximumGap, float penetrationTolerance)
    {
        float minimumY = float.PositiveInfinity;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 point = filter.transform.TransformPoint(vertex);
                Check(Finite(point), "Actual " + component + " mesh vertices must remain finite.");
                minimumY = Math.Min(minimumY, point.y);
            }
        }
        float gap = minimumY - gradeY;
        Check(Finite(gap) && gap >= -penetrationTolerance && gap <= maximumGap,
            pose + " actual " + component + " lowest tread/tire vertex must stay between grade-0.002 and native surface+0.005 city units; grade gap=" +
            gap.ToString("F4", CultureInfo.InvariantCulture) + ", allowed=[" +
            (-penetrationTolerance).ToString("F3", CultureInfo.InvariantCulture) + "," +
            maximumGap.ToString("F3", CultureInfo.InvariantCulture) + "] city units. Check root/mesh support height; do not use bucket/blade vertices.");
        Console.WriteLine(pose + " " + component + " lowest mesh vertex is " +
            gap.ToString("F4", CultureInfo.InvariantCulture) + " city units above grade.");
    }

    private static void CheckActualTrackShoeMeshGeometry(MeshFilter filter,
        string machineKind, string pose)
    {
        const int shoesPerLoop = 20;
        const int verticesPerShoe = 8;
        const int indicesPerShoe = 36;
        const float minimumTriangleArea = 1e-9f;
        Mesh mesh = filter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Check(vertices.Length == shoesPerLoop * verticesPerShoe &&
            triangles.Length == shoesPerLoop * indicesPerShoe,
            pose + " actual " + machineKind +
            " track loop must contain 20 indexed 8-vertex closed steel shoes.");
        if (vertices.Length != shoesPerLoop * verticesPerShoe ||
            triangles.Length != shoesPerLoop * indicesPerShoe) return;

        int straightShoes = 0;
        int capsuleShoes = 0;
        float minimumArea = float.PositiveInfinity;
        float minimumVolume = float.PositiveInfinity;
        bool everyTriangleHasArea = true;
        bool everyShoeClosed = true;
        float maximumNormalTangentDot = 0f;
        for (int shoe = 0; shoe < shoesPerLoop; shoe++)
        {
            int vertexBase = shoe * verticesPerShoe;
            int triangleBase = shoe * indicesPerShoe;
            Vector3 normalAxis = (vertices[vertexBase + 3] - vertices[vertexBase]).normalized;
            Vector3 tangentAxis = (vertices[vertexBase + 4] - vertices[vertexBase]).normalized;
            float normalTangentDot = Math.Abs(Vector3.Dot(normalAxis, tangentAxis));
            maximumNormalTangentDot = Math.Max(maximumNormalTangentDot, normalTangentDot);
            Check(normalAxis.sqrMagnitude > .99f && tangentAxis.sqrMagnitude > .99f &&
                normalTangentDot < .0001f,
                pose + " actual " + machineKind + " shoe " +
                shoe.ToString(CultureInfo.InvariantCulture) +
                " capsule/straight normal and longitudinal tangent must be orthogonal; |dot|=" +
                normalTangentDot.ToString("F6", CultureInfo.InvariantCulture) + ".");
            if (Math.Abs(normalAxis.y) >= .999f && Math.Abs(normalAxis.z) < .05f)
                straightShoes++;
            else capsuleShoes++;

            int[,] edgeUseCount = new int[verticesPerShoe, verticesPerShoe];
            double signedVolume = 0d;
            int shoeFaceCount = 0;
            for (int index = triangleBase; index < triangleBase + indicesPerShoe; index += 3)
            {
                int first = triangles[index];
                int second = triangles[index + 1];
                int third = triangles[index + 2];
                bool validFace = first >= vertexBase && first < vertexBase + verticesPerShoe &&
                    second >= vertexBase && second < vertexBase + verticesPerShoe &&
                    third >= vertexBase && third < vertexBase + verticesPerShoe;
                Check(validFace,
                    pose + " actual " + machineKind + " shoe triangles must reference only their own eight vertices.");
                if (!validFace) { everyTriangleHasArea = false; continue; }

                Vector3 a = vertices[first], b = vertices[second], c = vertices[third];
                float area = Vector3.Cross(b - a, c - a).magnitude * .5f;
                minimumArea = Math.Min(minimumArea, area);
                everyTriangleHasArea &= Finite(area) && area > minimumTriangleArea;
                signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6d;
                AddShoeEdgeUse(edgeUseCount, first - vertexBase, second - vertexBase);
                AddShoeEdgeUse(edgeUseCount, second - vertexBase, third - vertexBase);
                AddShoeEdgeUse(edgeUseCount, third - vertexBase, first - vertexBase);
                shoeFaceCount++;
            }

            int uniqueEdges = 0;
            for (int first = 0; first < verticesPerShoe; first++)
                for (int second = first + 1; second < verticesPerShoe; second++)
                {
                    int useCount = edgeUseCount[first, second];
                    if (useCount == 0) continue;
                    uniqueEdges++;
                    if (useCount != 2) everyShoeClosed = false;
                }
            double volume = Math.Abs(signedVolume);
            minimumVolume = (float)Math.Min(minimumVolume, volume);
            everyShoeClosed &= shoeFaceCount == 12 && uniqueEdges == 18 &&
                Finite((float)volume) && volume > minimumTriangleArea;
        }
        Check(straightShoes >= 2 && capsuleShoes >= 2,
            pose + " actual " + machineKind +
            " mesh sample must include shoes on both straight runs and capsule ends.");
        Check(everyTriangleHasArea,
            pose + " actual " + machineKind +
            " every steel-shoe triangle must have mesh-space area > 1e-9 square metres; minimum=" +
            minimumArea.ToString("G6", CultureInfo.InvariantCulture) + ".");
        Check(everyShoeClosed,
            pose + " actual " + machineKind +
            " every eight-vertex steel shoe must form a closed 12-face/18-edge manifold with nonzero measured volume; minimum=" +
            minimumVolume.ToString("G6", CultureInfo.InvariantCulture) + " cubic metres.");
        Check(maximumNormalTangentDot < .0001f,
            pose + " actual " + machineKind +
            " capsule and straight shoe bases must preserve perpendicular normal/tangent axes; maximum |dot|=" +
            maximumNormalTangentDot.ToString("G6", CultureInfo.InvariantCulture) + ".");
    }

    private static void AddShoeEdgeUse(int[,] edgeUseCount, int first, int second)
    {
        int low = Math.Min(first, second);
        int high = Math.Max(first, second);
        edgeUseCount[low, high]++;
    }

    private static List<TrackMotionMetrics> CheckTrackedTravelAnimation(CityFleet fleet)
    {
        SetFleetStage(fleet, JobStage.Idle, -1, Vector3.zero, Vector3.zero);
        const int settleFrames = 24;
        for (int frame = 0; frame < settleFrames; frame++)
            UpdateFleet(fleet, .05f);
        var records = new List<TrackMotionMetrics>();
        int travelStep = 0;
        foreach (string machineKind in new[] { "excavator", "bulldozer" })
        {
            Transform machine = (Transform)GetField(fleet, machineKind);
            MeshFilter[] shoeFilters = machine.gameObject.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.gameObject.name.IndexOf("track shoes",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                    filter.gameObject.activeInHierarchy).ToArray();
            Transform[] rollers = machine.gameObject.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.gameObject.name.IndexOf("road roller",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                    transform.gameObject.activeInHierarchy).ToArray();
            Check(shoeFilters.Length >= 2 && rollers.Length >= 10 &&
                shoeFilters.All(filter => filter.gameObject.GetComponent<MeshRenderer>() != null),
                "Production " + machineKind + " must expose visible circulating steel-shoe meshes and road rollers.");
            foreach (MeshFilter filter in shoeFilters)
                CheckActualTrackShoeMeshGeometry(filter, machineKind, "stationary capsule/straight");
            Vector3[][] parkedShoes = shoeFilters.Select(filter =>
                (Vector3[])filter.sharedMesh.vertices.Clone()).ToArray();
            Quaternion[] parkedRollers = rollers.Select(transform => transform.localRotation).ToArray();
            Vector3 parkedPosition = machine.localPosition;
            UpdateFleet(fleet, 1f);
            Check(Vector3.Distance(parkedPosition, machine.localPosition) < .0001f,
                "Stationary " + machineKind + " crawler must remain at its parked transform.");
            for (int i = 0; i < shoeFilters.Length; i++)
                Check(SameVertices(parkedShoes[i], shoeFilters[i].sharedMesh.vertices),
                    "Stationary " + machineKind + " steel track shoes must not creep.");
            for (int i = 0; i < rollers.Length; i++)
                Check(Quaternion.Angle(parkedRollers[i], rollers[i].localRotation) < .1f,
                    "Stationary " + machineKind + " rollers must not creep.");

            Vector3 depot = new Vector3(0f, 0f, 1f + travelStep++);
            SetFleetStage(fleet, JobStage.Idle, -1, Vector3.zero, depot);
            UpdateFleet(fleet, 1f);
            Check(Vector3.Distance(parkedPosition, machine.localPosition) > .5f,
                "The production crawler must undergo measurable fleet travel before its tracks animate.");
            bool shoeRotated = shoeFilters.Where((filter, index) =>
                !SameVertices(parkedShoes[index], filter.sharedMesh.vertices)).Any();
            bool rollerRotated = rollers.Where((roller, index) =>
                Quaternion.Angle(parkedRollers[index], roller.localRotation) > .5f).Any();
            Check(shoeRotated && rollerRotated,
                "Production " + machineKind + " visible steel shoes and rollers must circulate with actual fleet travel.");
            foreach (MeshFilter filter in shoeFilters)
                CheckActualTrackShoeMeshGeometry(filter, machineKind, "travelled capsule/straight");
            Vector3[][] travelledShoes = shoeFilters.Select(filter =>
                (Vector3[])filter.sharedMesh.vertices.Clone()).ToArray();
            Quaternion[] travelledRollers = rollers.Select(transform => transform.localRotation).ToArray();
            Vector3 travelledPosition = machine.localPosition;
            UpdateFleet(fleet, 1f);
            Check(Vector3.Distance(travelledPosition, machine.localPosition) < .0001f,
                "Stationary " + machineKind + " crawler must hold its travel pose.");
            for (int i = 0; i < shoeFilters.Length; i++)
                Check(SameVertices(travelledShoes[i], shoeFilters[i].sharedMesh.vertices),
                    "Stationary " + machineKind + " steel track shoes must hold their travel pose.");
            for (int i = 0; i < rollers.Length; i++)
                Check(Quaternion.Angle(travelledRollers[i], rollers[i].localRotation) < .1f,
                    "Stationary " + machineKind + " rollers must hold their travel pose.");
            records.Add(new TrackMotionMetrics
            {
                Machine = machineKind,
                TravelCityUnits = Vector3.Distance(parkedPosition, machine.localPosition),
                MaximumShoeVertexMotionModelMeters = MaximumVertexDistance(parkedShoes,
                    shoeFilters.Select(filter => filter.sharedMesh.vertices).ToArray()),
                MaximumRollerRotationDegrees = rollers.Select((roller, index) =>
                    Quaternion.Angle(parkedRollers[index], roller.localRotation)).Max(),
                StationaryShoesUnchanged = shoeFilters.Select((filter, index) =>
                    SameVertices(travelledShoes[index], filter.sharedMesh.vertices)).All(unchanged => unchanged),
                StationaryRollersUnchanged = rollers.Select((roller, index) =>
                    Quaternion.Angle(travelledRollers[index], roller.localRotation) < .1f).All(unchanged => unchanged)
            });
        }
        PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
            new Vector3(0f, FixtureGroundY, 0f));
        return records;
    }

    private static float MaximumVertexDistance(Vector3[][] before, Vector3[][] after)
    {
        float maximum = 0f;
        int meshCount = Math.Min(before.Length, after.Length);
        for (int mesh = 0; mesh < meshCount; mesh++)
        {
            int vertexCount = Math.Min(before[mesh].Length, after[mesh].Length);
            for (int vertex = 0; vertex < vertexCount; vertex++)
                maximum = Math.Max(maximum, Vector3.Distance(before[mesh][vertex],
                    after[mesh][vertex]));
        }
        return maximum;
    }

    private static List<TruckRouteMetrics> CheckTruckRouteSpeed(CityFleet fleet)
    {
        var records = new List<TruckRouteMetrics>();
        Transform truck = (Transform)GetField(fleet, "truck");
        const float sampleDelta = .05f;
        foreach (float dispatchDistance in new[] { 10f, 100f })
        {
            Vector3 workCenter = new Vector3(0f, FixtureGroundY, dispatchDistance);
            Vector3 depot = new Vector3(0f, FixtureGroundY, 0f);
            PrepareFleetAtWorkDock(fleet, workCenter, depot);
            SetFleetStage(fleet, JobStage.Hauling, 0, workCenter, depot);

            Vector3 previousPosition = Vector3.zero;
            bool previousWasVisible = false;
            float maximumVisibleSpeed = 0f;
            float observedTravel = 0f;
            float minimumHeadingAlignment = 1f;
            const int frames = 120;
            for (int frame = 0; frame < frames; frame++)
            {
                UpdateFleet(fleet, sampleDelta);
                bool visible = IsVisibleMachine(truck);
                if (!visible)
                {
                    previousWasVisible = false;
                    continue;
                }
                Vector3 currentPosition = truck.position;
                if (previousWasVisible)
                {
                    Vector3 displacement = currentPosition - previousPosition;
                    float travel = displacement.magnitude;
                    observedTravel += travel;
                    float speed = travel / sampleDelta;
                    maximumVisibleSpeed = Math.Max(maximumVisibleSpeed, speed);
                    if (travel > .001f)
                    {
                        Vector3 direction = displacement.normalized;
                        Vector3 forward = truck.rotation * Vector3.forward;
                        minimumHeadingAlignment = Math.Min(minimumHeadingAlignment,
                            Vector3.Dot(direction, forward));
                    }
                }
                previousPosition = currentPosition;
                previousWasVisible = true;
            }
            Check(observedTravel > .05f && maximumVisibleSpeed <= .35f,
                "Truck travel over a " + dispatchDistance.ToString("F0", CultureInfo.InvariantCulture) +
                " city-unit dispatch must be measured from unscaled world positions and stay below 0.35 city units/second; observed max=" +
                maximumVisibleSpeed.ToString("F3", CultureInfo.InvariantCulture) + ".");
            Check(minimumHeadingAlignment >= -.05f,
                "Truck heading must not point backwards relative to its actual route displacement.");
            records.Add(new TruckRouteMetrics
            {
                DispatchDistanceCityUnits = dispatchDistance,
                MaximumVisibleSpeedCityUnitsPerSecond = maximumVisibleSpeed,
                ObservedTravelCityUnits = observedTravel,
                MinimumHeadingAlignment = minimumHeadingAlignment,
                SampledSeconds = frames * sampleDelta
            });
        }
        float turnaroundAlignment = CheckTruckTurnaroundHeading(fleet, 10f);
        records[0].FullTurnMinimumHeadingAlignment = turnaroundAlignment;
        Check(turnaroundAlignment >= -.05f,
            "Across the complete truck route turnaround, its heading must remain longitudinally consistent with actual movement.");
        PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
            new Vector3(0f, FixtureGroundY, 0f));
        return records;
    }

    private static float CheckTruckTurnaroundHeading(CityFleet fleet, float dispatchDistance)
    {
        Vector3 workCenter = new Vector3(0f, FixtureGroundY, dispatchDistance);
        Vector3 depot = new Vector3(0f, FixtureGroundY, 0f);
        PrepareFleetAtWorkDock(fleet, workCenter, depot);
        SetFleetStage(fleet, JobStage.Hauling, 0, workCenter, depot);
        Transform truck = (Transform)GetField(fleet, "truck");
        UpdateFleet(fleet, 0f);
        float routeLength = (float)GetField(fleet, "tripRouteLength");
        const float sampleDelta = .05f;
        int frames = Math.Min(5000,
            (int)Math.Ceiling(routeLength / .10f / sampleDelta));
        Vector3 previousPosition = truck.position;
        float minimumAlignment = 1f;
        int movementSamples = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            UpdateFleet(fleet, sampleDelta);
            Vector3 currentPosition = truck.position;
            Vector3 displacement = currentPosition - previousPosition;
            if (displacement.magnitude > .001f)
            {
                movementSamples++;
                minimumAlignment = Math.Min(minimumAlignment,
                    Vector3.Dot(displacement.normalized, truck.rotation * Vector3.forward));
            }
            previousPosition = currentPosition;
        }
        Check(movementSamples > 0,
            "A representative truck round trip must produce measurable route motion.");
        return minimumAlignment;
    }

    private static bool IsVisibleMachine(Transform machine)
    {
        if (!machine.gameObject.activeInHierarchy) return false;
        MeshRenderer[] renderers = machine.gameObject.GetComponentsInChildren<MeshRenderer>(true)
            .Where(renderer => renderer.gameObject.activeInHierarchy && renderer.enabled).ToArray();
        return renderers.Any(renderer => renderer.sharedMaterial == null ||
            renderer.sharedMaterial.GetColor("_BaseColor").a > .05f);
    }

    private static void PrepareFleetAtWorkDock(CityFleet fleet, Vector3 workCenter, Vector3 depot,
        bool importedJob = false)
    {
        int clearedLoads = importedJob ? 4 : 0;
        SetField(fleet, "truckTripState", GetTruckTripState(fleet, "ParkedAtDepot"));
        SetField(fleet, "pendingRouteRebuild", false);
        SetFleetStage(fleet, JobStage.Idle, -1, workCenter, depot, clearedLoads);
        SetFleetStage(fleet, JobStage.Clearing, 0, workCenter, depot, clearedLoads);
        SetField(fleet, "transferredCargoPieces", 0);
        SetField(fleet, "excavatorWaitingForTruck", false);
        UpdateFleet(fleet, 0f);

        Vector3[] route = (Vector3[])GetField(fleet, "tripRoute");
        Transform truck = (Transform)GetField(fleet, "truck");
        float routeLength = (float)GetField(fleet, "tripRouteLength");
        const float setupDelta = .05f;
        int maximumSetupFrames = (int)Math.Ceiling(
            (Vector3.Distance(workCenter, depot) + 8f) /
            CityFleet.HaulingSpeed / setupDelta) + 100;
        int setupFrames = 0;
        while (GetField(fleet, "truckTripState").ToString() != "ParkedAtWork" &&
            setupFrames++ < maximumSetupFrames)
            UpdateFleet(fleet, setupDelta);

        Check(GetField(fleet, "truckTripState").ToString() == "ParkedAtWork",
            "Fixture setup must reach its real work dock through inbound route, fade reveal, and stopped heading turn; state=" +
            GetField(fleet, "truckTripState") + ", frames=" + setupFrames.ToString(CultureInfo.InvariantCulture) +
            "/" + maximumSetupFrames.ToString(CultureInfo.InvariantCulture) + ", route=" +
            routeLength.ToString("F3", CultureInfo.InvariantCulture) + " city units, trip=" +
            ((float)GetField(fleet, "tripDistance")).ToString("F3", CultureInfo.InvariantCulture) +
            ", live route=" + ((float)GetField(fleet, "tripRouteLength"))
                .ToString("F3", CultureInfo.InvariantCulture) +
            ", pending=" + GetField(fleet, "pendingRouteRebuild") + ".");
        Check(Vector3.Distance(truck.localPosition, route[0]) < .02f &&
            Quaternion.Angle(truck.localRotation, Quaternion.identity) < 1f,
            "The live inbound truck must settle at the actual work-bed dock in its stopped digging orientation.");
        Check(IsVisibleMachine((Transform)GetField(fleet, "excavator")),
            "The actual excavator root must finish its local fade/reveal before contact sampling.");
    }

    private static object GetTruckTripState(CityFleet fleet, string name)
    {
        FieldInfo field = typeof(CityFleet).GetField("truckTripState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Production truck route-state field is missing.");
        return Enum.Parse(field.FieldType, name);
    }

    private static HaulCycleMetrics CheckTruckHaulCycle(CityFleet fleet, ExportFile export)
    {
        const float cycleSeconds = EquipmentMotion.DigCycleSeconds;
        const int frames = 64;
        const float sampleDelta = cycleSeconds * .70f / (frames - 1);
        Vector3 workCenter = new Vector3(0f, FixtureGroundY, 10f);
        Vector3 depot = new Vector3(0f, FixtureGroundY, 0f);
        PrepareFleetAtWorkDock(fleet, workCenter, depot);
        Transform bucket = (Transform)GetField(fleet, "bucket");
        Transform bed = (Transform)GetField(fleet, "truckBed");
        Transform truck = (Transform)GetField(fleet, "truck");
        Transform[] joints =
        {
            (Transform)GetField(fleet, "turret"), (Transform)GetField(fleet, "boom"),
            (Transform)GetField(fleet, "stick"), bucket
        };
        Vector3[] tripRoute = (Vector3[])GetField(fleet, "tripRoute");
        int releaseCount = 0;
        Check(Math.Abs((float)GetField(fleet, "phase")) < .001f,
            "The actual work-dock route must begin the 64-pose cycle at production phase zero.");
        for (int frame = 0; frame < frames; frame++)
        {
            if (frame == 0) continue;
            int before = (int)GetField(fleet, "transferredCargoPieces");
            UpdateFleet(fleet, sampleDelta);
            int after = (int)GetField(fleet, "transferredCargoPieces");
            if (after > before)
            {
                releaseCount += after - before;
                CheckReleaseIsDockedAndInsideBed(fleet, bucket, bed, truck,
                    tripRoute[0], frame, "64-pose dig-cycle release");
            }
        }
        int releaseFrames = 0;
        while ((int)GetField(fleet, "transferredCargoPieces") == 0 &&
            releaseFrames++ < 200)
            UpdateFleet(fleet, .005f);
        releaseCount = (int)GetField(fleet, "transferredCargoPieces");
        if (releaseCount > 0)
            CheckReleaseIsDockedAndInsideBed(fleet, bucket, bed, truck,
                tripRoute[0], frames, "Live dig-cycle release");
        Check(releaseCount == 3 && VisibleCargoCount(fleet) == 3,
            "The first .72 dig-cycle release must load the three actual truck cargo fragments exactly once.");

        int departures = 0;
        int depotUnloads = 0;
        bool capturedLoadedPose = false;
        bool capturedDumpPose = false;
        bool capturedEmptyPose = false;
        bool hasDeparted = false;
        bool hasVisitedDepot = false;
        bool returnedHome = false;
        bool wasAwayLastFrame = false;
        int previousVisibleCargo = VisibleCargoCount(fleet);
        Quaternion[] heldJoints = joints.Select(joint => joint.localRotation).ToArray();
        Vector3 workTruckPosition = tripRoute[0];
        Vector3 depotUnloadPosition = tripRoute[1];
        float maximumDispatchSpeed = 0f;
        const float routeDelta = .05f;
        const int maximumRouteFrames = 2400;
        var tipAnimation = new AnimationClipRecord { Name = "TruckTip", FramesPerSecond = 20 };
        int tipStartFrame = -1;
        for (int frame = 0; frame < maximumRouteFrames; frame++)
        {
            Vector3 previousTruck = truck.localPosition;
            int cargoBefore = VisibleCargoCount(fleet);
            int transfersBefore = (int)GetField(fleet, "transferredCargoPieces");
            UpdateFleet(fleet, routeDelta);
            Vector3 currentTruck = truck.localPosition;
            float truckDistance = Vector3.Distance(previousTruck, currentTruck);
            maximumDispatchSpeed = Math.Max(maximumDispatchSpeed, truckDistance / routeDelta);
            float homeDistance = Vector3.Distance(currentTruck, workTruckPosition);
            float depotDistance = Vector3.Distance(currentTruck, depotUnloadPosition);
            bool truckDocked = homeDistance < .15f;
            int transfersAfter = (int)GetField(fleet, "transferredCargoPieces");
            int cargoAfter = VisibleCargoCount(fleet);
            string tripState = GetField(fleet, "truckTripState").ToString();
            if (tripState == "Unloading" && export != null)
            {
                if (tipStartFrame < 0) tipStartFrame = frame;
                AppendAnimationFrame(export, tipAnimation, fleet, (frame - tipStartFrame) * routeDelta);
            }

            if (!hasDeparted && !truckDocked)
            {
                hasDeparted = true;
                departures++;
            }
            else if (hasDeparted && truckDocked && hasVisitedDepot &&
                GetField(fleet, "truckTripState").ToString() == "ParkedAtWork")
            {
                returnedHome = true;
            }
            if (transfersAfter > transfersBefore)
            {
                releaseCount += transfersAfter - transfersBefore;
                CheckReleaseIsDockedAndInsideBed(fleet, bucket, bed, truck,
                    tripRoute[0], frame, "returning haul-cycle release");
            }
            if (!capturedLoadedPose && tripState == "Outbound" &&
                (float)GetField(fleet, "tripDistance") >=
                    CityFleet.HaulingSpeed * 5f - .001f)
            {
                Check(cargoAfter == 3,
                    "The actual outbound five-second truck pose must still carry all three production cargo fragments.");
                CapturePose(export, fleet, "haul-loaded-5s",
                    "Hauling", (float)GetField(fleet, "phase"), 0f);
                capturedLoadedPose = true;
            }
            if (!capturedDumpPose && tripState == "Unloading" &&
                (float)GetField(fleet, "tripClock") >= 3.6f)
            {
                Check(Quaternion.Angle(Quaternion.identity, bed.localRotation) > 30f &&
                    cargoAfter == 2,
                    "The actual 3.6-second depot-unload pose must raise the bed and show two remaining cargo fragments.");
                CapturePose(export, fleet, "tipper-full-dump",
                    "Recycling", (float)GetField(fleet, "tripClock"), 0f);
                capturedDumpPose = true;
            }
            if (cargoBefore > 0 && cargoAfter == 0)
            {
                Check(depotDistance < .8f,
                    "The actual truck cargo mesh must unload only when the route reaches its measured depot.");
                if (depotDistance < .8f)
                {
                    depotUnloads++;
                    hasVisitedDepot = true;
                    Check(tripState == "Unloading",
                        "Cargo must become empty at the production unloading depot rather than en route.");
                }
            }
            if (hasVisitedDepot && !capturedEmptyPose && tripState == "Inbound")
            {
                Check(cargoAfter == 0 && Quaternion.Angle(Quaternion.identity, bed.localRotation) < 1f,
                    "The empty-return pose must follow completed depot unloading with a level production truck bed.");
                CapturePose(export, fleet, "tipper-returned-empty",
                    "Recycling", 7.2f, 0f);
                capturedEmptyPose = true;
            }

            if (hasDeparted && !truckDocked)
            {
                if (wasAwayLastFrame)
                {
                    for (int joint = 0; joint < joints.Length; joint++)
                        Check(Quaternion.Angle(heldJoints[joint], joints[joint].localRotation) < .1f,
                            "The excavator arm must hold its real joint pose while its only truck is away from the bed.");
                    fleet.TryGetMachineAudioState(0, out _, out _, out float movement,
                        out float hydraulics);
                    Check(movement <= .01f && hydraulics <= .01f,
                        "Excavator movement/hydraulic arm audio activity must be silent throughout the truck-away hold.");
                }
                else
                {
                    for (int joint = 0; joint < joints.Length; joint++)
                        heldJoints[joint] = joints[joint].localRotation;
                }
                wasAwayLastFrame = true;
            }
            else
            {
                wasAwayLastFrame = false;
                for (int joint = 0; joint < joints.Length; joint++)
                    heldJoints[joint] = joints[joint].localRotation;
            }
            if (returnedHome) break;
        }
        Check(departures == 1 && depotUnloads == 1 && returnedHome,
            "The first load must dispatch once, unload once at the depot, and return the actual truck to its work-bed dock.");
        Check(capturedLoadedPose && capturedDumpPose && capturedEmptyPose,
            "Loaded-haul, depot-tip, and empty-return export poses must be sampled from the live route state machine.");
        if (export != null)
        {
            Check(tipAnimation.Frames.Count > 20,
                "Editable truck-tip animation must contain real production unloading samples.");
            export.AnimationClips.Add(tipAnimation);
        }
        Check(releaseCount == 3,
            "A second actual bucket release must wait until the truck has returned and docked.");
        Check(returnedHome && Math.Abs((float)GetField(fleet, "phase")) < .001f,
            "The excavation clock must reset only when the real truck has returned to its dock.");
        Check(maximumDispatchSpeed <= .35f,
            "The complete load/depot/return truck cycle must remain under 0.35 city units per second.");

        PrepareFleetAtWorkDock(fleet, workCenter, depot);
        return new HaulCycleMetrics
        {
            CyclePoseCount = frames,
            ReleaseFragmentCount = releaseCount,
            DispatchCount = departures,
            DepotUnloadCount = depotUnloads,
            ReturnedToWorkDock = returnedHome,
            MaximumRouteSpeedCityUnitsPerSecond = maximumDispatchSpeed
        };
    }

    private static void CheckReleaseIsDockedAndInsideBed(CityFleet fleet, Transform bucket,
        Transform bed, Transform truck, Vector3 expectedDockPosition, int frame, string label)
    {
        float homeDistance = Vector3.Distance(truck.localPosition, expectedDockPosition);
        BedInterior interior = MeasureActualTruckBedInterior(bed);
        List<VertexReference> tips = FindActualBucketToothTipVertices(bucket);
        bool fitsBed = tips.Count >= 4 && homeDistance < .15f;
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < tips.Count; i++)
        {
            Vector3 point = bed.InverseTransformPoint(ActualWorldVertex(tips[i]));
            minimum = new Vector3(Math.Min(minimum.x, point.x), Math.Min(minimum.y, point.y),
                Math.Min(minimum.z, point.z));
            maximum = new Vector3(Math.Max(maximum.x, point.x), Math.Max(maximum.y, point.y),
                Math.Max(maximum.z, point.z));
            fitsBed &= point.x > interior.InnerXMin + .02f &&
                point.x < interior.InnerXMax - .02f &&
                point.z > interior.InnerZMin + .02f &&
                point.z < interior.InnerZMax - .02f &&
                point.y > interior.FloorTop + .02f &&
                point.y > interior.WallTop + .02f;
        }
        Check(fitsBed,
            label + " at frame " + frame.ToString(CultureInfo.InvariantCulture) +
            " must release actual tooth vertices only inside the docked truck bed; dock delta=" +
            homeDistance.ToString("F4", CultureInfo.InvariantCulture) + ", range X=" +
            interior.InnerXMin.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            interior.InnerXMax.ToString("F3", CultureInfo.InvariantCulture) + " Y=" +
            interior.FloorTop.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            interior.WallTop.ToString("F3", CultureInfo.InvariantCulture) + " Z=" +
            interior.InnerZMin.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            interior.InnerZMax.ToString("F3", CultureInfo.InvariantCulture) + ", phase=" +
            ((float)GetField(fleet, "phase")).ToString("F3", CultureInfo.InvariantCulture) +
            ", tip bounds=" + minimum.x.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maximum.x.ToString("F3", CultureInfo.InvariantCulture) + "/" +
            minimum.y.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maximum.y.ToString("F3", CultureInfo.InvariantCulture) + "/" +
            minimum.z.ToString("F3", CultureInfo.InvariantCulture) + ".." +
            maximum.z.ToString("F3", CultureInfo.InvariantCulture) + ".");
    }

    private static bool SameVertices(Vector3[] left, Vector3[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (Vector3.Distance(left[i], right[i]) > .00001f) return false;
        return true;
    }

    private static int VisibleCargoCount(CityFleet fleet)
    {
        Transform[] pieces = (Transform[])GetField(fleet, "cargoPieces");
        return pieces.Count(piece => piece.gameObject.activeInHierarchy);
    }

    private static void SetFleetStage(CityFleet fleet, JobStage stage, int district)
    {
        SetFleetStage(fleet, stage, district, new Vector3(10f, 0f, 5f));
    }

    private static void SetFleetStage(CityFleet fleet, JobStage stage, int district,
        Vector3 worldJobCenter)
    {
        SetFleetStage(fleet, stage, district, worldJobCenter, Vector3.zero);
    }

    private static void SetFleetStage(CityFleet fleet, JobStage stage, int district,
        Vector3 worldJobCenter, Vector3 worldDepot)
    {
        SetFleetStage(fleet, stage, district, worldJobCenter, worldDepot, 0);
    }

    private static void SetFleetStage(CityFleet fleet, JobStage stage, int district,
        Vector3 worldJobCenter, Vector3 worldDepot, int clearedLoads)
    {
        var state = new GameState
        {
            excavators = 1,
            trucks = 1,
            bulldozers = 1,
            jobStage = stage,
            jobDistrict = district,
            districts = new[] { new DistrictState { clearedLoads = clearedLoads } }
        };
        fleet.Refresh(state, worldJobCenter, worldDepot);
    }

    private static void UpdateFleet(CityFleet fleet, float deltaTime)
    {
        Time.deltaTime = deltaTime;
        MethodInfo update = typeof(CityFleet).GetMethod("Update",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (update == null) throw new InvalidOperationException("Production CityFleet.Update was not linked.");
        update.Invoke(fleet, null);
    }

    private static void CapturePose(ExportFile output, CityFleet fleet, string label,
        string stage, float phase, float bucketLoad)
    {
        var capture = new PoseRecord { Label = label, Stage = stage, PhaseSeconds = phase,
            BucketLoad = bucketLoad, Machines = new List<MachineRecord>() };
        foreach (string field in new[] { "excavator", "truck", "bulldozer" })
        {
            Transform root = (Transform)GetField(fleet, field);
            var machine = new MachineRecord
            {
                Kind = field,
                Name = root.gameObject.name,
                WorldPosition = Vec(root.position),
                WorldTransform = Matrix(root.localToWorldMatrix),
                Nodes = new List<NodeRecord>()
            };
            CaptureNodes(output, root, root, machine.Nodes);
            machine.VertexCount = machine.Nodes.Where(node => node.Mesh != null)
                .Sum(node => output.Meshes.First(mesh => mesh.Id == node.Mesh).Vertices.Length / 3);
            machine.TriangleCount = machine.Nodes.Where(node => node.Mesh != null)
                .Sum(node => output.Meshes.First(mesh => mesh.Id == node.Mesh).Triangles.Length / 3);
            capture.Machines.Add(machine);
        }
        ValidateHydraulicLinks(fleet);
        capture.Hydraulics = HydraulicRecords(fleet);
        capture.Audio = MachineAudioRecords(fleet);
        output.Poses.Add(capture);
    }

    private static void AppendAnimationFrame(ExportFile output, AnimationClipRecord clip,
        CityFleet fleet, float time)
    {
        int index = output.Poses.Count;
        CapturePose(output, fleet, clip.Name, "Actual production animation", time,
            EquipmentMotion.Dig((float)GetField(fleet, "phase")).bucketLoad);
        PoseRecord sample = output.Poses[index];
        output.Poses.RemoveAt(index);
        sample.TimeSeconds = time;
        clip.Frames.Add(sample);
    }

    private static void CaptureWorkAnimation(ExportFile output, CityFleet fleet)
    {
        PrepareFleetAtWorkDock(fleet, new Vector3(0f, FixtureGroundY, 10f),
            new Vector3(0f, FixtureGroundY, 0f));
        var clip = new AnimationClipRecord { Name = "WorkingCycle", FramesPerSecond = 15 };
        const int frames = 210;
        float step = EquipmentMotion.DigCycleSeconds / frames;
        for (int frame = 0; frame <= frames; frame++)
        {
            if (frame != 0) UpdateFleet(fleet, step);
            AppendAnimationFrame(output, clip, fleet, frame * step);
        }
        Check(clip.Frames.Count == frames + 1,
            "Editable animation must capture the complete production working cycle.");
        output.AnimationClips.Add(clip);
    }

    private static void CaptureNodes(ExportFile output, Transform vehicleRoot,
        Transform current, List<NodeRecord> target)
    {
        var filter = current.gameObject.GetComponent<MeshFilter>();
        var renderer = current.gameObject.GetComponent<MeshRenderer>();
        string path = RelativePath(vehicleRoot, current);
        var node = new NodeRecord
        {
            Path = path,
            Name = current.gameObject.name,
            WorldTransform = Matrix(current.localToWorldMatrix),
            LocalPosition = Vec(current.localPosition),
            LocalRotation = Quat(current.localRotation),
            LocalScale = Vec(current.localScale),
            Mesh = filter == null || filter.sharedMesh == null ? null : Mesh(output, filter.sharedMesh),
            Material = renderer == null || renderer.sharedMaterial == null
                ? null : Material(output, renderer.sharedMaterial),
            Active = current.gameObject.activeInHierarchy
        };
        target.Add(node);
        for (int i = 0; i < current.childCount; i++)
            CaptureNodes(output, vehicleRoot, current.GetChild(i), target);
    }

    private static string Mesh(ExportFile output, UnityEngine.Mesh source)
    {
        MeshRecord present = output.Meshes.FirstOrDefault(mesh => mesh.RuntimeId == RuntimeHelpers.GetHashCode(source));
        if (present != null) return present.Id;
        if (source.vertices.Length == 0 || source.triangles.Length == 0)
            throw new InvalidOperationException("Production mesh is empty: " + source.name);
        if (source.normals.Length != source.vertices.Length)
            throw new InvalidOperationException("Production mesh normals do not match its vertices: " + source.name);
        if (source.uv.Length != 0 && source.uv.Length != source.vertices.Length)
            throw new InvalidOperationException("Production mesh UVs do not match its vertices: " + source.name);
        var record = new MeshRecord
        {
            Id = "mesh-" + output.Meshes.Count.ToString("D4", CultureInfo.InvariantCulture),
            Name = source.name,
            RuntimeId = RuntimeHelpers.GetHashCode(source),
            Vertices = Flatten(source.vertices),
            Triangles = (int[])source.triangles.Clone(),
            Normals = Flatten(source.normals),
            Uv = Flatten(source.uv),
            SubmeshCount = source.subMeshCount
        };
        Check(record.Triangles.Length % 3 == 0, "Production mesh triangle indices must be complete.");
        Check(record.Triangles.All(index => index >= 0 && index < source.vertices.Length),
            "Production mesh triangle index must point at an exported actual vertex.");
        for (int i = 0; i < source.normals.Length; i++)
            Check(Finite(source.normals[i]) && source.normals[i].magnitude <= 1.001f,
                "Production mesh normals must be finite unit-or-zero vectors.");
        for (int i = 0; i < source.vertices.Length; i++)
            Check(Finite(source.vertices[i]), "Production source mesh vertices must be finite.");
        output.Meshes.Add(record);
        return record.Id;
    }

    private static string Material(ExportFile output, UnityEngine.Material source)
    {
        MaterialRecord present = output.Materials.FirstOrDefault(material => material.RuntimeId ==
            RuntimeHelpers.GetHashCode(source));
        if (present != null) return present.Id;
        Color color = source.GetColor("_BaseColor");
        Texture2D texture = source.GetTexture("_BaseMap");
        string textureId = texture == null ? null : Texture(output, texture);
        var record = new MaterialRecord
        {
            Id = "material-" + output.Materials.Count.ToString("D3", CultureInfo.InvariantCulture),
            Name = source.name,
            RuntimeId = RuntimeHelpers.GetHashCode(source),
            Shader = source.shader == null ? null : source.shader.name,
            BaseColorRgba = new[] { color.r, color.g, color.b, color.a },
            Metallic = source.GetFloat("_Metallic"),
            Smoothness = source.GetFloat("_Smoothness"),
            BaseTexture = textureId,
            TextureScale = Vec(source.GetTextureScale("_BaseMap")),
            Instancing = source.enableInstancing
        };
        Check(record.BaseColorRgba.All(Finite) && Unit(record.Metallic) && Unit(record.Smoothness),
            "Production machine materials must expose finite PBR values.");
        output.Materials.Add(record);
        return record.Id;
    }

    private static string Texture(ExportFile output, Texture2D source)
    {
        TextureRecord present = output.Textures.FirstOrDefault(texture => texture.RuntimeId ==
            RuntimeHelpers.GetHashCode(source));
        if (present != null) return present.Id;
        Check(source.width > 0 && source.height > 0 && source.width <= 128 && source.height <= 128,
            "Authored fleet textures must stay within the bounded 128x128 export resolution.");
        Color[] pixels = source.GetPixels();
        Check(pixels.Length == source.width * source.height,
            "Production procedural texture pixels must be present in the captured Texture2D.");
        var record = new TextureRecord
        {
            Id = "texture-" + output.Textures.Count.ToString("D2", CultureInfo.InvariantCulture),
            Name = source.name,
            RuntimeId = RuntimeHelpers.GetHashCode(source),
            Width = source.width,
            Height = source.height,
            Format = source.format.ToString(),
            Wrap = source.wrapMode.ToString(),
            Filter = source.filterMode.ToString(),
            Anisotropy = source.anisoLevel,
            Png = "textures/" + "equipment-procedural-" +
                output.Textures.Count.ToString("D2", CultureInfo.InvariantCulture) + ".png"
        };
        output.Textures.Add(record);
        output.PendingPngs.Add(record.Id, (record.Png, source));
        return record.Id;
    }

    private static void ValidateHydraulicLinks(CityFleet fleet)
    {
        foreach (string name in new[] { "boomLift", "stickRam", "bucketRam", "bladeLiftLeft",
                     "bladeLiftRight", "bedLiftLeft", "bedLiftRight" })
        {
            object link = GetField(fleet, name);
            Transform barrel = (Transform)GetField(link, "barrel");
            Transform rod = (Transform)GetField(link, "rod");
            Transform basePin = (Transform)GetField(link, "basePin");
            Transform rodPin = (Transform)GetField(link, "rodPin");
            float span = Vector3.Distance(basePin.localPosition, rodPin.localPosition);
            float barrelLength = barrel.localScale.y;
            float rodLength = rod.localScale.y;
            Check(Finite(span) && Finite(barrelLength) && Finite(rodLength) &&
                span > .025f && barrelLength >= .04f && rodLength >= .025f &&
                Math.Abs(span - barrelLength - rodLength) < .003f &&
                Math.Abs(Vector3.Distance(basePin.localPosition, barrel.localPosition) -
                    barrelLength * .5f) < .003f &&
                Math.Abs(Vector3.Distance(rodPin.localPosition, rod.localPosition) -
                    rodLength * .5f) < .003f,
                "Production hydraulic linkage must be finite and end-to-end fitted: " + name + ".");
        }
    }

    private static List<HydraulicRecord> HydraulicRecords(CityFleet fleet)
    {
        var result = new List<HydraulicRecord>();
        foreach (string name in new[] { "boomLift", "stickRam", "bucketRam", "bladeLiftLeft",
                     "bladeLiftRight", "bedLiftLeft", "bedLiftRight" })
        {
            object link = GetField(fleet, name);
            Transform barrel = (Transform)GetField(link, "barrel");
            Transform rod = (Transform)GetField(link, "rod");
            Transform basePin = (Transform)GetField(link, "basePin");
            Transform rodPin = (Transform)GetField(link, "rodPin");
            result.Add(new HydraulicRecord
            {
                Name = name,
                BasePin = Vec(basePin.position),
                RodPin = Vec(rodPin.position),
                Barrel = Vec(barrel.position),
                Rod = Vec(rod.position),
                BarrelLength = barrel.localScale.y,
                RodLength = rod.localScale.y,
                BarrelMesh = MeshFrom(barrel),
                RodMesh = MeshFrom(rod)
            });
        }
        return result;
    }

    private static string MeshFrom(Transform transform)
    {
        MeshFilter filter = transform.gameObject.GetComponent<MeshFilter>();
        return filter == null || filter.sharedMesh == null ? null : filter.sharedMesh.name;
    }

    private static List<AudioRecord> MachineAudioRecords(CityFleet fleet)
    {
        var result = new List<AudioRecord>();
        MethodInfo method = typeof(CityFleet).GetMethod("TryGetMachineAudioState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("Missing production machine-audio sample API.");
        for (int index = 0; index < 3; index++)
        {
            object[] args = { index, Vector3.zero, 0f, 0f, 0f };
            bool active = (bool)method.Invoke(fleet, args);
            Vector3 position = (Vector3)args[1];
            float load = (float)args[2], movement = (float)args[3], hydraulics = (float)args[4];
            string[] machineFields = { "excavator", "truck", "bulldozer" };
            Transform machine = (Transform)GetField(fleet, machineFields[index]);
            Check(active == machine.gameObject.activeInHierarchy &&
                Finite(position) && Unit(load) && Unit(movement) && Unit(hydraulics),
                "Production machine-audio state for index " + index.ToString(CultureInfo.InvariantCulture) +
                " must match the machine's active state and keep finite normalized output.");
            result.Add(new AudioRecord
            {
                MachineIndex = index,
                Active = active,
                WorldPosition = Vec(position),
                Load = load,
                Movement = movement,
                Hydraulics = hydraulics
            });
        }
        return result;
    }

    private static void ValidateCapturedFleet(ExportFile output)
    {
        Check(output.Poses.Count == 5, "Export all five baseline/loading/haul/tip poses.");
        Check(output.Materials.Count >= 5, "Capture all authored production fleet materials.");
        Check(output.Textures.Count >= 2, "Capture actual procedural wear textures for paint and steel.");
        Check(output.Meshes.Sum(mesh => mesh.Vertices.Length / 3) <= 50000,
            "Three production machine models must stay below the 50,000 unique mesh-vertex fixture budget.");
        foreach (PoseRecord pose in output.Poses)
        {
            Check(pose.Machines.Count == 3, "Each pose must export excavator, tipper and dozer.");
            foreach (MachineRecord machine in pose.Machines)
            {
                Check(machine.VertexCount > 0 && machine.VertexCount <= 50000 &&
                    machine.TriangleCount > 0 && machine.TriangleCount <= 12000,
                    "Production " + machine.Kind + " must stay inside the 50,000-vertex and 12,000-triangle fixture budgets.");
                Check(machine.WorldTransform.All(Finite) && machine.WorldPosition.All(Finite),
                    "Each production machine must carry a finite world transform and position.");
                Check(machine.Nodes.Any(node => node.Mesh != null),
                    "Every production fleet machine must export its real MeshFilter.sharedMesh.");
                Check(machine.Nodes.Any(node => node.WorldTransform.Length == 16),
                    "Production child mesh hierarchy must carry a world transformation.");
            }
        }
        Check(output.Poses[2].Machines.Single(machine => machine.Kind == "truck").Nodes
                .Count(node => node.Name.StartsWith("Visible irregular rubble load", StringComparison.Ordinal) &&
                    node.Active) > 0,
            "Loaded production tipper must carry actual rubble meshes during hauling.");
        Check(output.Poses[3].Machines.Single(machine => machine.Kind == "truck").Nodes
                .Single(node => node.Name == "Rear-hinged dump bed").LocalRotation.Length == 4,
            "Full tipper pose must serialize its production bed articulation quaternion.");
    }

    private static void WriteTextures(ExportFile output, string destination)
    {
        foreach (TextureRecord record in output.Textures)
        {
            (string relative, Texture2D texture) payload = output.PendingPngs[record.Id];
            string path = Path.Combine(destination,
                payload.relative.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(path, Png(payload.texture));
        }
    }

    private static byte[] Png(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        using (var raw = new MemoryStream())
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, true))
        {
            for (int y = texture.height - 1; y >= 0; y--)
            {
                zlib.WriteByte(0);
                for (int x = 0; x < texture.width; x++)
                {
                    Color pixel = pixels[y * texture.width + x];
                    zlib.WriteByte(ToByte(pixel.r));
                    zlib.WriteByte(ToByte(pixel.g));
                    zlib.WriteByte(ToByte(pixel.b));
                    zlib.WriteByte(ToByte(pixel.a));
                }
            }
            zlib.Dispose();
            using (var png = new MemoryStream())
            {
                png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                byte[] header = new byte[13];
                Put32(header, 0, texture.width);
                Put32(header, 4, texture.height);
                header[8] = 8; header[9] = 6;
                Chunk(png, "IHDR", header);
                Chunk(png, "IDAT", raw.ToArray());
                Chunk(png, "IEND", Array.Empty<byte>());
                return png.ToArray();
            }
        }
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 255f);
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        Write32(output, data.Length);
        output.Write(name, 0, name.Length);
        output.Write(data, 0, data.Length);
        uint crc = 0xffffffffu;
        for (int i = 0; i < name.Length; i++) crc = Crc(crc, name[i]);
        for (int i = 0; i < data.Length; i++) crc = Crc(crc, data[i]);
        Write32(output, unchecked((int)~crc));
    }
    private static uint Crc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }
    private static void Put32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
    private static void Write32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static string RelativePath(Transform root, Transform node)
    {
        var names = new Stack<string>();
        for (Transform current = node; current != null; current = current.parent)
        {
            names.Push(current.gameObject.name);
            if (current == root) break;
        }
        return string.Join("/", names);
    }

    private static object GetField(object owner, string name)
    {
        if (owner == null) throw new InvalidOperationException("Missing reflected production object: " + name);
        FieldInfo field = owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing production field " + name +
            " on " + owner.GetType().Name + ".");
        return field.GetValue(owner);
    }
    private static void SetField(object owner, string name, object value)
    {
        FieldInfo field = owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing production field " + name + ".");
        field.SetValue(owner, value);
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static bool Unit(float value) { return Finite(value) && value >= 0f && value <= 1f; }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
    private static bool Finite(Vector2 value) { return Finite(value.x) && Finite(value.y); }
    private static float[] Vec(Vector3 value) { return new[] { value.x, value.y, value.z }; }
    private static float[] Vec(Vector2 value) { return new[] { value.x, value.y }; }
    private static float[] Quat(Quaternion value)
    {
        // Serialization follows System.Numerics' x,y,z,w field order.
        var matrix = Matrix4x4.TRS(Vector3.zero, value, Vector3.one).ToArray();
        return QuaternionComponents(value, matrix);
    }
    private static float[] QuaternionComponents(Quaternion value, float[] matrix)
    {
        // Matrix-to-quaternion conversion avoids exposing test-double implementation fields.
        float trace = matrix[0] + matrix[5] + matrix[10];
        float x, y, z, w;
        if (trace > 0f)
        {
            float s = (float)Math.Sqrt(trace + 1f) * 2f;
            w = .25f * s; x = (matrix[9] - matrix[6]) / s;
            y = (matrix[2] - matrix[8]) / s; z = (matrix[4] - matrix[1]) / s;
        }
        else if (matrix[0] > matrix[5] && matrix[0] > matrix[10])
        {
            float s = (float)Math.Sqrt(1f + matrix[0] - matrix[5] - matrix[10]) * 2f;
            w = (matrix[9] - matrix[6]) / s; x = .25f * s;
            y = (matrix[1] + matrix[4]) / s; z = (matrix[2] + matrix[8]) / s;
        }
        else if (matrix[5] > matrix[10])
        {
            float s = (float)Math.Sqrt(1f + matrix[5] - matrix[0] - matrix[10]) * 2f;
            w = (matrix[2] - matrix[8]) / s; x = (matrix[1] + matrix[4]) / s;
            y = .25f * s; z = (matrix[6] + matrix[9]) / s;
        }
        else
        {
            float s = (float)Math.Sqrt(1f + matrix[10] - matrix[0] - matrix[5]) * 2f;
            w = (matrix[4] - matrix[1]) / s; x = (matrix[2] + matrix[8]) / s;
            y = (matrix[6] + matrix[9]) / s; z = .25f * s;
        }
        return new[] { x, y, z, w };
    }
    private static float[] Matrix(Matrix4x4 value) { return value.ToArray(); }
    private static float[] Flatten(Vector3[] values)
    {
        var result = new float[values.Length * 3];
        for (int i = 0; i < values.Length; i++)
        {
            result[i * 3] = values[i].x;
            result[i * 3 + 1] = values[i].y;
            result[i * 3 + 2] = values[i].z;
        }
        return result;
    }
    private static float[] Flatten(Vector2[] values)
    {
        var result = new float[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            result[i * 2] = values[i].x;
            result[i * 2 + 1] = values[i].y;
        }
        return result;
    }
    private static string FindProjectRoot()
    {
        for (DirectoryInfo current = new DirectoryInfo(Environment.CurrentDirectory);
             current != null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "Assets", "NewGaza")))
                return current.FullName;
        throw new DirectoryNotFoundException("Run the equipment fixture from the project root.");
    }

    private sealed class ExportFile
    {
        public int SchemaVersion;
        public string[] Source;
        public string CaptureMethod, CoordinateConvention, UnityMathStub;
        public List<MeshRecord> Meshes;
        public List<MaterialRecord> Materials;
        public List<TextureRecord> Textures = new List<TextureRecord>();
        public List<PoseRecord> Poses;
        public List<AnimationClipRecord> AnimationClips = new List<AnimationClipRecord>();
        public CaptureSummary Summary;
        public MotionMetrics Motion;
        [System.Text.Json.Serialization.JsonIgnore]
        public readonly Dictionary<string, (string relative, Texture2D texture)> PendingPngs =
            new Dictionary<string, (string relative, Texture2D texture)>();
    }
    private sealed class MeshRecord
    {
        public string Id, Name;
        [System.Text.Json.Serialization.JsonIgnore]
        public int RuntimeId;
        public int SubmeshCount;
        public float[] Vertices, Normals, Uv;
        public int[] Triangles;
    }
    private sealed class MaterialRecord
    {
        public string Id, Name, Shader, BaseTexture;
        [System.Text.Json.Serialization.JsonIgnore]
        public int RuntimeId;
        public float[] BaseColorRgba, TextureScale;
        public float Metallic, Smoothness;
        public bool Instancing;
    }
    private sealed class TextureRecord
    {
        public string Id, Name, Format, Wrap, Filter, Png;
        [System.Text.Json.Serialization.JsonIgnore]
        public int RuntimeId;
        public int Width, Height, Anisotropy;
    }
    private sealed class PoseRecord
    {
        public string Label, Stage;
        public float PhaseSeconds, BucketLoad;
        public float TimeSeconds;
        public List<MachineRecord> Machines;
        public List<HydraulicRecord> Hydraulics;
        public List<AudioRecord> Audio;
    }
    private sealed class AnimationClipRecord
    {
        public string Name;
        public int FramesPerSecond;
        public List<PoseRecord> Frames = new List<PoseRecord>();
    }
    private sealed class MachineRecord
    {
        public string Kind, Name;
        public int VertexCount, TriangleCount;
        public float[] WorldPosition, WorldTransform;
        public List<NodeRecord> Nodes;
    }
    private sealed class NodeRecord
    {
        public string Path, Name, Mesh, Material;
        public float[] WorldTransform, LocalPosition, LocalRotation, LocalScale;
        public bool Active;
    }
    private sealed class HydraulicRecord
    {
        public string Name, BarrelMesh, RodMesh;
        public float[] BasePin, RodPin, Barrel, Rod;
        public float BarrelLength, RodLength;
    }
    private sealed class AudioRecord
    {
        public int MachineIndex;
        public bool Active;
        public float[] WorldPosition;
        public float Load, Movement, Hydraulics;
    }
    private sealed class CaptureSummary
    {
        public int UniqueMeshCount, UniqueMeshVertices, PoseCount;
        public int PeakSingleMachineVertices, PeakSingleMachineTriangles;
    }
    private sealed class MotionMetrics
    {
        public float DigSweepCityUnits, NormalFleetEnvelopeCityUnits, DepotFleetEnvelopeCityUnits;
        public float DigSweepMeters, NormalFleetEnvelopeMeters, DepotFleetEnvelopeMeters;
        public float ActualProductionExcavatorSweepCityUnits;
        public float CityUnitsToMeters, NormalLimitCityUnits, DepotLimitCityUnits;
        public GroundContactMetrics GroundContact;
        public BedContactMetrics ReleaseContact;
        public List<TrackMotionMetrics> TrackMotion;
        public List<TruckRouteMetrics> TruckRoutes;
        public HaulCycleMetrics HaulCycle;
    }
    private sealed class GroundContactMetrics
    {
        public float ContactDistanceModelMeters;
        public float MinimumDigArcClearanceModelMeters;
        public float MinimumLiftedClearanceModelMeters;
        public int SampledFrames;
    }
    private sealed class BedContactMetrics
    {
        public float FloorTopModelY, WallTopModelY;
        public float InnerXMinModel, InnerXMaxModel, InnerZMinModel, InnerZMaxModel;
        public float[] ToothTipBedLocalBounds;
        public int ToothTipVertexCount;
    }
    private sealed class TrackMotionMetrics
    {
        public string Machine;
        public float TravelCityUnits;
        public float MaximumShoeVertexMotionModelMeters;
        public float MaximumRollerRotationDegrees;
        public bool StationaryShoesUnchanged, StationaryRollersUnchanged;
    }
    private sealed class TruckRouteMetrics
    {
        public float DispatchDistanceCityUnits;
        public float MaximumVisibleSpeedCityUnitsPerSecond;
        public float ObservedTravelCityUnits;
        public float MinimumHeadingAlignment;
        public float FullTurnMinimumHeadingAlignment;
        public float SampledSeconds;
    }
    private sealed class HaulCycleMetrics
    {
        public int CyclePoseCount, ReleaseFragmentCount, DispatchCount, DepotUnloadCount;
        public bool ReturnedToWorkDock;
        public float MaximumRouteSpeedCityUnitsPerSecond;
    }
    private sealed class VertexReference
    {
        public readonly MeshFilter Filter;
        public readonly int Index;
        public VertexReference(MeshFilter filter, int index)
        {
            Filter = filter;
            Index = index;
        }
    }
    private sealed class BedInterior
    {
        public readonly float FloorTop, WallTop, InnerXMin, InnerXMax, InnerZMin, InnerZMax;
        public BedInterior(float floorTop, float wallTop, float innerXMin, float innerXMax,
            float innerZMin, float innerZMax)
        {
            FloorTop = floorTop;
            WallTop = wallTop;
            InnerXMin = innerXMin;
            InnerXMax = innerXMax;
            InnerZMin = innerZMin;
            InnerZMax = innerZMax;
        }
    }
}