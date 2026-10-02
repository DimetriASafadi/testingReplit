using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>Transform-only, deterministic presentation of the owned salvage fleet.</summary>
    public sealed class CityFleet : MonoBehaviour
    {
        // Vehicle geometry is authored in model-space metres; only vehicle roots
        // are scaled. Geography and route destinations remain in city coordinates.
        private const float VehicleScale = .07f;
        private const float NativeWorkSurfaceAboveGroundCityUnits = .006f;
        private const float DepotApronAboveGroundCityUnits = .003f;
        internal const float HaulingSpeed = .22f; // city units/s = 4.4 m/s at 20 m/city unit.
        internal const float HaulingSpeedMetersPerSecond = HaulingSpeed * 20f;
        private const float VehicleRepositionFadeSeconds = .35f;

        private enum TruckTripState
        {
            ParkedAtDepot, ParkedAtWork, TurningOutFromWork, Outbound,
            TurningAtDepot, Unloading, Inbound, TurningInAtWork, ReassignFade
        }

        private CityGeometry geometry;
        private Material paint, steel, rubber, glass, dark, rubble;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private Transform excavator, turret, boom, stick, bucket;
        private Transform bulldozer, blade, truck, truckBed, cargo, bucketPayload;
        private Transform[] wheels;
        private Transform[] cargoPieces;
        private Transform[] excavatorJoints;
        private Transform[] transitionParts;
        private Vector3[] transitionPositions;
        private Quaternion[] transitionRotations;
        private readonly bool[] rootTransitionSkipped = new bool[2];
        private readonly bool[] vehicleFadePending = new bool[2];
        private readonly float[] vehicleFadeClock = new float[2];
        private EquipmentHydraulicLink boomLift, stickRam, bucketRam, bladeLiftLeft,
            bladeLiftRight, bedLiftLeft, bedLiftRight;
        private EquipmentTrackRig excavatorTracks, bulldozerTracks;
        private Transform boomBaseAnchor, boomMovingAnchor, stickBaseAnchor, stickMovingAnchor;
        private Transform bucketBaseAnchor, bucketMovingAnchor, bladeBaseLeft, bladeMovingLeft;
        private Transform bladeBaseRight, bladeMovingRight, bedBaseLeft, bedMovingLeft;
        private Transform bedBaseRight, bedMovingRight;
        private Mesh trackBeltMesh, tireTreadMesh;
        private JobStage stage;
        private Vector3 jobCenter;
        private Vector3 depot;
        private int districtIndex;
        private float phase;
        private float transitionClock = 2f;
        private int previousDistrict = -1;
        private JobStage previousStage = JobStage.Idle;
        private bool excavatorOwned, truckOwned, bulldozerOwned;
        private bool importedJob;
        private Vector3[] route = new Vector3[8];
        private int routeCount;
        private Vector3[] tripRoute = new Vector3[2];
        private float tripRouteLength;
        private CityRoadRoute roadTripRoute;
        private CityRoadNetwork roadNetwork;
        private Func<string, float> roadSpeedMultiplier;
        private bool roadsConfigured;
        private string truckRouteStatus = "لم يُهيّأ مسار الشارع بعد.";
        private string excavatorRouteStatus, bulldozerRouteStatus;
        public string RouteStatus
        {
            get
            {
                string status = truckRouteStatus;
                if (!string.IsNullOrEmpty(excavatorRouteStatus))
                    status = excavatorRouteStatus + " " + status;
                if (!string.IsNullOrEmpty(bulldozerRouteStatus))
                    status = bulldozerRouteStatus + " " + status;
                return status;
            }
        }
        private CityRoadRoute excavatorRoadRoute, bulldozerRoadRoute;
        private float excavatorRoadDistance, bulldozerRoadDistance;
        private Vector3 excavatorRoadTarget, bulldozerRoadTarget;
        private bool excavatorOnRoad, bulldozerOnRoad;
        private bool excavatorDockedAtWork, bulldozerDockedAtWork;
        private bool trackedDestinationClearing;
        private bool trackedDestinationInitialized;
        private Vector3 lastExcavatorRoadTarget, lastBulldozerRoadTarget;
        private float tripDistance;
        private float tripClock;
        private Quaternion tripTurnStart, tripTurnTarget;
        private TruckTripState truckTripState = TruckTripState.ParkedAtDepot;
        private bool excavatorWaitingForTruck;
        private float excavatorHoldPhase;
        private bool pendingRouteRebuild;
        private bool truckReassignToWork;
        private bool roadReassignTravel;
        private int transferredCargoPieces;
        private float dozerWorkRootOffsetModel;
        private float depotExcavatorRootOffsetModel;
        private float depotDozerRootOffsetModel;
        private float depotTruckRootOffsetModel;
        private float truckWheelBottomYModel;
        private float wheelRoll;
        private bool truckTravelAdvancedThisFrame;
        internal float LastMeasuredTruckWorldSpeed { get; private set; }
        public float ActualWorldSpeed { get { return LastMeasuredTruckWorldSpeed; } }
        internal float LastMeasuredTruckSpeedMetersPerSecond =>
            LastMeasuredTruckWorldSpeed * 20f;
        private Vector3 previousTruckPosition;
        private Vector3 previousWheelPosition;
        private Vector3 previousMeasuredTruckWorldPosition;
        private bool hasTruckWorldSpeedSample;
        private Vector3 previousExcavatorPosition;
        private Vector3 previousDozerPosition;
        private Quaternion previousTruckRotation;
        private Quaternion previousExcavatorRotation;
        private Quaternion previousDozerRotation;
        private bool haveMotionSample;
        private Quaternion[] previousExcavatorJoints;
        private Quaternion previousBladeRotation;
        private Quaternion previousBedRotation;
        private float excavatorLoad, excavatorMovement, excavatorHydraulics;
        private float truckLoad, truckMovement, truckHydraulics;
        private float dozerLoad, dozerMovement, dozerHydraulics;
        internal float MaximumAnimatedHorizontalEnvelope { get; private set; }
        internal float MaximumExcavatorSweepEnvelope { get; private set; }

        internal void Initialize(CityGeometry source, Material construction, Material windows, Material tires,
            Material exposedSteel, Material paintSource, Material stone)
        {
            geometry = source;
            paint = Finish(paintSource, "worn diesel ochre enamel", new Color(.72f, .43f, .14f),
                .15f, .28f, 17);
            steel = Finish(exposedSteel, "rubbed weathered machinery steel", new Color(.46f, .47f, .45f),
                .75f, .25f, 43);
            rubber = Finish(tires, "matte tread rubber", new Color(.105f, .11f, .105f),
                0f, .12f, 0);
            glass = Finish(windows, "smoky green-grey safety glass", new Color(.34f, .40f, .39f),
                .03f, .82f, 0);
            dark = Finish(exposedSteel, "dark coated machinery recess", new Color(.12f, .135f, .13f),
                .2f, .2f, 0);
            rubble = Finish(stone, "dusty carried concrete", new Color(.76f, .72f, .64f),
                0f, .18f, 0);

            trackBeltMesh = EquipmentGeometry.TrackBelt(geometry, "Closed linked-track carcass", 1.95f, .44f, .34f);
            tireTreadMesh = EquipmentGeometry.TireTread(geometry, "Low-poly truck tyre casing");
            BuildExcavator();
            BuildTruck();
            BuildBulldozer();
            ConfigureGroundedVehicleOffsets();
            excavator.gameObject.SetActive(false);
            truck.gameObject.SetActive(false);
            bulldozer.gameObject.SetActive(false);
            transitionParts = new[] { excavator, turret, boom, stick, bucket,
                bulldozer, blade, truckBed };
            transitionPositions = new Vector3[transitionParts.Length];
            transitionRotations = new Quaternion[transitionParts.Length];
            previousExcavatorJoints = new Quaternion[4];
            excavatorJoints = new[] { turret, boom, stick, bucket };
            SnapshotMotion();
        }

        /// <summary>Configures the production sourced street graph exactly once.</summary>
        public void ConfigureRoads(CityRoadNetwork roads, Func<string, float> speedMultiplier)
        {
            if (roads == null) throw new ArgumentNullException(nameof(roads));
            if (roadsConfigured)
                throw new InvalidOperationException("CityFleet road routing may only be configured once.");
            roadNetwork = roads;
            roadSpeedMultiplier = speedMultiplier;
            roadsConfigured = true;
            truckRouteStatus = "جارٍ حساب مسارات الشوارع.";
            trackedDestinationInitialized = false;
        }

        private void ConfigureGroundedVehicleOffsets()
        {
            float workSurfaceHeightModel =
                NativeWorkSurfaceAboveGroundCityUnits / VehicleScale;
            float depotSurfaceHeightModel =
                DepotApronAboveGroundCityUnits / VehicleScale;
            float excavatorTrackBottom = excavatorTracks.LowestShoeVertexYModel;
            float dozerTrackBottom = bulldozerTracks.LowestShoeVertexYModel;
            truckWheelBottomYModel = FindLowestTruckWheelMeshYModel();
            if (float.IsNaN(excavatorTrackBottom) || float.IsInfinity(excavatorTrackBottom) ||
                float.IsNaN(dozerTrackBottom) || float.IsInfinity(dozerTrackBottom) ||
                float.IsNaN(truckWheelBottomYModel) || float.IsInfinity(truckWheelBottomYModel))
                throw new InvalidOperationException(
                    "Unable to derive fleet root heights from the authored tread and wheel meshes.");

            float workRootHeight = workSurfaceHeightModel - excavatorTrackBottom;
            float truckWorkRootHeight = workSurfaceHeightModel - truckWheelBottomYModel;
            EquipmentMotion.ConfigureGroundedRootHeights(workRootHeight, truckWorkRootHeight);
            dozerWorkRootOffsetModel = workSurfaceHeightModel - dozerTrackBottom -
                EquipmentMotion.WorkRootHeightModel;
            depotExcavatorRootOffsetModel = depotSurfaceHeightModel - excavatorTrackBottom;
            depotDozerRootOffsetModel = depotSurfaceHeightModel - dozerTrackBottom;
            depotTruckRootOffsetModel = depotSurfaceHeightModel - truckWheelBottomYModel;
        }

        private float FindLowestTruckWheelMeshYModel()
        {
            float lowest = float.PositiveInfinity;
            for (int wheel = 0; wheel < wheels.Length; wheel++)
            {
                MeshFilter[] filters = wheels[wheel].gameObject.GetComponentsInChildren<MeshFilter>(true);
                for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
                {
                    Mesh mesh = filters[filterIndex].sharedMesh;
                    if (mesh == null) continue;
                    Vector3[] vertices = mesh.vertices;
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        Vector3 world = filters[filterIndex].transform.TransformPoint(
                            vertices[vertex]);
                        Vector3 truckLocal = truck.InverseTransformPoint(world);
                        lowest = Mathf.Min(lowest, truckLocal.y);
                    }
                }
            }
            return lowest;
        }

        private Material Finish(Material source, string name, Color tint, float metallic,
            float smoothness, int textureSeed)
        {
            var material = new Material(source) { name = "New Gaza • " + name };
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            if (textureSeed != 0)
            {
                Texture2D texture = WornTexture(name, textureSeed);
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            }
            ownedMaterials.Add(material);
            return material;
        }

        private Texture2D WornTexture(string name, int seed)
        {
            const int resolution = 64;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, false)
            {
                name = "New Gaza • procedural worn surface • " + name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2
            };
            var pixels = new Color[resolution * resolution];
            float offset = seed * .173f;
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)resolution;
                    float v = y / (float)resolution;
                    float broad = Mathf.PerlinNoise(offset + u * 4.1f, offset + v * 4.1f);
                    float grain = Mathf.PerlinNoise(offset + u * 27f, offset + v * 27f);
                    float scratches = Mathf.PerlinNoise(offset + u * 11f, offset + v * 37f);
                    float value = .84f + broad * .105f + grain * .045f;
                    if (scratches > .83f && Mathf.Sin((v * 13f + offset) * Mathf.PI) > .4f)
                        value *= .84f;
                    pixels[y * resolution + x] = new Color(value, value, value, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            ownedTextures.Add(texture);
            return texture;
        }

        internal void Refresh(GameState state, Vector3 worldJobCenter, Vector3 worldDepot)
        {
            Vector3 nextJobCenter = transform.InverseTransformPoint(worldJobCenter);
            Vector3 nextDepot = transform.InverseTransformPoint(worldDepot);
            bool locationChanged = Vector3.Distance(jobCenter, nextJobCenter) > .001f ||
                Vector3.Distance(depot, nextDepot) > .001f;
            bool changed = previousDistrict != state.jobDistrict ||
                previousStage != state.jobStage || locationChanged;
            bool previouslyOwnedTruck = truckOwned;
            bool nextImportedJob = state.jobDistrict >= 0 &&
                state.jobDistrict < state.districts.Length &&
                state.districts[state.jobDistrict].clearedLoads >=
                    GameCatalog.Districts[state.jobDistrict].rubbleLoads;
            bool routeChanged = previousDistrict != state.jobDistrict ||
                importedJob != nextImportedJob || locationChanged || routeCount == 0;
            bool hadPreviousScene = previousDistrict >= 0;
            if (changed)
            {
                if (hadPreviousScene) CaptureTransitionPose();
                if (state.jobStage == JobStage.Clearing &&
                    (previousStage != JobStage.Clearing ||
                     previousDistrict != state.jobDistrict) && !IsTruckTripActive())
                {
                    excavatorWaitingForTruck = false;
                    excavatorHoldPhase = 0f;
                }
                previousDistrict = state.jobDistrict;
                previousStage = state.jobStage;
                phase = 0f;
                transitionClock = hadPreviousScene ? 0f : 2f;
            }
            stage = state.jobStage;
            districtIndex = state.jobDistrict;
            jobCenter = nextJobCenter;
            depot = nextDepot;
            if (roadsConfigured && !trackedDestinationInitialized)
            {
                SetPose(excavator, depot + VehicleOffset(
                    new Vector3(0f, depotExcavatorRootOffsetModel, 0f)), Vector3.zero);
                SetPose(bulldozer, depot + VehicleOffset(
                    new Vector3(0f, depotDozerRootOffsetModel, 0f)), Vector3.zero);
                trackedDestinationInitialized = true;
                trackedDestinationClearing = false;
            }
            excavatorOwned = state.excavators > 0;
            truckOwned = state.trucks > 0;
            bulldozerOwned = state.bulldozers > 0;
            importedJob = nextImportedJob;
            Vector3 workOffset = importedJob ? new Vector3(.3f,0f,5.6f) : Vector3.zero;
            MaximumExcavatorSweepEnvelope =
                EquipmentMotion.MaximumDigEnvelope(workOffset) * VehicleScale;
            MaximumAnimatedHorizontalEnvelope =
                EquipmentMotion.MaximumFleetWorkEnvelope(workOffset) * VehicleScale;
            excavator.gameObject.SetActive(excavatorOwned);
            truck.gameObject.SetActive(truckOwned);
            bulldozer.gameObject.SetActive(bulldozerOwned);
            if (routeChanged)
            {
                if (IsTruckTripActive())
                    pendingRouteRebuild = true;
                else if (hadPreviousScene && truckOwned)
                    BeginTruckReassignment(stage == JobStage.Clearing);
                else
                {
                    CreateRoute();
                    if (truckOwned && stage == JobStage.Clearing)
                        BeginInbound();
                }
            }
            else if (changed && truckOwned)
            {
                if (stage == JobStage.Clearing &&
                    truckTripState == TruckTripState.ParkedAtDepot)
                    BeginInbound();
                else if (stage != JobStage.Clearing &&
                    truckTripState == TruckTripState.ParkedAtWork)
                    BeginWorkDeparture();
            }
            else if (!previouslyOwnedTruck && truckOwned)
            {
                if (hadPreviousScene)
                    BeginTruckReassignment(stage == JobStage.Clearing);
                else if (stage == JobStage.Clearing)
                    BeginInbound();
            }
        }

        private Transform Root(string name, Transform parent)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            return root;
        }

        private Transform VehicleRoot(string name)
        {
            Transform root = Root(name, transform);
            root.localScale = Vector3.one * VehicleScale;
            return root;
        }

        // Geographic destinations stay unscaled; only authored model-space offsets use this.
        private static Vector3 VehicleOffset(Vector3 modelOffset)
        {
            return modelOffset * VehicleScale;
        }

        private static void SetPose(Transform vehicle, Vector3 geographicPosition, Vector3 modelOffset)
        {
            vehicle.localPosition = geographicPosition + VehicleOffset(modelOffset);
        }

        private Transform Part(string name, Transform parent, Mesh mesh, Material material,
            Vector3 position, Vector3 scale, Quaternion rotation, bool shadows = false)
        {
            Transform root = Root(name, parent);
            root.localPosition = position;
            root.localRotation = rotation;
            root.localScale = scale;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return root;
        }

        private void Cube(string name, Transform parent, Material material, Vector3 position,
            Vector3 scale, bool shadows = false)
        {
            Part(name, parent, geometry.Box, material, position, scale, Quaternion.identity, shadows);
        }

        private void Pane(CityMeshBatch batch, string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            batch.Add(EquipmentGeometry.Quad(geometry, name, a, b, c, d), glass,
                Vector3.zero, Vector3.one, Quaternion.identity);
        }

        private void BuildExcavator()
        {
            excavator = VehicleRoot("Compact tracked excavator • ochre diesel");
            var undercarriage = new CityMeshBatch(geometry);
            undercarriage.Box(paint, new Vector3(0f, .47f, 0f), new Vector3(1.06f, .18f, 1.42f));
            undercarriage.Box(steel, new Vector3(0f, .56f, 0f), new Vector3(.75f, .10f, .9f));
            undercarriage.Add(geometry.Cylinder, steel, new Vector3(0f, .64f, 0f),
                new Vector3(.82f, .10f, .82f), Quaternion.identity);
            undercarriage.Build("Track chains / rollers / slewing ring", excavator, Vector3.zero, true);
            excavatorTracks = new EquipmentTrackRig(geometry, excavator, trackBeltMesh,
                rubber, steel, dark, VehicleScale, .57f, 1.95f, .44f, .34f);

            turret = Root("Upper house • rotating counterweight and operator cab", excavator);
            turret.localPosition = new Vector3(0f, .65f, 0f);
            var upper = new CityMeshBatch(geometry);
            upper.Box(paint, new Vector3(0f, .15f, -.04f), new Vector3(1.23f, .38f, 1.38f));
            upper.Box(paint, new Vector3(0f, .38f, -.64f), new Vector3(1.22f, .44f, .37f));
            upper.Box(steel, new Vector3(0f, .40f, -.81f), new Vector3(1.11f, .13f, .12f));
            upper.Box(dark, new Vector3(0f, .60f, -.54f), new Vector3(.78f, .32f, .30f));
            // Recessed rear cooling grille, separated vertical slats and side louvers.
            for (int i = 0; i < 7; i++)
                upper.Box(steel, new Vector3(-.30f + i * .10f, .42f, -.405f),
                    new Vector3(.035f, .20f, .018f), 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                    upper.Box(dark, new Vector3(side * .623f, .32f + i * .055f, -.53f),
                        new Vector3(.018f, .022f, .27f));
                upper.Round(steel, new Vector3(side * .34f, .37f, -.02f),
                    new Vector3(.09f, .05f, .12f));
            }
            // Offset exhaust stack and perforated rain cap.
            upper.Round(dark, new Vector3(.40f, .75f, -.56f), new Vector3(.085f, .44f, .085f));
            upper.Round(steel, new Vector3(.40f, .98f, -.56f), new Vector3(.14f, .055f, .14f));

            // Four glazed panels in a sloped safety-cab shell, each with structural uprights.
            Vector3 frontBL = new Vector3(-.30f, .56f, .52f);
            Vector3 frontBR = new Vector3(.04f, .56f, .52f);
            Vector3 frontTR = new Vector3(.04f, 1.15f, .31f);
            Vector3 frontTL = new Vector3(-.30f, 1.15f, .31f);
            Pane(upper, "Inclined laminated front pane", frontBL, frontBR, frontTR, frontTL);
            Vector3 rearBL = new Vector3(-.30f, .56f, -.20f);
            Vector3 rearBR = new Vector3(.04f, .56f, -.20f);
            Vector3 rearTR = new Vector3(.04f, 1.15f, -.28f);
            Vector3 rearTL = new Vector3(-.30f, 1.15f, -.28f);
            Pane(upper, "Cab rear pane", rearBL, rearBR, rearTR, rearTL);
            Pane(upper, "Cab left split side glazing", new Vector3(-.30f,.56f,.52f),
                new Vector3(-.30f,.56f,-.20f), new Vector3(-.30f,1.15f,-.28f),
                new Vector3(-.30f,1.15f,.31f));
            Pane(upper, "Cab right split side glazing", new Vector3(.04f,.56f,.52f),
                new Vector3(.04f,.56f,-.20f), new Vector3(.04f,1.15f,-.28f),
                new Vector3(.04f,1.15f,.31f));
            upper.Beam(paint, new Vector3(-.37f,.57f,.53f), new Vector3(-.37f,1.22f,.29f), .055f);
            upper.Beam(paint, new Vector3(.11f,.57f,.53f), new Vector3(.11f,1.22f,.29f), .055f);
            upper.Beam(paint, new Vector3(-.36f,.57f,-.22f), new Vector3(-.36f,1.22f,-.30f), .05f);
            upper.Beam(paint, new Vector3(.11f,.57f,-.22f), new Vector3(.11f,1.22f,-.30f), .05f);
            upper.Beam(paint, frontTL, frontTR, .05f);
            upper.Beam(paint, rearTL, rearTR, .05f);
            upper.Beam(paint, new Vector3(-.37f,.56f,.16f), new Vector3(-.37f,1.15f,.12f), .035f);
            upper.Beam(paint, new Vector3(.11f,.56f,.16f), new Vector3(.11f,1.15f,.12f), .035f);
            upper.Box(paint, new Vector3(-.13f,1.20f,.03f), new Vector3(.57f,.11f,.77f));
            upper.Box(dark, new Vector3(-.13f,.53f,.16f), new Vector3(.47f,.06f,.50f));
            upper.Build("Faceted counterweight / framed smoky cab / engine details", turret, Vector3.zero, true);

            boom = Root("Boom foot • pinned to the revolving upper carriage", turret);
            boom.localPosition = new Vector3(.17f, .34f, .36f);
            Part("Forged tapered main boom", boom, EquipmentGeometry.TaperedBeam(geometry, "Forged tapered boom"),
                paint, new Vector3(0f,.70f,.44f), new Vector3(.34f,1.42f,.33f), Quaternion.identity, true);
            Part("Boom side wear plate", boom, geometry.Box, steel,
                new Vector3(.19f,.69f,.46f), new Vector3(.035f,1.18f,.20f), Quaternion.identity);
            boomBaseAnchor = Root("Boom lift cylinder fixed eye", turret);
            boomBaseAnchor.localPosition = new Vector3(.23f,.27f,.22f);
            boomMovingAnchor = Root("Boom lift cylinder pinned eye", boom);
            boomMovingAnchor.localPosition = new Vector3(.12f,.90f,.47f);
            boomLift = new EquipmentHydraulicLink(geometry, turret, steel, "Boom lift ram", .11f, .58f);

            stick = Root("Dipper stick pivot", boom);
            stick.localPosition = new Vector3(0f, 1.28f, .78f);
            Part("Tapered dipper stick", stick, EquipmentGeometry.TaperedBeam(geometry, "Dipper forging"),
                paint, new Vector3(0f,-.62f,.12f), new Vector3(.25f,1.26f,.25f),
                Quaternion.identity, true);
            Part("Dipper polished wear strip", stick, geometry.Box, steel,
                new Vector3(.145f,-.56f,.18f), new Vector3(.025f,.79f,.13f), Quaternion.identity);
            stickBaseAnchor = Root("Dipper ram base eye", boom);
            stickBaseAnchor.localPosition = new Vector3(.20f,.77f,.40f);
            stickMovingAnchor = Root("Dipper ram pinned eye", stick);
            stickMovingAnchor.localPosition = new Vector3(.10f,-.55f,.18f);
            stickRam = new EquipmentHydraulicLink(geometry, boom, steel, "Dipper cylinder", .085f, .61f);

            bucket = Root("Bucket heel / curling hinge", stick);
            bucket.localPosition = new Vector3(0f,-1.15f,.35f);
            Part("Curved excavator bucket shell", bucket,
                EquipmentGeometry.BucketShell(geometry, "Pressed curved bucket shell"), steel,
                Vector3.zero, Vector3.one, Quaternion.identity, true);
            var bucketDetails = new CityMeshBatch(geometry);
            bucketDetails.Box(paint, new Vector3(-.25f,.08f,-.12f), new Vector3(.14f,.18f,.13f));
            bucketDetails.Box(paint, new Vector3(.25f,.08f,-.12f), new Vector3(.14f,.18f,.13f));
            for (int tooth = 0; tooth < 5; tooth++)
                bucketDetails.Add(EquipmentGeometry.WedgeTooth(geometry, "Replaceable bucket tooth"),
                    steel, new Vector3(-.25f + tooth * .125f,-.16f,.40f),
                    new Vector3(.105f,.13f,.24f), Quaternion.identity);
            bucketDetails.Build("Replaceable beveled cutting teeth / heel cheeks", bucket, Vector3.zero);
            Transform bucketFill = Root("Scoopable concrete fragments", bucket);
            Vector3[] fragmentPositions =
            {
                new Vector3(-.15f,-.025f,.10f), new Vector3(-.05f,.005f,.17f),
                new Vector3(.06f,-.005f,.12f), new Vector3(.15f,.025f,.18f)
            };
            for (int i = 0; i < fragmentPositions.Length; i++)
                Part("Bucket load fragment " + i, bucketFill, geometry.BrokenConcrete, rubble,
                    fragmentPositions[i], new Vector3(.22f,.17f,.21f),
                    Quaternion.Euler(i * 13f, i * 37f, i * 7f));
            bucketPayload = bucketFill;
            bucketFill.gameObject.SetActive(false);
            bucketBaseAnchor = Root("Bucket curl ram base eye", stick);
            bucketBaseAnchor.localPosition = new Vector3(.13f,-.41f,.20f);
            bucketMovingAnchor = Root("Bucket curl ram pinned eye", bucket);
            bucketMovingAnchor.localPosition = new Vector3(.16f,.17f,-.15f);
            bucketRam = new EquipmentHydraulicLink(geometry, stick, steel, "Bucket curl cylinder", .075f, .58f);
            bucket.gameObject.name = "Bucket curl pivot • open bowl / five teeth";
        }

        private void BuildBulldozer()
        {
            bulldozer = VehicleRoot("Compact crawler dozer • hydraulically lifted blade");
            var body = new CityMeshBatch(geometry);
            body.Box(paint, new Vector3(0f,.63f,-.02f), new Vector3(1.04f,.36f,1.33f));
            body.Box(paint, new Vector3(0f,.83f,-.59f), new Vector3(.91f,.55f,.82f));
            body.Box(steel, new Vector3(0f,.72f,-1.02f), new Vector3(.82f,.10f,.12f));
            // Grille slots and stack behind the cab give the engine deck real depth.
            body.Box(dark, new Vector3(0f,.70f,-.165f), new Vector3(.75f,.17f,.018f));
            for (int i = 0; i < 8; i++)
                body.Box(steel, new Vector3(-.31f + i * .088f,.70f,-.145f),
                    new Vector3(.025f,.14f,.018f));
            body.Round(dark, new Vector3(.31f,1.24f,-.65f), new Vector3(.085f,.55f,.085f));
            body.Round(steel, new Vector3(.31f,1.52f,-.65f), new Vector3(.13f,.045f,.13f));

            Vector3 fl = new Vector3(-.31f,.94f,.38f);
            Vector3 fr = new Vector3(.31f,.94f,.38f);
            Vector3 tr = new Vector3(.31f,1.43f,.22f);
            Vector3 tl = new Vector3(-.31f,1.43f,.22f);
            Pane(body, "Dozer inclined front windscreen", fl, fr, tr, tl);
            Pane(body, "Dozer rear safety glass", new Vector3(-.31f,.94f,-.33f),
                new Vector3(.31f,.94f,-.33f), new Vector3(.31f,1.43f,-.35f),
                new Vector3(-.31f,1.43f,-.35f));
            Pane(body, "Dozer left side glass", new Vector3(-.31f,.94f,.38f),
                new Vector3(-.31f,.94f,-.33f), new Vector3(-.31f,1.43f,-.35f), tl);
            Pane(body, "Dozer right side glass", new Vector3(.31f,.94f,.38f),
                new Vector3(.31f,.94f,-.33f), new Vector3(.31f,1.43f,-.35f), tr);
            body.Beam(paint, new Vector3(-.37f,.92f,.41f), new Vector3(-.37f,1.49f,.20f), .06f);
            body.Beam(paint, new Vector3(.37f,.92f,.41f), new Vector3(.37f,1.49f,.20f), .06f);
            body.Beam(paint, new Vector3(-.36f,.92f,-.35f), new Vector3(-.36f,1.49f,-.37f), .05f);
            body.Beam(paint, new Vector3(.36f,.92f,-.35f), new Vector3(.36f,1.49f,-.37f), .05f);
            body.Beam(paint, tl, tr, .05f);
            body.Box(paint, new Vector3(0f,1.48f,-.04f), new Vector3(.84f,.11f,.88f));
            body.Build("Crawler chassis / framed cab / vented engine deck", bulldozer, Vector3.zero, true);
            bulldozerTracks = new EquipmentTrackRig(geometry, bulldozer, trackBeltMesh,
                rubber, steel, dark, VehicleScale, .58f, 1.95f, .44f, .34f);

            blade = Root("Angled lift blade yoke", bulldozer);
            blade.localPosition = new Vector3(0f,.44f,1.10f);
            var bladeParts = new CityMeshBatch(geometry);
            bladeParts.Add(EquipmentGeometry.CurvedBlade(geometry, "Rolled concave bulldozer blade"),
                paint, Vector3.zero, Vector3.one, Quaternion.identity);
            bladeParts.Box(steel, new Vector3(0f,-.34f,.18f), new Vector3(2.04f,.105f,.20f));
            bladeParts.Box(paint, new Vector3(-.94f,.02f,-.02f), new Vector3(.13f,.57f,.27f), -9f);
            bladeParts.Box(paint, new Vector3(.94f,.02f,-.02f), new Vector3(.13f,.57f,.27f), 9f);
            for (int bolt = 0; bolt < 8; bolt++)
                bladeParts.Add(geometry.Cylinder, steel,
                    new Vector3(-.83f + bolt * .237f,-.34f,.30f),
                    new Vector3(.04f,.028f,.04f), Quaternion.Euler(90f,0f,0f));
            bladeParts.Build("Rolled mouldboard / replaceable hardened cutting edge", blade, Vector3.zero, true);

            bladeBaseLeft = Root("Left blade lift cylinder base", bulldozer);
            bladeBaseLeft.localPosition = new Vector3(-.49f,.70f,.55f);
            bladeBaseRight = Root("Right blade lift cylinder base", bulldozer);
            bladeBaseRight.localPosition = new Vector3(.49f,.70f,.55f);
            bladeMovingLeft = Root("Left blade yoke ram eye", blade);
            bladeMovingLeft.localPosition = new Vector3(-.76f,.15f,-.11f);
            bladeMovingRight = Root("Right blade yoke ram eye", blade);
            bladeMovingRight.localPosition = new Vector3(.76f,.15f,-.11f);
            bladeLiftLeft = new EquipmentHydraulicLink(geometry, bulldozer, steel, "Left blade lift ram", .085f, .62f);
            bladeLiftRight = new EquipmentHydraulicLink(geometry, bulldozer, steel, "Right blade lift ram", .085f, .62f);
        }

        private void BuildTruck()
        {
            truck = VehicleRoot("Compact 4.13 m six-wheel tipper • cab and hydraulic body");
            var chassis = new CityMeshBatch(geometry);
            chassis.Box(dark, new Vector3(0f,.43f,-.25f), new Vector3(.92f,.18f,3.70f));
            chassis.Box(steel, new Vector3(0f,.52f,-.18f), new Vector3(.76f,.13f,1.38f));
            chassis.Box(paint, new Vector3(0f,.71f,.55f), new Vector3(.94f,.34f,.47f));
            // Framed cab panes slope back at the windscreen and remain distinct on every side.
            Pane(chassis, "Tipper sloped windscreen", new Vector3(-.44f,.79f,1.48f),
                new Vector3(.44f,.79f,1.48f), new Vector3(.40f,1.31f,1.24f),
                new Vector3(-.40f,1.31f,1.24f));
            Pane(chassis, "Tipper rear cab pane", new Vector3(-.40f,.79f,.64f),
                new Vector3(.40f,.79f,.64f), new Vector3(.40f,1.31f,.67f),
                new Vector3(-.40f,1.31f,.67f));
            Pane(chassis, "Tipper left door glazing", new Vector3(-.46f,.80f,1.46f),
                new Vector3(-.46f,.80f,.66f), new Vector3(-.43f,1.30f,.68f),
                new Vector3(-.41f,1.30f,1.24f));
            Pane(chassis, "Tipper right door glazing", new Vector3(.46f,.80f,1.46f),
                new Vector3(.46f,.80f,.66f), new Vector3(.43f,1.30f,.68f),
                new Vector3(.41f,1.30f,1.24f));
            chassis.Beam(paint, new Vector3(-.49f,.77f,1.49f), new Vector3(-.44f,1.36f,1.21f), .06f);
            chassis.Beam(paint, new Vector3(.49f,.77f,1.49f), new Vector3(.44f,1.36f,1.21f), .06f);
            chassis.Beam(paint, new Vector3(-.48f,.77f,.63f), new Vector3(-.46f,1.36f,.64f), .05f);
            chassis.Beam(paint, new Vector3(.48f,.77f,.63f), new Vector3(.46f,1.36f,.64f), .05f);
            chassis.Beam(paint, new Vector3(-.45f,1.34f,1.21f), new Vector3(.45f,1.34f,1.21f), .055f);
            chassis.Box(paint, new Vector3(0f,1.37f,.98f), new Vector3(.99f,.12f,.91f));
            chassis.Box(steel, new Vector3(0f,.42f,1.52f), new Vector3(1.10f,.12f,.12f));
            chassis.Box(dark, new Vector3(0f,.66f,1.49f), new Vector3(.28f,.24f,.07f));
            for (int side = -1; side <= 1; side += 2)
            {
                chassis.Beam(steel, new Vector3(side * .45f,1.20f,1.14f),
                    new Vector3(side * .68f,1.24f,1.14f), .045f);
                chassis.Box(paint, new Vector3(side * .31f,.72f,1.48f), new Vector3(.16f,.09f,.04f));
                chassis.Box(steel, new Vector3(side * .29f,.79f,1.49f), new Vector3(.13f,.035f,.025f));
                chassis.Box(dark, new Vector3(side * .475f,.66f,1.02f), new Vector3(.035f,.20f,.50f));
            }
            chassis.Build("Cab frame / safety glass / mirrors / bumper", truck, Vector3.zero, true);

            wheels = new Transform[6];
            for (int axle = 0; axle < 3; axle++)
                for (int side = 0; side < 2; side++)
                {
                    int index = axle * 2 + side;
                    float x = side == 0 ? -.61f : .61f;
                    float z = axle == 0 ? 1.02f : axle == 1 ? -.78f : -1.61f;
                    wheels[index] = Root("Axle " + (axle + 1) + " rolling tyre " + (side == 0 ? "left" : "right"), truck);
                    wheels[index].localPosition = new Vector3(x,.32f,z);
                    var wheel = new CityMeshBatch(geometry);
                    wheel.Add(tireTreadMesh, rubber, Vector3.zero, new Vector3(.68f,.34f,.68f),
                        Quaternion.Euler(0f,0f,90f));
                    wheel.Add(geometry.Cylinder, dark, Vector3.zero,
                        new Vector3(.34f,.35f,.34f), Quaternion.Euler(0f,0f,90f));
                    wheel.Add(geometry.Cylinder, steel,
                        new Vector3(side == 0 ? -.185f : .185f,0f,0f),
                        new Vector3(.22f,.035f,.22f), Quaternion.Euler(0f,0f,90f));
                    for (int bolt = 0; bolt < 8; bolt++)
                    {
                        float a = bolt * Mathf.PI * .25f;
                        wheel.Add(geometry.Cylinder, steel,
                            new Vector3((side == 0 ? -.207f : .207f),
                                Mathf.Sin(a) * .13f, Mathf.Cos(a) * .13f),
                            new Vector3(.035f,.024f,.035f), Quaternion.Euler(0f,0f,90f));
                    }
                    wheel.Build("Ribbed tyre / steel hub / wheel studs", wheels[index], Vector3.zero);
                }

            truckBed = Root("Rear-hinged dump bed", truck);
            truckBed.localPosition = new Vector3(0f,.55f,-2.55f);
            var bed = new CityMeshBatch(geometry);
            bed.Box(dark, new Vector3(0f,.105f,1.15f), new Vector3(1.18f,.035f,2.18f));
            bed.Box(paint, new Vector3(0f,.06f,1.15f), new Vector3(1.30f,.14f,2.34f));
            bed.Box(paint, new Vector3(0f,.35f,.05f), new Vector3(1.28f,.57f,.12f));
            bed.Box(paint, new Vector3(0f,.35f,2.24f), new Vector3(1.28f,.57f,.12f));
            for (int side = -1; side <= 1; side += 2)
            {
                bed.Box(paint, new Vector3(side * .61f,.35f,1.15f), new Vector3(.13f,.57f,2.30f));
                for (int rib = 0; rib < 6; rib++)
                    bed.Box(steel, new Vector3(side * .685f,.35f,.12f + rib * .40f),
                        new Vector3(.028f,.48f,.045f));
                bed.Beam(steel, new Vector3(side * .66f,.67f,.05f),
                    new Vector3(side * .66f,.67f,2.22f), .035f);
            }
            bed.Box(steel, new Vector3(0f,.34f,-.085f), new Vector3(.40f,.07f,.035f));
            bed.Build("Reinforced steel subframe / lined tipper box", truckBed, Vector3.zero, true);
            cargo = Root("Discrete carried rubble fragments", truckBed);
            cargoPieces = new Transform[9];
            for (int i = 0; i < cargoPieces.Length; i++)
            {
                int row = i / 3;
                int column = i % 3;
                cargoPieces[i] = Part("Visible irregular rubble load " + (i + 1), cargo,
                    geometry.BrokenConcrete, rubble,
                    new Vector3((column - 1) * .28f, .24f + row * .17f, .22f + row * .52f),
                    new Vector3(.39f,.26f,.41f), Quaternion.Euler(i * 9f, i * 41f, i * 7f));
                cargoPieces[i].gameObject.SetActive(false);
            }
            bedBaseLeft = Root("Left tip ram chassis eye", truck);
            bedBaseLeft.localPosition = new Vector3(-.40f,.52f,-1.25f);
            bedBaseRight = Root("Right tip ram chassis eye", truck);
            bedBaseRight.localPosition = new Vector3(.40f,.52f,-1.25f);
            bedMovingLeft = Root("Left tip ram bed eye", truckBed);
            bedMovingLeft.localPosition = new Vector3(-.40f,.18f,1.80f);
            bedMovingRight = Root("Right tip ram bed eye", truckBed);
            bedMovingRight.localPosition = new Vector3(.40f,.18f,1.80f);
            bedLiftLeft = new EquipmentHydraulicLink(geometry, truck, steel, "Left tipping cylinder", .075f, .60f);
            bedLiftRight = new EquipmentHydraulicLink(geometry, truck, steel, "Right tipping cylinder", .075f, .60f);
            cargo.gameObject.SetActive(false);
        }

        private void CreateRoute()
        {
            if (importedJob)
            {
                route[0] = roadsConfigured
                    ? depot + VehicleOffset(new Vector3(0f,depotTruckRootOffsetModel,0f))
                    : depot + VehicleOffset(new Vector3(-1.4f,depotTruckRootOffsetModel,-4.5f));
                route[1] = depot + VehicleOffset(new Vector3(-1.4f,depotTruckRootOffsetModel,4.5f));
                route[2] = route[0];
                routeCount = 3;
            }
            else
            {
                // Direct illustrative dispatch between sourced work and depot coordinates.
                // No implied street alignment or invented rectilinear road network.
                route[0] = roadsConfigured
                    ? depot + VehicleOffset(new Vector3(0f,depotTruckRootOffsetModel,0f))
                    : depot + VehicleOffset(new Vector3(-1.4f,depotTruckRootOffsetModel,-4.5f));
                route[1] = new Vector3(jobCenter.x,jobCenter.y +
                    VehicleOffset(new Vector3(0f,EquipmentMotion.TruckWorkRootHeightModel,0f)).y,
                    jobCenter.z);
                route[2] = route[0];
                routeCount = 3;
            }
            Vector3 work = importedJob
                ? depot + VehicleOffset(new Vector3(.3f,EquipmentMotion.WorkRootHeightModel,5.6f))
                : jobCenter + VehicleOffset(new Vector3(0f,EquipmentMotion.WorkRootHeightModel,0f));
            tripRoute[0] = work + VehicleOffset(new Vector3(-2.5f,
                EquipmentMotion.TruckWorkRootHeightModel -
                    EquipmentMotion.WorkRootHeightModel,.35f));
            tripRoute[1] = route[0];
            roadTripRoute = roadsConfigured
                ? roadNetwork.FindRoute(tripRoute[0], tripRoute[1], roadSpeedMultiplier)
                : null;
            tripRouteLength = roadsConfigured
                ? (roadTripRoute == null ? 0f : roadTripRoute.Length)
                : Vector3.Distance(tripRoute[0], tripRoute[1]);
            if (roadsConfigured)
                truckRouteStatus = roadTripRoute == null
                    ? "تعذّر الوصول: لا يوجد اتصال بين الطريق والعمل والمستودع ضمن مسافة ١٠٠ متر. ستبقى الشاحنة في مكانها."
                    : "المسار عبر الشوارع جاهز للشاحنة.";
        }

        private void CaptureTransitionPose()
        {
            rootTransitionSkipped[0] = rootTransitionSkipped[1] = false;
            for (int i = 0; i < transitionParts.Length; i++)
            {
                transitionPositions[i] = transitionParts[i].localPosition;
                transitionRotations[i] = transitionParts[i].localRotation;
            }
        }

        private void ApplyTransitionPose(float blend)
        {
            for (int i = 0; i < transitionParts.Length; i++)
            {
                bool vehicleRoot = i == 0 || i == 5;
                if (vehicleRoot && roadsConfigured) continue;
                if (vehicleRoot &&
                    Vector3.Distance(transitionPositions[i], transitionParts[i].localPosition) > .3f)
                {
                    int fadeIndex = i == 0 ? 0 : 1;
                    if (!rootTransitionSkipped[fadeIndex])
                    {
                        rootTransitionSkipped[fadeIndex] = true;
                        if (!vehicleFadePending[fadeIndex])
                        {
                            vehicleFadePending[fadeIndex] = true;
                            vehicleFadeClock[fadeIndex] = 0f;
                            transitionParts[i].gameObject.SetActive(false);
                        }
                    }
                    continue;
                }
                if (blend >= 1f) continue;
                transitionParts[i].localPosition = Vector3.Lerp(transitionPositions[i],
                    transitionParts[i].localPosition, blend);
                transitionParts[i].localRotation = Quaternion.Slerp(transitionRotations[i],
                    transitionParts[i].localRotation, blend);
            }
        }

        private bool IsTruckTripActive()
        {
            return truckTripState == TruckTripState.Outbound ||
                truckTripState == TruckTripState.TurningAtDepot ||
                truckTripState == TruckTripState.Unloading ||
                truckTripState == TruckTripState.Inbound ||
                truckTripState == TruckTripState.TurningOutFromWork ||
                truckTripState == TruckTripState.TurningInAtWork ||
                truckTripState == TruckTripState.ReassignFade;
        }

        private void BeginTruckReassignment(bool toWork)
        {
            truckReassignToWork = toWork;
            if (roadsConfigured)
            {
                Vector3 work = importedJob
                    ? depot + VehicleOffset(new Vector3(.3f,EquipmentMotion.WorkRootHeightModel,5.6f))
                    : jobCenter + VehicleOffset(new Vector3(0f,EquipmentMotion.WorkRootHeightModel,0f));
                Vector3 workDock = work + VehicleOffset(new Vector3(-2.5f,
                    EquipmentMotion.TruckWorkRootHeightModel -
                        EquipmentMotion.WorkRootHeightModel,.35f));
                Vector3 depotDock = depot + VehicleOffset(new Vector3(0f,depotTruckRootOffsetModel,0f));
                Vector3 current = truck.localPosition;
                if (toWork)
                {
                    tripRoute[0] = workDock;
                    tripRoute[1] = current;
                    roadTripRoute = roadNetwork.FindRoute(tripRoute[0], tripRoute[1],
                        roadSpeedMultiplier);
                }
                else
                {
                    tripRoute[0] = current;
                    tripRoute[1] = depotDock;
                    roadTripRoute = roadNetwork.FindRoute(tripRoute[0], tripRoute[1],
                        roadSpeedMultiplier);
                }
                if (roadTripRoute == null)
                {
                    roadReassignTravel = false;
                    truckTripState = toWork ? TruckTripState.ParkedAtDepot :
                        TruckTripState.ParkedAtWork;
                    truckRouteStatus = "تعذّر تغيير موقع الشاحنة عبر شبكة الشوارع؛ ستبقى في مكانها.";
                    return;
                }
                roadReassignTravel = true;
                tripRouteLength = roadTripRoute.Length;
                tripDistance = tripClock = 0f;
                truckTripState = toWork ? TruckTripState.Inbound : TruckTripState.Outbound;
                truckRouteStatus = "الشاحنة تعيد التموضع عبر شبكة الشوارع.";
                return;
            }
            truckTripState = TruckTripState.ReassignFade;
            tripClock = 0f;
            if (truck != null) truck.gameObject.SetActive(false);
        }

        private void FinishTruckReassignment()
        {
            bool keepLoadedBucket = excavatorWaitingForTruck &&
                excavatorHoldPhase < EquipmentMotion.DigCycleSeconds * .70f;
            pendingRouteRebuild = false;
            CreateRoute();
            transferredCargoPieces = 0;
            if (truckReassignToWork && truckOwned)
            {
                truck.localPosition = tripRoute[0];
                truck.localRotation = Quaternion.identity;
                truckTripState = TruckTripState.ParkedAtWork;
                excavatorWaitingForTruck = keepLoadedBucket;
                phase = keepLoadedBucket ? excavatorHoldPhase : 0f;
            }
            else
            {
                truck.localPosition = tripRoute[1];
                truck.localRotation = Quaternion.LookRotation(
                    tripRoute[0] - tripRoute[1], Vector3.up);
                truckTripState = TruckTripState.ParkedAtDepot;
            }
            truckBed.localRotation = Quaternion.identity;
            truck.gameObject.SetActive(truckOwned);
        }

        private void BeginOutbound()
        {
            if (roadsConfigured && roadTripRoute == null)
            {
                truckRouteStatus = "تعذّر المسار: الشاحنة متوقفة عند موقع العمل ولا تسلك طريقًا مباشرًا عبر الأحياء.";
                return;
            }
            tripDistance = 0f;
            tripClock = 0f;
            truckTripState = TruckTripState.Outbound;
            truck.localPosition = tripRoute[0];
            Vector3 direction = roadsConfigured && roadTripRoute != null
                ? roadTripRoute.DirectionAtDistance(0f) : tripRoute[1] - tripRoute[0];
            truck.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            truckBed.localRotation = Quaternion.identity;
        }

        private void BeginInbound()
        {
            if (roadsConfigured && roadTripRoute == null)
            {
                truckTripState = TruckTripState.ParkedAtDepot;
                truckRouteStatus = "تعذّر المسار: الشاحنة متوقفة في المستودع ولا تسلك طريقًا مباشرًا عبر الأحياء.";
                return;
            }
            tripDistance = 0f;
            tripClock = 0f;
            truckTripState = TruckTripState.Inbound;
            truck.localPosition = tripRoute[1];
            Vector3 direction = roadsConfigured && roadTripRoute != null
                ? roadTripRoute.DirectionAtDistance(tripRouteLength) * -1f
                : tripRoute[0] - tripRoute[1];
            truck.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            truckBed.localRotation = Quaternion.identity;
        }

        private void BeginTruckTurn(bool atDepot)
        {
            tripClock = 0f;
            tripTurnStart = truck.localRotation;
            Vector3 direction;
            if (roadsConfigured && roadTripRoute != null)
            {
                direction = atDepot
                    ? roadTripRoute.DirectionAtDistance(tripRouteLength) * -1f
                    : roadTripRoute.DirectionAtDistance(0f);
            }
            else
                direction = atDepot ? tripRoute[0] - tripRoute[1] : Vector3.forward;
            tripTurnTarget = atDepot
                ? Quaternion.LookRotation(direction, Vector3.up)
                : Quaternion.identity;
            truckTripState = atDepot
                ? TruckTripState.TurningAtDepot
                : TruckTripState.TurningInAtWork;
        }

        private void BeginWorkDeparture()
        {
            tripClock = 0f;
            tripTurnStart = truck.localRotation;
            Vector3 direction = roadsConfigured && roadTripRoute != null
                ? roadTripRoute.DirectionAtDistance(0f)
                : tripRoute[1] - tripRoute[0];
            tripTurnTarget = Quaternion.LookRotation(direction, Vector3.up);
            truckTripState = TruckTripState.TurningOutFromWork;
        }

        private void PlaceTruckOnTripRoute(bool returning)
        {
            float distance = Mathf.Clamp(tripDistance, 0f, tripRouteLength);
            Vector3 position, direction;
            if (roadsConfigured && roadTripRoute != null)
            {
                float routeDistance = returning ? tripRouteLength - distance : distance;
                position = roadTripRoute.PositionAtDistance(routeDistance);
                direction = roadTripRoute.DirectionAtDistance(routeDistance);
                if (returning) direction = direction * -1f;
                position.y = returning ? tripRoute[1].y : tripRoute[0].y;
            }
            else
            {
                position = returning
                    ? Vector3.Lerp(tripRoute[1], tripRoute[0], distance /
                        Mathf.Max(.001f, tripRouteLength))
                    : Vector3.Lerp(tripRoute[0], tripRoute[1], distance /
                        Mathf.Max(.001f, tripRouteLength));
                direction = returning ? tripRoute[0] - tripRoute[1] : tripRoute[1] - tripRoute[0];
            }
            truck.localPosition = position;
            if (direction.sqrMagnitude > .001f)
                truck.localRotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private float TruckSpeedAtTripDistance(bool returning)
        {
            float speed = HaulingSpeed;
            if (!roadsConfigured || roadTripRoute == null) return speed;
            float routeDistance = returning ? tripRouteLength - tripDistance : tripDistance;
            string roadId = roadTripRoute.RoadIdAtDistance(routeDistance);
            if (!string.IsNullOrEmpty(roadId) && roadSpeedMultiplier != null)
                speed *= roadSpeedMultiplier(roadId);
            return speed;
        }

        private void UpdateTrackedRoadTravel(float dt, bool clearing)
        {
            if (!roadsConfigured) return;
            Vector3 work = importedJob
                ? depot + VehicleOffset(new Vector3(.3f,EquipmentMotion.WorkRootHeightModel,5.6f))
                : jobCenter + VehicleOffset(new Vector3(0f,EquipmentMotion.WorkRootHeightModel,0f));
            Vector3 excavatorTarget = clearing ? work :
                depot + VehicleOffset(new Vector3(0f,depotExcavatorRootOffsetModel,0f));
            Vector3 dozerTarget = clearing
                ? work + VehicleOffset(new Vector3(1.55f,dozerWorkRootOffsetModel,-.48f))
                : depot + VehicleOffset(new Vector3(0f,depotDozerRootOffsetModel,0f));
            bool destinationChanged = !trackedDestinationInitialized ||
                trackedDestinationClearing != clearing ||
                Vector3.Distance(lastExcavatorRoadTarget, excavatorTarget) > .01f ||
                Vector3.Distance(lastBulldozerRoadTarget, dozerTarget) > .01f;
            if (destinationChanged)
            {
                trackedDestinationInitialized = true;
                trackedDestinationClearing = clearing;
                lastExcavatorRoadTarget = excavatorTarget;
                lastBulldozerRoadTarget = dozerTarget;
                if (excavatorOwned)
                    BeginTrackedRoadTrip(excavator, excavatorTarget, true);
                if (bulldozerOwned)
                    BeginTrackedRoadTrip(bulldozer, dozerTarget, false);
            }
            if (excavatorOnRoad) AdvanceTrackedRoadTrip(excavator, true, dt);
            if (bulldozerOnRoad) AdvanceTrackedRoadTrip(bulldozer, false, dt);
            if (!excavatorOwned) excavatorDockedAtWork = false;
            if (!bulldozerOwned) bulldozerDockedAtWork = false;
        }

        private void BeginTrackedRoadTrip(Transform vehicle, Vector3 destination, bool excavatorVehicle)
        {
            Vector3 start = vehicle.localPosition;
            if (Vector3.Distance(start, destination) <= .03f)
            {
                vehicle.localPosition = destination;
                SetTrackedDocked(excavatorVehicle, trackedDestinationClearing);
                return;
            }
            CityRoadRoute route = roadNetwork.FindRoute(start, destination, roadSpeedMultiplier);
            if (route == null)
            {
                if (excavatorVehicle) excavatorOnRoad = false;
                else bulldozerOnRoad = false;
                SetTrackedDocked(excavatorVehicle, false);
                if (excavatorVehicle)
                    excavatorRouteStatus = "تعذّر وصول الحفارة إلى الطريق؛ ستبقى عند موقعها دون اختصار عبر المباني.";
                else
                    bulldozerRouteStatus = "تعذّر وصول الجرّافة إلى الطريق؛ ستبقى عند موقعها دون اختصار عبر المباني.";
                return;
            }
            if (excavatorVehicle)
            {
                excavatorRouteStatus = null;
                excavatorRoadRoute = route;
                excavatorRoadDistance = 0f;
                excavatorRoadTarget = destination;
                excavatorOnRoad = true;
                excavatorDockedAtWork = false;
            }
            else
            {
                bulldozerRouteStatus = null;
                bulldozerRoadRoute = route;
                bulldozerRoadDistance = 0f;
                bulldozerRoadTarget = destination;
                bulldozerOnRoad = true;
                bulldozerDockedAtWork = false;
            }
        }

        private void AdvanceTrackedRoadTrip(Transform vehicle, bool excavatorVehicle, float dt)
        {
            CityRoadRoute route = excavatorVehicle ? excavatorRoadRoute : bulldozerRoadRoute;
            if (route == null) return;
            float distance = excavatorVehicle ? excavatorRoadDistance : bulldozerRoadDistance;
            float speed = .10f;
            string roadId = route.RoadIdAtDistance(distance);
            if (!string.IsNullOrEmpty(roadId) && roadSpeedMultiplier != null)
                speed *= roadSpeedMultiplier(roadId);
            distance = Mathf.Min(route.Length, distance + speed * dt);
            Vector3 position = route.PositionAtDistance(distance);
            position.y = (excavatorVehicle ? excavatorRoadTarget : bulldozerRoadTarget).y;
            vehicle.localPosition = position;
            Vector3 direction = route.DirectionAtDistance(distance);
            if (direction.sqrMagnitude > .001f)
                vehicle.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            if (excavatorVehicle) excavatorRoadDistance = distance;
            else bulldozerRoadDistance = distance;
            if (distance + .0001f < route.Length) return;
            vehicle.localPosition = excavatorVehicle ? excavatorRoadTarget : bulldozerRoadTarget;
            if (excavatorVehicle) excavatorOnRoad = false;
            else bulldozerOnRoad = false;
            SetTrackedDocked(excavatorVehicle, trackedDestinationClearing);
        }

        private void SetTrackedDocked(bool excavatorVehicle, bool atWork)
        {
            if (excavatorVehicle) excavatorDockedAtWork = atWork;
            else bulldozerDockedAtWork = atWork;
        }

        private void UpdateTruckTrip(float dt, bool clearing)
        {
            truckTravelAdvancedThisFrame = false;
            if (!truckOwned) return;
            switch (truckTripState)
            {
                case TruckTripState.ParkedAtDepot:
                    truck.localPosition = tripRoute[1];
                    truckBed.localRotation = Quaternion.identity;
                    if (clearing) BeginInbound();
                    break;
                case TruckTripState.ParkedAtWork:
                    truck.localPosition = tripRoute[0];
                    truck.localRotation = Quaternion.identity;
                    truckBed.localRotation = Quaternion.identity;
                    if (!clearing) BeginWorkDeparture();
                    break;
                case TruckTripState.TurningOutFromWork:
                    tripClock += dt;
                    truck.localPosition = tripRoute[0];
                    truck.localRotation = Quaternion.Slerp(tripTurnStart,
                        tripTurnTarget, Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(tripClock / .75f)));
                    if (tripClock >= .75f) BeginOutbound();
                    break;
                case TruckTripState.Outbound:
                    truckTravelAdvancedThisFrame = dt > 0f &&
                        tripDistance < tripRouteLength;
                    tripDistance += TruckSpeedAtTripDistance(false) * dt;
                    if (tripDistance >= tripRouteLength)
                    {
                        tripDistance = tripRouteLength;
                        PlaceTruckOnTripRoute(false);
                        if (roadReassignTravel)
                        {
                            roadReassignTravel = false;
                            CreateRoute();
                        }
                        BeginTruckTurn(true);
                    }
                    else PlaceTruckOnTripRoute(false);
                    break;
                case TruckTripState.TurningAtDepot:
                    tripClock += dt;
                    truck.localPosition = tripRoute[1];
                    truck.localRotation = Quaternion.Slerp(tripTurnStart,
                        tripTurnTarget, Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(tripClock / .75f)));
                    if (tripClock >= .75f)
                    {
                        if (transferredCargoPieces > 0)
                        {
                            truckTripState = TruckTripState.Unloading;
                            tripClock = 0f;
                        }
                        else if (roadReassignTravel)
                        {
                            roadReassignTravel = false;
                            CreateRoute();
                            if (clearing) BeginInbound();
                            else truckTripState = TruckTripState.ParkedAtDepot;
                        }
                        else if (pendingRouteRebuild)
                            BeginTruckReassignment(stage == JobStage.Clearing);
                        else if (clearing)
                            BeginInbound();
                        else
                            truckTripState = TruckTripState.ParkedAtDepot;
                    }
                    break;
                case TruckTripState.Unloading:
                    tripClock += dt;
                    truck.localPosition = tripRoute[1];
                    truckBed.localRotation = Quaternion.Euler(
                        -38f * EquipmentMotion.Unload(tripClock), 0f, 0f);
                    if (tripClock >= 7.2f)
                    {
                        transferredCargoPieces = 0;
                        truckBed.localRotation = Quaternion.identity;
                        if (roadReassignTravel)
                        {
                            roadReassignTravel = false;
                            CreateRoute();
                            if (clearing) BeginInbound();
                            else truckTripState = TruckTripState.ParkedAtDepot;
                        }
                        else if (pendingRouteRebuild)
                            BeginTruckReassignment(stage == JobStage.Clearing);
                        else if (clearing)
                            BeginInbound();
                        else
                            truckTripState = TruckTripState.ParkedAtDepot;
                    }
                    break;
                case TruckTripState.Inbound:
                    truckTravelAdvancedThisFrame = dt > 0f &&
                        tripDistance < tripRouteLength;
                    tripDistance += TruckSpeedAtTripDistance(true) * dt;
                    if (tripDistance >= tripRouteLength)
                    {
                        tripDistance = tripRouteLength;
                        PlaceTruckOnTripRoute(true);
                        if (roadReassignTravel)
                        {
                            roadReassignTravel = false;
                            CreateRoute();
                        }
                        BeginTruckTurn(false);
                    }
                    else PlaceTruckOnTripRoute(true);
                    break;
                case TruckTripState.TurningInAtWork:
                    tripClock += dt;
                    truck.localPosition = tripRoute[0];
                    truck.localRotation = Quaternion.Slerp(tripTurnStart,
                        tripTurnTarget, Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(tripClock / .75f)));
                    if (tripClock >= .75f)
                    {
                        if (roadReassignTravel)
                        {
                            roadReassignTravel = false;
                            CreateRoute();
                            if (clearing)
                            {
                                truckTripState = TruckTripState.ParkedAtWork;
                                excavatorWaitingForTruck = false;
                                excavatorHoldPhase = 0f;
                            }
                            else BeginWorkDeparture();
                        }
                        else if (pendingRouteRebuild)
                            BeginTruckReassignment(stage == JobStage.Clearing);
                        else if (clearing)
                        {
                            truckTripState = TruckTripState.ParkedAtWork;
                            excavatorWaitingForTruck = false;
                            excavatorHoldPhase = 0f;
                            phase = 0f;
                        }
                        else BeginWorkDeparture();
                    }
                    break;
                case TruckTripState.ReassignFade:
                    tripClock += dt;
                    if (tripClock >= VehicleRepositionFadeSeconds)
                        FinishTruckReassignment();
                    break;
            }
        }

        private void Update()
        {
            if (geometry == null) return;
            float dt = Time.deltaTime;
            transitionClock += dt;
            float transition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transitionClock / 1.15f));
            bool clearing = stage == JobStage.Clearing;
            UpdateTrackedRoadTravel(dt, clearing);
            bool trackedCrewAtWork = !roadsConfigured ||
                ((!excavatorOwned || excavatorDockedAtWork) &&
                 (!bulldozerOwned || bulldozerDockedAtWork) &&
                 (excavatorOwned || bulldozerOwned));
            if (clearing && truckOwned)
            {
                if (excavatorWaitingForTruck)
                {
                    if (truckTripState == TruckTripState.ParkedAtWork &&
                        excavatorHoldPhase < EquipmentMotion.DigCycleSeconds * .70f)
                    {
                        excavatorWaitingForTruck = false;
                        phase = excavatorHoldPhase;
                    }
                    else phase = excavatorHoldPhase;
                }
                else if (truckTripState == TruckTripState.ParkedAtWork && trackedCrewAtWork)
                    phase += dt;
                else
                    phase = 0f;
            }
            else if (clearing && !truckOwned)
            {
                if (excavatorWaitingForTruck) phase = excavatorHoldPhase;
                else if (trackedCrewAtWork) phase += dt;
                else phase = 0f;
            }
            else phase += dt;
            Vector3 work = importedJob
                ? depot + VehicleOffset(new Vector3(.3f,EquipmentMotion.WorkRootHeightModel,5.6f))
                : jobCenter + VehicleOffset(new Vector3(0f,EquipmentMotion.WorkRootHeightModel,0f));
            EquipmentMotion.ExcavationPose digPose = EquipmentMotion.Dig(phase);

            if (excavatorOwned)
            {
                if (clearing)
                {
                    if (!roadsConfigured || excavatorDockedAtWork)
                    {
                        SetPose(excavator, work, Vector3.zero);
                        excavator.localRotation = Quaternion.identity;
                        turret.localRotation = Quaternion.Euler(0f, digPose.turretYaw, 0f);
                        boom.localRotation = Quaternion.Euler(digPose.boomAngle, 0f, 0f);
                        stick.localRotation = Quaternion.Euler(digPose.stickAngle, 0f, 0f);
                        bucket.localRotation = Quaternion.Euler(digPose.bucketAngle, 0f, 0f);
                    }
                }
                else
                {
                    if (!roadsConfigured)
                        SetPose(excavator, depot,
                            new Vector3(0f,depotExcavatorRootOffsetModel,5.8f));
                    if (!excavatorOnRoad) excavator.localRotation = Quaternion.Euler(0f,180f,0f);
                    turret.localRotation = Quaternion.identity;
                    boom.localRotation = Quaternion.Euler(15f,0f,0f);
                    stick.localRotation = Quaternion.Euler(-62f,0f,0f);
                    bucket.localRotation = Quaternion.Euler(24f,0f,0f);
                }
            }

            if (bulldozerOwned)
            {
                if (clearing)
                {
                    if (!roadsConfigured || bulldozerDockedAtWork)
                    {
                        float push = Mathf.Sin(Mathf.Repeat(phase, 11f) / 11f * Mathf.PI * 2f) * .22f;
                        SetPose(bulldozer, work,
                            new Vector3(1.55f,dozerWorkRootOffsetModel,-.48f + push));
                        bulldozer.localRotation = Quaternion.identity;
                        blade.localRotation = Quaternion.Euler(EquipmentMotion.PushBlade(phase), 0f, 0f);
                    }
                }
                else
                {
                    if (!roadsConfigured)
                        SetPose(bulldozer, depot,
                            new Vector3(0f,depotDozerRootOffsetModel,-5.1f));
                    if (!bulldozerOnRoad) bulldozer.localRotation = Quaternion.Euler(0f,180f,0f);
                    blade.localRotation = Quaternion.Euler(-7f,0f,0f);
                }
            }

            float cycle = Mathf.Repeat(phase, EquipmentMotion.DigCycleSeconds) /
                EquipmentMotion.DigCycleSeconds;
            if (truckOwned)
            {
                if (clearing && !excavatorWaitingForTruck &&
                    truckTripState == TruckTripState.ParkedAtWork && cycle >= .72f)
                {
                    transferredCargoPieces = Mathf.Min(cargoPieces.Length,
                        transferredCargoPieces + 3);
                    if (transferredCargoPieces > 0)
                    {
                        excavatorWaitingForTruck = true;
                        excavatorHoldPhase = EquipmentMotion.DigCycleSeconds * .80f;
                        phase = excavatorHoldPhase;
                        BeginWorkDeparture();
                    }
                }
                UpdateTruckTrip(dt, clearing);
            }
            else
            {
                if (clearing && !excavatorWaitingForTruck && cycle >= .48f)
                {
                    excavatorWaitingForTruck = true;
                    excavatorHoldPhase = EquipmentMotion.DigCycleSeconds * .48f;
                    phase = excavatorHoldPhase;
                }
                truckTripState = TruckTripState.ParkedAtDepot;
                truckTravelAdvancedThisFrame = false;
                cargo.gameObject.SetActive(false);
            }

            ApplyTransitionPose(transition);
            UpdateVehicleFades(dt);
            excavatorTracks.Update();
            bulldozerTracks.Update();
            cargo.gameObject.SetActive(truckOwned && transferredCargoPieces > 0);
            SetCargoVisiblePieces(truckTripState == TruckTripState.Unloading
                ? EquipmentMotion.VisibleCargoPieces(1f - EquipmentMotion.DumpedAmount(tripClock),
                    transferredCargoPieces)
                : transferredCargoPieces);

            boomLift.Follow(boomBaseAnchor, boomMovingAnchor);
            stickRam.Follow(stickBaseAnchor, stickMovingAnchor);
            bucketRam.Follow(bucketBaseAnchor, bucketMovingAnchor);
            bladeLiftLeft.Follow(bladeBaseLeft, bladeMovingLeft);
            bladeLiftRight.Follow(bladeBaseRight, bladeMovingRight);
            bedLiftLeft.Follow(bedBaseLeft, bedMovingLeft);
            bedLiftRight.Follow(bedBaseRight, bedMovingRight);
            if (clearing && excavatorOwned)
                bucketPayload.gameObject.SetActive(digPose.bucketLoad > .45f);
            else
                bucketPayload.gameObject.SetActive(false);

            UpdateWheelRoll(dt);
            UpdateAudioIntensities(dt, digPose);
        }

        private void UpdateVehicleFades(float dt)
        {
            UpdateVehicleFade(0, excavator, excavatorOwned, dt);
            UpdateVehicleFade(1, bulldozer, bulldozerOwned, dt);
        }

        private void UpdateVehicleFade(int index, Transform root, bool owned, float dt)
        {
            if (!vehicleFadePending[index]) return;
            vehicleFadeClock[index] += dt;
            if (vehicleFadeClock[index] < VehicleRepositionFadeSeconds) return;
            vehicleFadePending[index] = false;
            root.gameObject.SetActive(owned);
        }

        private void SetCargoVisiblePieces(int count)
        {
            if (cargoPieces == null) return;
            for (int i = 0; i < cargoPieces.Length; i++)
                cargoPieces[i].gameObject.SetActive(i < count);
        }

        private void UpdateWheelRoll(float dt)
        {
            if (!truckOwned)
            {
                LastMeasuredTruckWorldSpeed = 0f;
                return;
            }
            Vector3 current = truck.localPosition;
            Vector3 worldPosition = truck.position;
            float measuredDistance = Vector3.Distance(current, previousWheelPosition);
            float worldDistance = hasTruckWorldSpeedSample
                ? Vector3.Distance(worldPosition, previousMeasuredTruckWorldPosition) : 0f;
            LastMeasuredTruckWorldSpeed = truckTravelAdvancedThisFrame && dt > .0001f
                ? worldDistance / dt : 0f;
            if (haveMotionSample && truckTravelAdvancedThisFrame && dt > .0001f)
            {
                Vector3 displacement = current - previousWheelPosition;
                float sign = Vector3.Dot(displacement, truck.localRotation * Vector3.forward) >= 0f ? 1f : -1f;
                wheelRoll += sign * measuredDistance / (.34f * VehicleScale) * Mathf.Rad2Deg;
                foreach (Transform wheel in wheels)
                    wheel.localRotation = Quaternion.Euler(wheelRoll, 0f, 0f);
            }
            previousWheelPosition = current;
            previousMeasuredTruckWorldPosition = worldPosition;
            hasTruckWorldSpeedSample = true;
        }

        private void UpdateAudioIntensities(float dt, EquipmentMotion.ExcavationPose digPose)
        {
            if (dt <= .0001f) return;
            Vector3 truckPosition = truck.localPosition;
            Vector3 excavatorPosition = excavator.localPosition;
            Vector3 dozerPosition = bulldozer.localPosition;
            if (!haveMotionSample)
            {
                previousTruckPosition = truckPosition;
                previousExcavatorPosition = excavatorPosition;
                previousDozerPosition = dozerPosition;
                previousTruckRotation = truck.localRotation;
                previousExcavatorRotation = excavator.localRotation;
                previousDozerRotation = bulldozer.localRotation;
                SnapshotMotion();
                haveMotionSample = true;
            }
            excavatorMovement = SpeedIntensity(Vector3.Distance(excavatorPosition, previousExcavatorPosition) / dt, .55f,
                Quaternion.Angle(previousExcavatorRotation, excavator.localRotation) / dt, 85f);
            truckMovement = SpeedIntensity(Vector3.Distance(truckPosition, previousTruckPosition) / dt, .22f,
                Quaternion.Angle(previousTruckRotation, truck.localRotation) / dt, 95f);
            dozerMovement = SpeedIntensity(Vector3.Distance(dozerPosition, previousDozerPosition) / dt, .40f,
                Quaternion.Angle(previousDozerRotation, bulldozer.localRotation) / dt, 70f);
            excavatorHydraulics = JointIntensity(excavatorJoints, previousExcavatorJoints, dt);
            truckHydraulics = SingleJointIntensity(truckBed, ref previousBedRotation, dt);
            dozerHydraulics = SingleJointIntensity(blade, ref previousBladeRotation, dt);

            excavatorLoad = excavatorOwned ? Mathf.Clamp01(.10f +
                (stage == JobStage.Clearing ? .19f + digPose.hydraulicEffort * .48f : 0f)) : 0f;
            truckLoad = truckOwned ? Mathf.Clamp01(.10f + truckMovement * .55f +
                (truckTripState == TruckTripState.Unloading ? truckHydraulics * .18f : 0f)) : 0f;
            dozerLoad = bulldozerOwned ? Mathf.Clamp01(.11f +
                (stage == JobStage.Clearing ? .15f + dozerHydraulics * .30f : 0f)) : 0f;

            previousTruckPosition = truckPosition;
            previousExcavatorPosition = excavatorPosition;
            previousDozerPosition = dozerPosition;
            previousTruckRotation = truck.localRotation;
            previousExcavatorRotation = excavator.localRotation;
            previousDozerRotation = bulldozer.localRotation;
        }

        private static float SpeedIntensity(float linearSpeed, float linearFull,
            float angularSpeed, float angularFull)
        {
            return Mathf.Clamp01(Mathf.Max(linearSpeed / linearFull, angularSpeed / angularFull));
        }

        private float JointIntensity(Transform[] joints, Quaternion[] previous, float dt)
        {
            float degreesPerSecond = 0f;
            for (int i = 0; i < joints.Length; i++)
            {
                degreesPerSecond += Quaternion.Angle(previous[i], joints[i].localRotation) / dt;
                previous[i] = joints[i].localRotation;
            }
            return Mathf.Clamp01(degreesPerSecond / 150f);
        }

        private static float SingleJointIntensity(Transform joint, ref Quaternion previous, float dt)
        {
            float value = Mathf.Clamp01(Quaternion.Angle(previous, joint.localRotation) / dt / 100f);
            previous = joint.localRotation;
            return value;
        }

        private void SnapshotMotion()
        {
            if (previousExcavatorJoints == null) return;
            previousExcavatorJoints[0] = turret.localRotation;
            previousExcavatorJoints[1] = boom.localRotation;
            previousExcavatorJoints[2] = stick.localRotation;
            previousExcavatorJoints[3] = bucket.localRotation;
            previousBladeRotation = blade.localRotation;
            previousBedRotation = truckBed.localRotation;
            previousTruckPosition = truck.localPosition;
            previousWheelPosition = previousTruckPosition;
            previousExcavatorPosition = excavator.localPosition;
            previousDozerPosition = bulldozer.localPosition;
            previousTruckRotation = truck.localRotation;
            previousExcavatorRotation = excavator.localRotation;
            previousDozerRotation = bulldozer.localRotation;
        }

        internal bool TryGetMachineAudioState(int index, out Vector3 worldPosition,
            out float load, out float movement, out float hydraulics)
        {
            worldPosition = Vector3.zero;
            load = movement = hydraulics = 0f;
            Transform machine;
            switch (index)
            {
                case 0:
                    machine = excavator;
                    load = excavatorLoad;
                    movement = excavatorMovement;
                    hydraulics = excavatorHydraulics;
                    break;
                case 1:
                    machine = truck;
                    load = truckLoad;
                    movement = truckMovement;
                    hydraulics = truckHydraulics;
                    break;
                case 2:
                    machine = bulldozer;
                    load = dozerLoad;
                    movement = dozerMovement;
                    hydraulics = dozerHydraulics;
                    break;
                default:
                    return false;
            }
            if (machine == null || !machine.gameObject.activeInHierarchy) return false;
            worldPosition = machine.position;
            return true;
        }

        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
                if (material != null) Destroy(material);
            ownedMaterials.Clear();
            foreach (Texture2D texture in ownedTextures)
                if (texture != null) Destroy(texture);
            ownedTextures.Clear();
        }
    }
}