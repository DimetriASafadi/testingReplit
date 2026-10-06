using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>
    /// Continuous sourced Gaza roads, footprints and land cover with authored game architecture.
    /// Neighborhood presentation cells are NOT official district polygons.
    /// The session is the only owner of progress; this class is a disposable view.
    /// </summary>
    public sealed class CityWorld : MonoBehaviour
    {
        private CityFleetTeams fleetTeams;
        internal CityFleet FleetForJob(RubbleDispatchState job) => fleetTeams?.ForJob(job);
        internal CityFleet FleetForSite(string id) => fleetTeams?.ForSite(id) ?? fleet;
        internal int FleetCount => fleetTeams?.Count ?? 1;
        internal CityFleet FleetAt(int index) => fleetTeams != null ? fleetTeams.At(index) : fleet;
        internal void CaptureFleets(GameState state) => fleetTeams?.Capture(state);
        internal void RestoreFleets(GameState state) => fleetTeams?.Restore(state);
        private const float CityGroundY = -.09f;

        private sealed class PlotView
        {
            internal Transform anchor;
            internal GameObject visual;
            internal int stage = -1;
            internal CityConstructionPhase phase = CityConstructionPhase.Inactive;
            internal CityConstructionCrew crew;
            internal Vector3 size;
            internal ProjectDefinition definition;
            internal string sourceBuildingId;
        }

        private sealed class DistrictView
        {
            internal Transform root;
            internal Vector3 center;
            internal PlotView[] plots;
            internal GameObject rubble;
            internal GameObject crane;
            internal GameObject badge;
            internal GameObject damagedPlots;
            internal GameObject finishedPlots;
            internal CitySelectable salvageHit;
            internal DistrictFog fog;
            internal int rubbleRemaining = -1;
            internal bool unlocked;
            internal bool fogged;
            internal bool rewarded;
            internal float coverageRadius;
        }

        private GameSession session;
        private CityGeometry geometry;
        private CityModelLibrary modelLibrary;
        private CityBasemap basemap;
        private CityUrbanContext urbanContext;
        private Transform backgroundRoot;
        private List<CityUrbanDistrictRequest> backgroundRequests;
        private Material backgroundOpen, backgroundRoad, backgroundFootprint;
        private readonly HashSet<string> clearedBackground = new HashSet<string>(StringComparer.Ordinal);
        private Material seaSurfaceMaterial;
        private Transform cityRoot;
        private DistrictView[] districts;
        private DistrictFog[] districtFogs;
        private Material fogMaterial;
        private GameObject selection;
        private GameObject factory;
        private GameObject factorySite;
        private GameObject completedCoastRoad;
        private CitySelectable factoryHit;
        private CityFleet fleet;
        internal CityFleet Fleet { get { return fleet; } }
        public CityRoadNetwork Roads { get; private set; }
        private CityRoadView roadView;
        private string selectedRoadId;
        private int selectedDistrict = -1;
        private int selectedPlot = -1;
        private int factoryLevel = -1;
        private bool finale;
        private Material sand, limestone, cream, terracotta, teal, glass, asphalt, sidewalk;
        private Material urbanGround;
        private Material dark, iron, rubble, leaf, grass, yellow, white, water, sea, foam;
        private Material concrete, brick, windowFrame, rebarRust, waterTank, waterTankLight, patina;
        private Material[] districtColors;

        /// <summary>Geographic bounds are supplied by GameGeography in city-local Unity units.</summary>
        public int ImportedModelCount { get { return modelLibrary != null ? modelLibrary.ImportedModelCount : 0; } }

        public void Initialize(GameSession gameSession)
        {
            if (gameSession == null) throw new ArgumentNullException(nameof(gameSession));
            if (session != null) session.Changed -= Refresh;
            if (session != null) session.PlotSelected -= SetSelectedPlot;
            DisposeConstructionCrews();
            roadView?.Dispose();
            roadView = null;
            DisposeFogFields();
            if (cityRoot != null)
            {
                cityRoot.gameObject.SetActive(false);
                Destroy(cityRoot.gameObject);
            }
            modelLibrary?.Dispose();
            modelLibrary = null;
            geometry?.Dispose();
            session = gameSession;
            geometry = new CityGeometry();
            modelLibrary = new CityModelLibrary();
            basemap = CityBasemap.LoadFromResources();
            Roads = new CityRoadNetwork(basemap);
            session.Economy.RegisterRoadSegments(Roads.Definitions);
            fogMaterial = CreateFogMaterial();
            MakePalette();
            cityRoot = new GameObject("New Gaza • geographically placed neighborhood centers").transform;
            cityRoot.SetParent(transform, false);
            BuildLandscape();
            var urbanRequests = new List<CityUrbanDistrictRequest>();
            for (int i = 0; i < GameCatalog.FinalDistrictIndex; i++)
                urbanRequests.Add(new CityUrbanDistrictRequest(GameCatalog.Districts[i].id,
                    Point(GameGeography.DistrictPoint(i)), GameCatalog.Districts[i].projects.Length));
            backgroundRequests = urbanRequests;
            backgroundOpen = geometry.GroundMaterial("mapped dry open land", "DryGround_Albedo",
                Hex(0x87917C), basemap.EffectiveUnitsPerKilometre);
            backgroundRoad = geometry.Material("local weathered asphalt", Hex(0x636663), surface: SurfaceKind.Asphalt);
            backgroundFootprint = geometry.Material("scattered shattered concrete", Hex(0x766B5B), surface: SurfaceKind.Concrete);
            clearedBackground.Clear();
            RebuildBackground();
            roadView = new GameObject("Interactive sourced street surfaces").AddComponent<CityRoadView>();
            roadView.transform.SetParent(cityRoot, false);
            roadView.Initialize(Roads, geometry, cityRoot);
            BuildDistricts();
            BuildFactorySite();
            BuildSelection();
            fleet = new GameObject("Salvage fleet • articulated machines").AddComponent<CityFleet>();
            fleet.transform.SetParent(cityRoot, false);
            fleet.Initialize(geometry, yellow, glass, dark, iron, teal, rubble);
            fleet.ConfigureRoads(Roads, id => RoadEconomy.SpeedMultiplier(
                RoadEconomy.GetLevel(session.State, id)));
            fleet.ConfigureTravelSurface(point =>
            {
                string roadId = Roads.RoadIdUnder(point);
                return roadId != null && RoadEconomy.GetLevel(session.State, roadId) == 2;
            });
            fleetTeams = new GameObject("Independent salvage teams").AddComponent<CityFleetTeams>();
            fleetTeams.transform.SetParent(cityRoot, false);
            fleetTeams.Initialize(fleet, geometry, yellow, glass, dark, iron, teal, rubble, Roads,
                id => RoadEconomy.SpeedMultiplier(RoadEconomy.GetLevel(session.State, id)),
                point => {
                    string id = Roads.RoadIdUnder(point);
                    return id != null && RoadEconomy.GetLevel(session.State, id) == 2;
                },
                id => session.Development.SiteWorkPoint(id),
                id => session.Development.SiteDepotPoint(id));
            session.Changed += Refresh;
            session.PlotSelected += SetSelectedPlot;
            selectedDistrict = -1;
            selectedPlot = -1;
            factoryLevel = -1;
            finale = false;
            Refresh();
        }

        private void MakePalette()
        {
            if (seaSurfaceMaterial != null) Destroy(seaSurfaceMaterial);
            sand = geometry.GroundMaterial("coastal beach sand only", "CoastalSand_Albedo",
                Hex(0xC7C0AB), basemap.EffectiveUnitsPerKilometre);
            limestone = geometry.Material("neutral urban ground", Hex(0xADADA6), surface: SurfaceKind.Concrete);
            urbanGround = geometry.GroundMaterial("textured urban ground", "UrbanGround_Albedo",
                Hex(0xADADA6), basemap.EffectiveUnitsPerKilometre);
            cream = geometry.Material("aged off-white plaster", Hex(0xDED6C7), surface: SurfaceKind.Plaster);
            terracotta = geometry.Material("muted brick and terracotta", Hex(0x986E5B));
            teal = geometry.Material("muted utility teal", Hex(0x557675));
            glass = geometry.Material("smoky blue-grey glazing", Hex(0x596B6C), .42f);
            asphalt = geometry.Material("worn neutral asphalt", Hex(0x474744), surface: SurfaceKind.Asphalt);
            sidewalk = geometry.Material("weathered concrete paving", Hex(0xB7B8B2), surface: SurfaceKind.Concrete);
            dark = geometry.Material("shadowed recess", Hex(0x454542));
            iron = geometry.Material("weathered steel", Hex(0x777873), .3f);
            rubble = geometry.Material("dusty broken concrete", Hex(0xAAA292), surface: SurfaceKind.Concrete);
            leaf = geometry.Material("coastal palm green", Hex(0x55725B));
            grass = geometry.Material("dry garden sage", Hex(0x92947A));
            yellow = geometry.Material("construction ochre", Hex(0xC49D59));
            white = geometry.Material("faded road marking", Hex(0xDDD8CA));
            water = geometry.Material("coastal shallow turquoise", Hex(0x589CA4), .42f);
            sea = geometry.Material("Mediterranean deep blue", Hex(0x276983), .5f);
            foam = geometry.Material("sea foam", Hex(0xD2E0DE), .15f);
            Shader seaShader = Resources.Load<Shader>("NewGazaSea");
            if (seaShader == null)
                throw new InvalidOperationException("Missing retained Resources/NewGazaSea coastal shader.");
            seaSurfaceMaterial = new Material(seaShader) { name = "New Gaza • animated Mediterranean" };
            seaSurfaceMaterial.SetColor("_DeepColor", Hex(0x205A78));
            seaSurfaceMaterial.SetColor("_ShallowColor", Hex(0x4E969F));
            seaSurfaceMaterial.SetColor("_FoamColor", Hex(0xCCDAD7));
            seaSurfaceMaterial.SetFloat("_WaveSpeed", .8f);
            sea = seaSurfaceMaterial;
            concrete = geometry.Material("exposed grey concrete", Hex(0x99958A), surface: SurfaceKind.Concrete);
            brick = geometry.Material("dusty masonry infill", Hex(0x9E806A), surface: SurfaceKind.Stone);
            windowFrame = geometry.Material("weathered pale window frames", Hex(0xC5BBAA), surface: SurfaceKind.Stone);
            rebarRust = geometry.Material("oxidized exposed reinforcing steel", Hex(0x795B47), .2f);
            waterTank = geometry.Material("matte rooftop water tank", Hex(0x474A45), .08f);
            waterTankLight = geometry.Material("sun-faded rooftop water tank", Hex(0xBDBCB3), .08f);
            patina = geometry.Material("subtle plaster weathering", Hex(0xC6C0B5), surface: SurfaceKind.Plaster);
            districtColors = new[]
            {
                geometry.Material("district muted ochre", Hex(0xA9987C)),
                geometry.Material("district dusty rose", Hex(0xA68A7D)),
                geometry.Material("district dry sage", Hex(0x929781)),
                geometry.Material("district weathered blue grey", Hex(0x84928E)),
                geometry.Material("district muted clay", Hex(0xAA8877)),
                geometry.Material("district pale olive", Hex(0x9A9A7D)),
                geometry.Material("district limestone gold", Hex(0xB0A17F)),
                geometry.Material("district mineral grey", Hex(0x92958E)),
                geometry.Material("district olive stone", Hex(0x9B9C7E)),
                geometry.Material("district faded earth", Hex(0xA18572)),
                geometry.Material("Rashid coastal stone", Hex(0x8D9A8E))
            };
        }

        private static Color Hex(int hex)
        {
            return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
        }

        private Material DistrictAccent(int index)
        {
            return districtColors[index == GameCatalog.FinalDistrictIndex ?
                districtColors.Length - 1 : index % (districtColors.Length - 1)];
        }

        private static Material CreateFogMaterial()
        {
            Shader shader = Resources.Load<Shader>("NewGazaFog");
            if (shader == null)
                throw new InvalidOperationException("New Gaza requires Resources/NewGazaFog (custom URP soft-fog shader).");
            var material = new Material(shader) { name = "New Gaza • shared district dust / fog" };
            material.SetColor("_FogColor", new Color(.79f, .81f, .78f, 1f));
            material.SetFloat("_Softness", .16f);
            return material;
        }

        private void DisposeFogFields()
        {
            if (districtFogs != null)
            {
                foreach (DistrictFog fog in districtFogs)
                    if (fog != null) fog.Dispose();
                districtFogs = null;
            }
            if (fogMaterial != null)
            {
                Destroy(fogMaterial);
                fogMaterial = null;
            }
        }

        private void BuildLandscape()
        {
            GeoPoint[] coast = GameGeography.Coastline;
            GeoPoint[] road = GameGeography.RashidRoute;
            if (coast == null || coast.Length < 2 || road == null || road.Length < 2)
                throw new InvalidOperationException("City coast and Rashid route require at least two geographic points each.");
            float south = MapMinZ - 15f;
            float north = MapMaxZ + 15f;
            float west = MapMinX - 18f;
            float east = MapMaxX + 18f;
            // Extend the surveyed shoreline to the edges of this CITY map, not the Strip.
            var shore = new List<Vector3> { ExtrapolateAtZ(coast[0], coast[1], south) };
            foreach (GeoPoint p in coast)
                if (p.z > south && p.z < north) shore.Add(Point(p));
            shore.Add(ExtrapolateAtZ(coast[coast.Length - 2], coast[coast.Length - 1], north));
            var landscape = new CityMeshBatch(geometry);
            for (int i = 0; i < shore.Count - 1; i++)
            {
                Vector3 a = shore[i], b = shore[i + 1];
                AddQuad(landscape, sea, new Vector3(west,-.12f,a.z),
                    new Vector3(west,-.12f,b.z), b + Vector3.down * .12f, a + Vector3.down * .12f);
                AddQuad(landscape, sand, a + new Vector3(0f,-.10f,0f),
                    b + new Vector3(0f,-.10f,0f), b + new Vector3(2f,CityGroundY,0f),
                    a + new Vector3(2f,CityGroundY,0f));
                AddQuad(landscape, urbanGround, a + new Vector3(2f,CityGroundY,0f),
                    b + new Vector3(2f,CityGroundY,0f), new Vector3(east,CityGroundY,b.z),
                    new Vector3(east,CityGroundY,a.z));
                landscape.Beam(foam, a + new Vector3(0f,-.115f,0f),
                    b + new Vector3(0f,-.115f,0f), .025f);
            }
            landscape.Build("Geographic diagonal coastline • sea west / city east", cityRoot, Vector3.zero);
            var ribbon = new CityMeshBatch(geometry);
            // The sampled carriageway approaches within 2.2 units of the sea in
            // northern Rimal. Map-scale ribbons must stay inside that clearance.
            Path(ribbon, road, sidewalk, 1.8f, CityGroundY + .012f);
            ribbon.Build("Rashid coastal route • unfinished paving", cityRoot, Vector3.zero);
            var completed = new CityMeshBatch(geometry);
            Path(completed, road, asphalt, 1.2f, CityGroundY + .04f);
            for (int n = 0; n < 2; n++)
            {
                float length = PathLength(road);
                for (float d = 4f + n * 4f; d < length - 3f; d += n == 0 ? 8f : 16f)
                {
                    Vector3 direction;
                    Vector3 at = RoutePoint(road, d, out direction);
                    Vector3 inland = new Vector3(direction.z,0f,-direction.x);
                    if (n == 0)
                        completed.Box(white, at + Vector3.up * (CityGroundY + .045f),
                            new Vector3(.025f,.005f,.55f),
                            Mathf.Atan2(direction.x,direction.z) * Mathf.Rad2Deg);
                    else
                    {
                        Palm(completed, at + inland * 3.3f + Vector3.up * CityGroundY, .5f, d * 7f);
                        Lamp(completed, at + inland * 1.6f + Vector3.up * CityGroundY, 90f,.12f);
                    }
                }
            }
            completedCoastRoad = completed.Build("Completed Rashid road • full city-coast route",
                cityRoot, Vector3.zero);
            completedCoastRoad.SetActive(false);
            // Maritime diorama details are illustrative, not mapped harbor facilities.
            var maritime = new CityMeshBatch(geometry);
            float coastalSpan = coast[coast.Length - 1].z - coast[0].z;
            for (int i = 0; i < 2; i++)
            {
                float z = coast[0].z + coastalSpan * (i == 0 ? .24f : .76f);
                Boat(maritime,new Vector3(ShoreX(z) - 8f,-.04f,z),
                    i == 0 ? 20f : -25f, i == 0 ? 1.2f : .85f);
            }
            float lookoutZ = coast[0].z + coastalSpan * .88f;
            Vector3 lookout = new Vector3(ShoreX(lookoutZ) + 2.5f,0f,lookoutZ);
            maritime.Round(sidewalk,lookout + Vector3.up * .2f,new Vector3(2.5f,.4f,2.5f));
            maritime.Round(cream,lookout + Vector3.up * 1.7f,new Vector3(1f,2.8f,1f));
            maritime.Round(teal,lookout + Vector3.up * 3.35f,new Vector3(1.3f,.55f,1.3f));
            maritime.Add(geometry.Cone,terracotta,lookout + Vector3.up * 4f,
                new Vector3(1.6f,.8f,1.6f),Quaternion.identity);
            maritime.Build("Illustrative boats / seafront lookout",cityRoot,Vector3.zero);
            var arrow = new CityMeshBatch(geometry);
            Vector3 northPoint = new Vector3(east - 8f,.25f,north - 10f);
            arrow.Beam(teal,northPoint,northPoint + Vector3.forward * 6f,.36f);
            arrow.Add(geometry.Cone,teal,northPoint + Vector3.forward * 6.5f,
                new Vector3(1.9f,1.7f,.35f),Quaternion.Euler(90f,0f,0f));
            arrow.Build("North ↑",cityRoot,Vector3.zero);
        }

        private static Vector3 Point(GeoPoint p) { return new Vector3(p.x,0f,p.z); }

        private static Vector3 ExtrapolateAtZ(GeoPoint a, GeoPoint b, float z)
        {
            if (Mathf.Abs(b.z - a.z) < .001f) return new Vector3(a.x,0f,z);
            return new Vector3(Mathf.LerpUnclamped(a.x,b.x,(z - a.z) / (b.z - a.z)),0f,z);
        }

        private void AddQuad(CityMeshBatch batch, Material material, Vector3 a, Vector3 b,
            Vector3 c, Vector3 d)
        {
            var mesh = geometry.Own(new Mesh
            {
                vertices = new[] { a,b,c,d },
                // Shore shader requires u=0 west/deep, u=1 at the sampled coast.
                // Other terrain uses world UVs rather than stretching one tile over the city.
                uv = material == sea
                    ? new[] { Vector2.zero,Vector2.up,Vector2.one,Vector2.right }
                    : new[] { new Vector2(a.x,a.z),new Vector2(b.x,b.z),
                        new Vector2(c.x,c.z),new Vector2(d.x,d.z) },
                triangles = new[] { 0,1,2,0,2,3 }
            });
            mesh.RecalculateNormals();
            batch.Add(mesh,material,Vector3.zero,Vector3.one,Quaternion.identity);
        }

        private static float PathLength(GeoPoint[] path)
        {
            float length = 0f;
            for (int i = 1; i < path.Length; i++)
                length += Vector3.Distance(Point(path[i - 1]),Point(path[i]));
            return length;
        }

        private static Vector3 RoutePoint(GeoPoint[] path, float distance, out Vector3 direction)
        {
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 delta = Point(path[i]) - Point(path[i - 1]);
                float length = delta.magnitude;
                if (length < .001f) continue;
                if (distance <= length || i == path.Length - 1)
                {
                    direction = delta / length;
                    return Point(path[i - 1]) + direction * Mathf.Clamp(distance,0f,length);
                }
                distance -= length;
            }
            direction = Vector3.forward;
            return Point(path[path.Length - 1]);
        }

        private void Path(CityMeshBatch batch, GeoPoint[] path, Material material, float width, float y)
        {
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = Point(path[i - 1]), b = Point(path[i]);
                if ((a - b).sqrMagnitude < .001f) continue;
                Vector3 tangent = (b - a).normalized;
                Vector3 right = new Vector3(tangent.z,0f,-tangent.x) * (width * .5f);
                AddQuad(batch,material,a - right + Vector3.up * y,
                    b - right + Vector3.up * y,b + right + Vector3.up * y,
                    a + right + Vector3.up * y);
                batch.Round(material,b + Vector3.up * (y - .018f),new Vector3(width,.035f,width));
            }
        }

        private void Road(CityMeshBatch batch, Vector3 point, float width, float length, bool vertical)
        {
            Vector3 size = vertical ? new Vector3(width,.12f,length) : new Vector3(length,.12f,width);
            Vector3 pavement = vertical ? new Vector3(width + 1f,.16f,length) : new Vector3(length,.16f,width + 1f);
            batch.Box(sidewalk, point, pavement);
            batch.Box(asphalt, point + Vector3.up * .045f, size);
            int marks = Mathf.FloorToInt(length / 3f);
            for (int i = 0; i < marks; i++)
            {
                float offset = -length * .5f + 1.6f + i * 3f;
                Vector3 pos = point + new Vector3(vertical ? 0f : offset, .115f, vertical ? offset : 0f);
                batch.Box(white, pos, vertical ? new Vector3(.09f,.015f,1.05f) : new Vector3(1.05f,.015f,.09f));
            }
        }

        private void BuildDistricts()
        {
            int count = GameCatalog.Districts.Length;
            districts = new DistrictView[count];
            districtFogs = new DistrictFog[count];
            int coastalIndex = count - 1;
            GeoPoint[] route = GameGeography.RashidRoute;
            float routeLength = PathLength(route);
            Vector3 routeDirection;
            Vector3 routeMidpoint = RoutePoint(route,routeLength * .5f,out routeDirection);
            for (int i = 0; i < count; i++)
            {
                bool coast = i == coastalIndex;
                var definition = GameCatalog.Districts[i];
                var district = new DistrictView();
                IList<CityUrbanPlot> sourcedPlots = coast ? null :
                    urbanContext.GetDistrictPlots(definition.id, definition.projects.Length);
                Quaternion salvageRotation = Quaternion.identity;
                Quaternion badgeRotation = Quaternion.identity;
                Quaternion craneRotation = Quaternion.identity;
                Vector3 salvagePos = coast ?
                    new Vector3(routeDirection.z,0f,-routeDirection.x) * 17f :
                    urbanContext.GetUtilityPosition(definition.id, 0);
                Vector3 badgePos = coast ?
                    Point(route[route.Length - 1]) - routeMidpoint :
                    urbanContext.GetUtilityPosition(definition.id, 1);
                Vector3 cranePos = coast ?
                    salvagePos + new Vector3(5f,0f,1.5f) :
                    urbanContext.GetUtilityPosition(definition.id, 2);
                // Sourced city parcels share one world scale. Representatives remain unchanged.
                district.center = coast ? routeMidpoint : Point(GameGeography.DistrictPoint(i));
                if (!coast)
                {
                    salvagePos -= district.center;
                    badgePos -= district.center;
                    cranePos -= district.center;
                }
                district.coverageRadius = coast ? routeLength * .5f :
                    urbanContext.GetDistrictPresentation(definition.id).derivedCoverageRadius;
                district.root = new GameObject("District " + (i + 1) + " • " + definition.name).transform;
                district.root.SetParent(cityRoot, false);
                district.root.localPosition = new Vector3(district.center.x, CityGroundY, district.center.z);
                district.root.localScale = Vector3.one;
                districts[i] = district;
                district.plots = new PlotView[definition.projects.Length];
                for (int p = 0; p < definition.projects.Length; p++)
                {
                    Vector3 size = coast
                        ? new Vector3(7.6f,0f,Mathf.Min(11f,routeLength / Mathf.Max(1,definition.projects.Length) * .58f))
                        : sourcedPlots[p].size;
                    Vector3 position;
                    Quaternion rotation;
                    if (coast)
                    {
                        Vector3 direction;
                        Vector3 onRoute = RoutePoint(route,routeLength * (p + .5f) /
                            definition.projects.Length,out direction);
                        position = onRoute + new Vector3(direction.z,0f,-direction.x) * 9f -
                            district.center;
                        rotation = Quaternion.LookRotation(direction,Vector3.up);
                    }
                    else
                    {
                        position = sourcedPlots[p].worldPosition - district.center;
                        rotation = Quaternion.Euler(0f, sourcedPlots[p].yaw, 0f);
                    }
                    position.y = 0f;
                    Transform anchor = new GameObject("Plot " + p + " • " + definition.projects[p].name).transform;
                    anchor.SetParent(district.root, false);
                    anchor.localPosition = position;
                    anchor.localRotation = rotation;
                    district.plots[p] = new PlotView { anchor = anchor, size = size, definition = definition.projects[p],
                        sourceBuildingId = coast ? null : sourcedPlots[p].sourceBuildingId };
                    AddHit(anchor.gameObject, i, p, new Vector3(0f,1.1f,0f),
                        new Vector3(size.x * .85f,2.2f,size.z * .83f));
                }
                var salvage = new CityMeshBatch(geometry);
                float salvageHeight = modelLibrary.AddTo(salvage,"rubble_heap",Vector3.zero,
                    new Vector3(.36f,0f,.40f),
                    0f,.12f,preserveFootprint: true);
                district.rubble = salvage.Build("Imported rubble heap • clearing progress", district.root,
                    new Vector3(salvagePos.x,0f,salvagePos.z));
                district.rubble.transform.localRotation = salvageRotation;
                district.salvageHit = AddHit(district.rubble, i, -2,
                    new Vector3(0f,salvageHeight * .5f,0f),
                    new Vector3(.36f,Mathf.Max(.12f,salvageHeight),.40f));
                var badge = new CityMeshBatch(geometry);
                badge.Round(yellow, Vector3.zero, new Vector3(1f,.15f,1f));
                badge.Box(white, new Vector3(-.13f,.1f,0f), new Vector3(.35f,.06f,.11f), -45f);
                badge.Box(white, new Vector3(.15f,.1f,.09f), new Vector3(.6f,.06f,.11f), 45f);
                district.badge = badge.Build("Claimed district medallion • roadside", district.root,
                    new Vector3(badgePos.x,0f,badgePos.z));
                district.badge.transform.localRotation = badgeRotation;
                if (!coast) district.badge.transform.localScale = Vector3.one * .12f;
                district.badge.SetActive(false);
                district.crane = BuildCrane(district.root,
                    new Vector3(cranePos.x,0f,cranePos.z));
                district.crane.transform.localRotation = craneRotation;
                if (!coast) district.crane.transform.localScale = Vector3.one * .05f;
                district.crane.SetActive(false);

                IList<CityUrbanBuildingPresentation> fogContext = coast ? null :
                    urbanContext.GetDistrictContextCandidates(definition.id, 48);
                int fogCount = district.plots.Length + (fogContext == null ? 0 : fogContext.Count);
                var fogCenters = new Vector3[fogCount];
                var fogRotations = new Quaternion[fogCount];
                var fogSizes = new Vector3[fogCount];
                for (int p = 0; p < district.plots.Length; p++)
                {
                    fogCenters[p] = district.plots[p].anchor.localPosition;
                    fogRotations[p] = district.plots[p].anchor.localRotation;
                    fogSizes[p] = district.plots[p].size;
                }
                if (fogContext != null)
                    for (int p = 0; p < fogContext.Count; p++)
                    {
                        int slot = district.plots.Length + p;
                        fogCenters[slot] = fogContext[p].worldPosition - district.root.localPosition;
                        fogRotations[slot] = Quaternion.Euler(0f,fogContext[p].yaw,0f);
                        fogSizes[slot] = fogContext[p].size;
                    }
                DistrictState savedDistrict = session != null && session.State != null &&
                    session.State.districts != null && i < session.State.districts.Length
                    ? session.State.districts[i] : null;
                bool initiallyFogged = savedDistrict != null && !savedDistrict.unlocked;
                var fogObject = new GameObject("Soft localized dust / fog • locked district");
                fogObject.transform.SetParent(district.root, false);
                district.fog = fogObject.AddComponent<DistrictFog>();
                district.fog.Initialize(fogMaterial, i, fogCenters, fogRotations,
                    fogSizes, initiallyFogged, !coast);
                district.fogged = initiallyFogged;
                districtFogs[i] = district.fog;
            }
        }

        private void BuildInlandStreets(DistrictLayout layout, int districtIndex, Transform root)
        {
            var batch = new CityMeshBatch(geometry);
            for (int i = 0; i < layout.Streets.Length; i++)
            {
                DistrictLayout.Street street = layout.Streets[i];
                Vector3 delta = street.end - street.start;
                float length = delta.magnitude;
                if (length < .1f) continue;
                Vector3 tangent = delta / length;
                Vector3 normal = new Vector3(tangent.z,0f,-tangent.x);
                Vector3 midpoint = (street.start + street.end) * .5f;
                float yaw = Mathf.Atan2(tangent.x,tangent.z) * Mathf.Rad2Deg;
                float roadHalfWidth = street.width * .5f;
                float sidewalkOffset = roadHalfWidth + DistrictLayout.SidewalkWidth * .5f;

                batch.Box(asphalt,midpoint + Vector3.up * .04f,
                    new Vector3(street.width,.08f,length + .12f),yaw);
                for (int side = -1; side <= 1; side += 2)
                {
                    batch.Box(limestone,midpoint + normal * side * (roadHalfWidth + .055f) +
                        Vector3.up * .07f,new Vector3(.11f,.14f,length + .08f),yaw);
                    batch.Box(sidewalk,midpoint + normal * side * sidewalkOffset +
                        Vector3.up * .035f,
                        new Vector3(DistrictLayout.SidewalkWidth,.07f,length + .14f),yaw);
                }

                // A small compound set of street-only hit volumes replaces the old district
                // slab collider. Trim the ends so branches do not overlap at junctions.
                float hitLength = length - street.width * 1.45f;
                if (hitLength > .35f)
                {
                    var hitTarget = new GameObject("Street interaction • " + (i + 1)).transform;
                    hitTarget.SetParent(root,false);
                    hitTarget.localPosition = midpoint;
                    hitTarget.localRotation = Quaternion.Euler(0f,yaw,0f);
                    AddHit(hitTarget.gameObject,districtIndex,-1,
                        Vector3.up * .13f,
                        new Vector3(street.width * .92f,.26f,hitLength));
                }
            }
            for (int i = 0; i < layout.Streets.Length; i++)
            {
                DistrictLayout.Street street = layout.Streets[i];
                batch.Round(asphalt,street.start + Vector3.up * .04f,
                    new Vector3(street.width,.08f,street.width));
                batch.Round(asphalt,street.end + Vector3.up * .04f,
                    new Vector3(street.width,.08f,street.width));
            }
            batch.Build("Irregular branched streets • asphalt, curbs and sidewalks",
                root,Vector3.zero);
        }

        private static CitySelectable AddHit(GameObject target, int district, int plot, Vector3 center, Vector3 size)
        {
            var hit = target.AddComponent<CitySelectable>();
            hit.districtIndex = district;
            hit.plotIndex = plot;
            var collider = target.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
            return hit;
        }

        public void Refresh()
        {
            if (session == null || session.State == null || districts == null) return;
            GameState state = session.State;
            roadView?.Refresh(state, selectedRoadId);
            for (int d = 0; d < districts.Length && d < state.districts.Length; d++)
            {
                DistrictView view = districts[d];
                DistrictState district = state.districts[d];
                view.unlocked = district.unlocked;
                view.fogged = !district.unlocked;
                if (view.fog != null) view.fog.Show(view.fogged);
                view.rewarded = district.rewardClaimed;
                view.badge.SetActive(district.rewardClaimed);
                int remaining = Mathf.Max(0, GameCatalog.Districts[d].rubbleLoads - district.clearedLoads);
                if (view.rubbleRemaining != remaining)
                {
                    view.rubbleRemaining = remaining;
                    // Keep the hit target available after clearing: imported salvage contracts
                    // do not restore local ruins and are still dispatched by the session.
                    float scale = remaining == 0 ? .16f :
                        Mathf.Lerp(.3f,1f,remaining / (float)Mathf.Max(1,GameCatalog.Districts[d].rubbleLoads));
                    view.rubble.transform.localScale = new Vector3(1f,scale,1f);
                }
                bool constructing = false;
                bool plotChanged = false;
                for (int p = 0; p < view.plots.Length; p++)
                {
                    PlotView plot = view.plots[p];
                    ProjectState project = FindProject(district, plot.definition.id);
                    int stage = project != null && project.completed ? 3 :
                        project != null && project.startedUtc > 0 ? 2 : remaining == 0 ? 1 : 0;
                    if (session.Development != null && (project == null || (!project.completed && project.startedUtc == 0)))
                        stage = session.Development.Rules.Site(CityDevelopmentService.SiteId(d, p)).cleared ? -2 : 0;
                    CityConstructionPhase phase = CityConstructionVisuals.ResolvePhase(project, session.Now);
                    constructing |= stage == 2;
                    if (stage != plot.stage || (stage == 2 && phase != plot.phase))
                    {
                        ReplacePlot(plot, d, p, stage, phase);
                        plotChanged = true;
                    }
                    plot.phase = phase;
                    if (stage == 2)
                    {
                        if (plot.crew == null)
                        {
                            Vector3 crewFootprint = new Vector3(plot.size.x * .82f, 0f, plot.size.z * .8f);
                            plot.crew = new CityConstructionCrew(plot.anchor, crewFootprint, geometry,
                                cream, yellow, teal, yellow, iron, plot.definition.id == "farm");
                        }
                        bool visible = CityConstructionVisuals.ShouldShowCrew(project, session.Now,
                            district.unlocked, view.fogged);
                        plot.crew.SetPhase(phase, visible);
                    }
                    else if (plot.crew != null)
                        plot.crew.SetPhase(CityConstructionPhase.Inactive, false);
                }
                if (plotChanged) MergeDistrictPlots(view);
                view.crane.SetActive(constructing);
            }
            if (state.factoryLevel != factoryLevel)
            {
                factoryLevel = state.factoryLevel;
                ReplaceFactory();
            }
            int current = Mathf.Clamp(state.selectedDistrict,0,districts.Length - 1);
            factoryHit.districtIndex = current;
            if (current != selectedDistrict) FocusDistrict(current);
            ProjectState coastalRoad = FindProject(state.districts[districts.Length - 1],"road");
            completedCoastRoad.SetActive((coastalRoad != null && coastalRoad.completed) ||
                state.districts[districts.Length - 1].rewardClaimed);
            if (session.Development != null && session.Development.Rules != null)
                fleetTeams.Refresh(state, session.Development.WorkPosition, session.Development.DepotPosition);
            else
                fleet.Refresh(state, districts[Mathf.Clamp(state.jobDistrict,0,districts.Length - 1)].rubble.transform.position,
                    factorySite.transform.position);
            factorySite.SetActive(state.development == null ||
                (!state.development.requiresPlacedFactory && !state.development.dynamicFactoryProvided));
        }

        private static ProjectState FindProject(DistrictState district, string id)
        {
            if (district.projects == null) return null;
            foreach (ProjectState project in district.projects)
                if (project != null && project.id == id) return project;
            return null;
        }

        public void SetSelectedRoad(string id)
        {
            selectedRoadId = id;
            roadView?.Refresh(session.State, selectedRoadId);
        }

        private void MergeDistrictPlots(DistrictView district)
        {
            ReleaseVisual(district.damagedPlots);
            ReleaseVisual(district.finishedPlots);
            var damaged = new CityMeshBatch(geometry);
            var finished = new CityMeshBatch(geometry);
            foreach (PlotView plot in district.plots)
            {
                CityMeshBatch target = plot.stage == 3 ? finished : damaged;
                foreach (MeshFilter filter in plot.visual.GetComponentsInChildren<MeshFilter>())
                {
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    renderer.enabled = false;
                    Matrix4x4 mergedTransform = district.root.worldToLocalMatrix *
                        filter.transform.localToWorldMatrix;
                    target.Add(filter.sharedMesh,renderer.sharedMaterial,mergedTransform);
                }
            }
            // Source meshes remain cached for incremental stage changes. Only this district's
            // presentation batch is replaced; normal one-second session ticks do no mesh work.
            district.damagedPlots = damaged.Build("District plot batch • ruins / foundations / construction",
                district.root,Vector3.zero,true);
            district.finishedPlots = finished.Build("District plot batch • completed architecture",
                district.root,Vector3.zero,true);
        }

        private void ReplacePlot(PlotView plot, int district, int index, int stage,
            CityConstructionPhase phase)
        {
            ReleaseVisual(plot.visual);
            plot.stage = stage;
            var batch = new CityMeshBatch(geometry);
            Vector3 footprint = new Vector3(plot.size.x * .82f,.12f,plot.size.z * .8f);
            bool geographicParcel = district < GameCatalog.FinalDistrictIndex;
            bool housing = plot.definition.kind == ProjectKind.Housing;
            bool importedArchitecture = stage == 0 ||
                (housing && stage >= 1);
            float visualScale = geographicParcel && !importedArchitecture ? .2f : 1f;
            footprint /= visualScale;
            // Ruins and housing sit on the shared terrain, not on rectangular display pads.
            // Keep ground works only where a cleared/construction/infrastructure site needs them.
            if ((stage == 1 && !housing) || (stage == 2 && !housing) ||
                (stage == 3 && !housing))
                batch.Box(stage == 3 ? sidewalk : urbanGround, new Vector3(0f,.04f,0f), footprint);
            float modelHeight = 0f;
            if (stage == -2) { /* Clean, empty land: no preselected housing foundation. */ }
            else if (stage == 0)
            {
                string districtId = GameCatalog.Districts[district].id;
                CityRuinProfiles.Profile ruinProfile = CityRuinProfiles.ForDistrict(districtId);
                string ruinKey = CityRuinProfiles.StageZeroModel(districtId, plot.definition.id);
                modelHeight = modelLibrary.AddTo(batch,ruinKey,
                    Vector3.zero,
                    new Vector3(footprint.x * .76f,0f,footprint.z * .72f),0f,
                    ruinProfile.maxHeightCityUnits);
                string detailKey = CityRuinProfiles.StageZeroDetail(districtId, index);
                float detailWidth = footprint.x * .14f;
                float detailDepth = footprint.z * .14f;
                float detailX = footprint.x * ((index % 2 == 0) ? -.34f : .34f);
                float detailZ = footprint.z * ((index % 3 == 0) ? .34f : -.34f);
                modelLibrary.AddTo(batch,detailKey,new Vector3(detailX,0f,detailZ),
                    new Vector3(detailWidth,0f,detailDepth),(district * 47 + index * 23) % 360,
                    Mathf.Min(detailWidth,detailDepth) * .8f);
                AddImportedRubbleScatter(batch,footprint,district * 17 + index * 31);
            }
            else if (stage == 1 && housing)
                modelHeight = AddHousingModel(batch, district, footprint,
                    CityConstructionPhase.Foundation);
            else if (stage == 1) ClearedPlot(batch, footprint);
            else if (stage == 2 && housing)
                modelHeight = AddHousingModel(batch, district, footprint, phase);
            else if (stage == 2) Construction(batch, district, footprint);
            else if (housing)
                modelHeight = AddHousingModel(batch, district, footprint, CityConstructionPhase.Complete);
            else FinishedProject(batch, plot.definition, district, index, footprint);
            plot.visual = batch.Build(stage == 0 ? "Damaged structure" : stage == 1 ? "Cleared foundation" :
                stage == 2 ? "Under construction / scaffold" : "Completed • " + plot.definition.name,
                plot.anchor, Vector3.zero, stage == 0 || stage == 3);
            plot.visual.transform.localScale = Vector3.one * visualScale;
            // The selectable volume follows the architecture, so tapping an upper-storey
            // roof hits its own plot rather than the ground behind it in an angled view.
            float hitHeight = modelHeight > 0f ? modelHeight :
                stage == 1 ? .65f : stage == 2 ? 4.5f : 2.2f;
            if (stage == 3 && !housing)
            {
                switch (plot.definition.kind)
                {
                    case ProjectKind.Housing: hitHeight = district >= 5 ? 5.5f : 3f; break;
                    case ProjectKind.Landmark: hitHeight = 5.6f; break;
                    case ProjectKind.Water: hitHeight = 3f; break;
                    case ProjectKind.Power: hitHeight = 1.3f; break;
                    case ProjectKind.Road: hitHeight = 3.1f; break;
                    case ProjectKind.Park: hitHeight = 3.2f; break;
                    case ProjectKind.Services: hitHeight = 2.8f; break;
                    case ProjectKind.Investment:
                        hitHeight = district == GameCatalog.FinalDistrictIndex && plot.definition.id == "commerce" ? 5.6f :
                            plot.definition.id == "farm" ? 1.8f : 3.2f;
                        break;
                }
            }
            hitHeight *= visualScale;
            BoxCollider hit = plot.anchor.GetComponent<BoxCollider>();
            hit.center = new Vector3(0f,hitHeight * .5f,0f);
            hit.size = new Vector3(plot.size.x * .85f,hitHeight,plot.size.z * .83f);
        }

        private float AddHousingModel(CityMeshBatch batch, int district, Vector3 footprint,
            CityConstructionPhase phase)
        {
            string districtId = GameCatalog.Districts[district].id;
            CityHousingProfiles.Profile profile = CityHousingProfiles.ForDistrict(districtId);
            string key = CityHousingProfiles.ModelKey(districtId, phase);
            // Each phase has a native OBJ with the same authored parcel envelope. Stage-specific
            // measured height caps preserve one shared 1/20 scale; the final cap is the profile's
            // logical completed height in meters / 20 rather than a parcel-size multiplier.
            return modelLibrary.AddTo(batch, key, Vector3.zero,
                new Vector3(footprint.x * .76f, 0f, footprint.z * .76f),
                0f, profile.HeightCapFor(phase));
        }

        private void Update()
        {
            if (districts == null) return;
            float deltaTime = Time.deltaTime;
            foreach (DistrictView district in districts)
                if (district.plots != null)
                    foreach (PlotView plot in district.plots)
                        if (plot.crew != null) plot.crew.Update(deltaTime);
        }

        private void DisposeConstructionCrews()
        {
            if (districts == null) return;
            foreach (DistrictView district in districts)
            {
                if (district.plots == null) continue;
                foreach (PlotView plot in district.plots)
                {
                    if (plot.crew == null) continue;
                    plot.crew.Dispose();
                    plot.crew = null;
                }
            }
        }

        private void ReleaseVisual(GameObject visual)
        {
            if (visual == null) return;
            visual.SetActive(false);
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) geometry.Release(filter.sharedMesh);
            Destroy(visual);
        }

        private void AddImportedRubbleScatter(CityMeshBatch batch, Vector3 footprint, int seed)
        {
            var random = new System.Random(seed);
            for (int i = 0; i < 3; i++)
            {
                float width = footprint.x * (.2f + (float)random.NextDouble() * .12f);
                float depth = footprint.z * (.2f + (float)random.NextDouble() * .12f);
                float maxX = Mathf.Max(0f,footprint.x * .5f - width * .5f - .12f);
                float maxZ = Mathf.Max(0f,footprint.z * .5f - depth * .5f - .12f);
                float x = ((float)random.NextDouble() * 2f - 1f) * maxX;
                float z = ((float)random.NextDouble() * 2f - 1f) * maxZ;
                float yaw = (float)random.NextDouble() * 360f;
                modelLibrary.AddTo(batch,"rubble_heap",new Vector3(x,0f,z),
                    new Vector3(width,0f,depth),yaw,Mathf.Min(width,depth));
            }
        }

        private void Ruin(CityMeshBatch batch, int district, int plot, Vector3 footprint)
        {
            float w = footprint.x * .72f;
            float depth = footprint.z * .7f;
            float h = 1.02f + ((district * 3 + plot * 2) % 4) * .14f;
            float front = -depth * .42f;
            float rear = depth * .36f;
            batch.Box(concrete,new Vector3(0f,.13f,0f),new Vector3(w * .84f,.18f,depth * .82f));

            // Partial reinforced-concrete frame: missing bays leave the room volume visibly open.
            float leftHeight = h * (.68f + (plot % 2) * .2f);
            float rightHeight = h * (.48f + (district % 2) * .18f);
            float rearHeight = h * (.72f + ((district + plot) % 2) * .16f);
            Vector3 leftBase = new Vector3(-w * .37f,.22f,front);
            Vector3 rightBase = new Vector3(w * .37f,.22f,front);
            Vector3 rearBase = new Vector3(-w * .37f,.22f,rear);
            batch.Box(concrete,leftBase + Vector3.up * leftHeight * .5f,
                new Vector3(.2f,leftHeight,.2f));
            batch.Box(concrete,rightBase + Vector3.up * rightHeight * .5f,
                new Vector3(.2f,rightHeight,.2f));
            batch.Box(concrete,rearBase + Vector3.up * rearHeight * .5f,
                new Vector3(.2f,rearHeight,.2f));
            batch.Beam(concrete,leftBase + Vector3.up * leftHeight,
                new Vector3(-w * .08f,leftHeight + .04f,front),.18f);
            batch.Beam(concrete,rearBase + Vector3.up * rearHeight,
                new Vector3(-w * .08f,rearHeight + .04f,rear),.18f);

            // Broken brick infill survives only in disconnected, jagged-edged wall patches.
            batch.Box(brick,new Vector3(-w * .18f,.55f,front),new Vector3(w * .28f,.62f,.12f),-4f);
            batch.Box(limestone,new Vector3(w * .23f,.44f,rear),new Vector3(w * .24f,.4f,.13f),6f);
            batch.Box(concrete,new Vector3(w * .38f,.48f,depth * .04f),new Vector3(.13f,.72f,depth * .22f));
            batch.Add(geometry.BrokenConcrete,concrete,new Vector3(-w * .18f,h * .7f,front - .07f),
                new Vector3(w * .45f,.22f,depth * .31f),Quaternion.Euler(2f,7f,-4f));
            batch.Add(geometry.BrokenConcrete,rubble,new Vector3(w * .18f,h * .53f,rear * .45f),
                new Vector3(w * .35f,.18f,depth * .28f),Quaternion.Euler(-4f,31f,3f));

            // Short exposed bars protrude from fractured column and slab ends.
            batch.Beam(rebarRust,new Vector3(-w * .37f,.22f + leftHeight,front),
                new Vector3(-w * .4f,.43f + leftHeight,front + .06f),.035f);
            batch.Beam(rebarRust,new Vector3(-w * .34f,.22f + leftHeight,front),
                new Vector3(-w * .29f,.39f + leftHeight,front - .03f),.03f);
            batch.Beam(rebarRust,new Vector3(w * .37f,.22f + rightHeight,front),
                new Vector3(w * .41f,.39f + rightHeight,front - .03f),.035f);
            batch.Beam(rebarRust,new Vector3(-w * .37f,.22f + rearHeight,rear),
                new Vector3(-w * .42f,.43f + rearHeight,rear + .04f),.035f);

            RubblePile(batch,new Vector3(w * .13f,.1f,-depth * .12f),
                Mathf.Min(footprint.x,footprint.z) * .34f,district * 17 + plot * 31);
        }

        private void RubblePile(CityMeshBatch batch, Vector3 pos, float size, int seed)
        {
            var random = new System.Random(seed);
            size = Mathf.Max(.25f,size);
            for (int n = 0; n < 13; n++)
            {
                float x = ((float)random.NextDouble() - .5f) * size * 1.45f;
                float z = ((float)random.NextDouble() - .5f) * size * 1.3f;
                float shard = size * (.18f + (float)random.NextDouble() * .25f);
                float width = shard * (.75f + (float)random.NextDouble() * .8f);
                float depth = shard * (.65f + (float)random.NextDouble() * .75f);
                float thickness = shard * (.2f + (float)random.NextDouble() * .42f);
                float mound = Mathf.Max(0f,.52f - Mathf.Abs(x / (size * .72f)) -
                    Mathf.Abs(z / (size * .65f))) * size * .22f;
                Vector3 scale = new Vector3(width,thickness,depth);
                Vector3 center = pos + new Vector3(x,mound + thickness * .47f,z);
                Quaternion rotation = Quaternion.Euler(
                    ((float)random.NextDouble() - .5f) * 30f,
                    (float)random.NextDouble() * 360f,
                    ((float)random.NextDouble() - .5f) * 34f);
                Material shardMaterial = n % 6 == 0 ? brick :
                    n % 4 == 0 ? limestone : n % 3 == 0 ? concrete : rubble;
                if (n % 5 == 0)
                    batch.Add(geometry.Box,shardMaterial,center,scale,rotation);
                else
                    batch.Add(geometry.BrokenConcrete,shardMaterial,center,scale,rotation);
            }
        }

        private void ClearedPlot(CityMeshBatch batch, Vector3 footprint)
        {
            float x = footprint.x * .42f, z = footprint.z * .42f;
            batch.Box(limestone, new Vector3(0f,.12f,0f), new Vector3(x * 1.7f,.12f,z * 1.7f));
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = new Vector3(i % 2 == 0 ? -x : x,.25f,i < 2 ? -z : z);
                batch.Box(teal, p, new Vector3(.12f,.35f,.12f));
            }
            batch.Box(white, new Vector3(0f,.2f,-z), new Vector3(x * 2f,.025f,.06f));
            batch.Box(white, new Vector3(0f,.2f,z), new Vector3(x * 2f,.025f,.06f));
            batch.Box(white, new Vector3(-x,.2f,0f), new Vector3(.06f,.025f,z * 2f));
            batch.Box(white, new Vector3(x,.2f,0f), new Vector3(.06f,.025f,z * 2f));
            batch.Box(terracotta, new Vector3(x * .6f,.28f,z * .6f), new Vector3(.45f,.3f,.45f));
        }

        private void Construction(CityMeshBatch batch, int district, Vector3 footprint)
        {
            float x = footprint.x * .34f, z = footprint.z * .32f;
            float height = district == GameCatalog.FinalDistrictIndex ? 3.8f : 2.4f + district % 3 * .55f;
            batch.Box(limestone, new Vector3(0f,.18f,0f), new Vector3(x * 2.1f,.22f,z * 2.1f));
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = new Vector3(i % 2 == 0 ? -x : x,height * .5f,i < 2 ? -z : z);
                batch.Box(cream, p, new Vector3(.2f,height,.2f));
                batch.Box(iron, p + new Vector3(i % 2 == 0 ? -.18f : .18f,.2f,0f),
                    new Vector3(.055f,height + .4f,.055f));
            }
            for (float y = 1f; y < height; y += 1.1f)
            {
                batch.Box(limestone, new Vector3(0f,y,0f), new Vector3(x * 2f,.14f,z * 2f));
                batch.Box(terracotta, new Vector3(0f,y + .12f,-z - .22f), new Vector3(x * 2.3f,.1f,.5f));
                batch.Beam(iron, new Vector3(-x - .15f,y - .75f,-z - .24f),
                    new Vector3(x + .15f,y + .2f,-z - .24f), .05f);
                batch.Box(yellow, new Vector3(0f,y + .4f,-z - .38f), new Vector3(x * 2.3f,.06f,.06f));
            }
            batch.Box(teal, new Vector3(0f,.55f,z + .18f), new Vector3(x * 1.4f,.65f,.08f));
            batch.Box(yellow, new Vector3(x,.5f,-z - .25f), new Vector3(.55f,.8f,.08f));
            batch.Box(dark, new Vector3(x,.5f,-z - .3f), new Vector3(.15f,.65f,.025f), 24f);
        }

        private void FinishedProject(CityMeshBatch batch, ProjectDefinition project, int district, int index, Vector3 footprint)
        {
            string id = (project.id ?? string.Empty).ToLowerInvariant();
            Material accent = DistrictAccent(district);
            float scale = Mathf.Min(footprint.x / 4.6f,footprint.z / 4.3f);
            switch (project.kind)
            {
                case ProjectKind.Road:
                    if (district == GameCatalog.Districts.Length - 1)
                    {
                        // Completing this plot switches on the entire mapped coastal road
                        // in Refresh; the plot itself is only its interactive marker.
                        batch.Round(teal,new Vector3(0f,.35f,0f),new Vector3(1.6f,.35f,1.6f));
                        batch.Box(white,new Vector3(0f,.56f,0f),new Vector3(.95f,.04f,.16f));
                        batch.Box(white,new Vector3(0f,.56f,.31f),new Vector3(.65f,.04f,.11f));
                        break;
                    }
                    Road(batch, new Vector3(0f,.19f,0f), footprint.x * .55f, footprint.z * .95f, true);
                    for (int n = 0; n < 5; n++)
                        batch.Box(white,new Vector3(-footprint.x * .2f + n * footprint.x * .1f,.3f,0f),
                            new Vector3(footprint.x * .045f,.02f,footprint.z * .23f));
                    Lamp(batch,new Vector3(footprint.x * .36f,.2f,footprint.z * .22f),-90f);
                    Palm(batch,new Vector3(-footprint.x * .35f,.2f,-footprint.z * .25f),2f * scale,index * 32f);
                    break;
                case ProjectKind.Power:
                    Utility(batch, footprint, false);
                    break;
                case ProjectKind.Water:
                    Utility(batch, footprint, true);
                    break;
                case ProjectKind.Park:
                    Park(batch, footprint, district);
                    break;
                case ProjectKind.Investment:
                    if (id.Contains("farm") || id.Contains("agri"))
                        Farm(batch, footprint);
                    else if (id.Contains("factory") || id.Contains("workshop") || id.Contains("industry"))
                        Workshop(batch, footprint);
                    else if (district == GameCatalog.FinalDistrictIndex || id.Contains("hotel")) Hotel(batch, footprint);
                    else Commerce(batch, footprint, accent);
                    break;
                case ProjectKind.Landmark:
                    if (district == GameCatalog.FinalDistrictIndex || id.Contains("hotel")) Hotel(batch, footprint);
                    else Landmark(batch, footprint, accent, district);
                    break;
                case ProjectKind.Services:
                    if (id.Contains("market") || id.Contains("shop")) Commerce(batch, footprint, accent);
                    else ServiceBuilding(batch, footprint, accent, id.Contains("clinic") ||
                        id.Contains("hospital") || id == "services");
                    break;
                default:
                    if (id.Contains("hotel")) Hotel(batch, footprint);
                    else Housing(batch, footprint, accent, district, index,
                        id.Contains("apartment") || id.Contains("tower") || id.Contains("multi") || district >= 5);
                    break;
            }
        }

        private void Housing(CityMeshBatch batch, Vector3 footprint, Material accent, int district, int index, bool tall)
        {
            float w = footprint.x * (.57f + (district + index) % 3 * .025f);
            float d = footprint.z * (.56f + (district * 2 + index) % 4 * .018f);
            int floors = tall && district >= 5 ? 3 + (district + index) % 2 :
                1 + (district + index) % 2;
            float h = floors * .98f;
            Building(batch, Vector3.zero, w,d,h,cream,accent,floors);
            bool pergola = (district + index) % 4 == 2;

            // Flat service roof with the small water tanks and solar arrays common to
            // utilitarian apartment and courtyard-house silhouettes.
            if ((district + index) % 3 != 1)
            {
                Material tankMaterial = (district + index) % 2 == 0 ? waterTank : waterTankLight;
                batch.Box(iron,new Vector3(-w * .28f,h + .36f,d * .2f),new Vector3(.48f,.18f,.48f));
                batch.Round(tankMaterial,new Vector3(-w * .28f,h + .68f,d * .2f),new Vector3(.54f,.55f,.54f));
                batch.Round(dark,new Vector3(-w * .28f,h + .97f,d * .2f),new Vector3(.34f,.035f,.34f));
            }
            if (!pergola && (floors > 1 || (district + index) % 2 == 0))
                SolarPanel(batch,new Vector3(w * .24f,h + .68f,-d * .16f),
                    Mathf.Min(.62f,w * .28f));
            if (pergola)
            {
                float shadeX = w * .31f;
                float shadeZ = d * .17f;
                float shadeWidth = w * .48f;
                float shadeDepth = d * .38f;
                float roofLevel = h + .34f;
                for (int corner = 0; corner < 4; corner++)
                {
                    float sideX = corner % 2 == 0 ? -1f : 1f;
                    float sideZ = corner < 2 ? -1f : 1f;
                    batch.Box(iron,new Vector3(shadeX + sideX * shadeWidth * .5f,
                        roofLevel + .29f,shadeZ + sideZ * shadeDepth * .5f),
                        new Vector3(.045f,.58f,.045f));
                }
                batch.Box(iron,new Vector3(shadeX,roofLevel + .58f,shadeZ),
                    new Vector3(shadeWidth,.045f,.045f));
                batch.Box(iron,new Vector3(shadeX,roofLevel + .58f,shadeZ - shadeDepth * .35f),
                    new Vector3(shadeWidth,.035f,.035f));
                batch.Box(iron,new Vector3(shadeX,roofLevel + .58f,shadeZ + shadeDepth * .35f),
                    new Vector3(shadeWidth,.035f,.035f));
                batch.Box(iron,new Vector3(shadeX - shadeWidth * .35f,roofLevel + .58f,shadeZ),
                    new Vector3(.035f,.035f,shadeDepth));
                batch.Box(iron,new Vector3(shadeX + shadeWidth * .35f,roofLevel + .58f,shadeZ),
                    new Vector3(.035f,.035f,shadeDepth));
            }
            if ((district + index) % 4 == 0)
            {
                Vector3 ac = new Vector3(w * .25f,h + .43f,d * .24f);
                batch.Box(limestone,ac,new Vector3(.43f,.28f,.36f));
                for (int grille = 0; grille < 3; grille++)
                    batch.Box(iron,ac + new Vector3(0f,-.07f + grille * .07f,-.19f),
                        new Vector3(.27f,.018f,.015f));
            }
            batch.Box(sidewalk,new Vector3(0f,.17f,-d * .5f - .16f),new Vector3(.82f,.12f,.34f));
            if ((district + index) % 3 == 0)
                Palm(batch,new Vector3(w * .72f,.15f,d * .3f),1.65f,index * 70f + district * 19f);
        }

        private void Building(CityMeshBatch batch, Vector3 pos, float w, float d, float h,
            Material body, Material accent, int floors)
        {
            float floorHeight = h / Mathf.Max(1,floors);
            batch.Box(body,pos + new Vector3(0f,h * .5f + .16f,0f),new Vector3(w,h,d));
            batch.Box(concrete,pos + new Vector3(0f,.24f,0f),new Vector3(w + .12f,.18f,d + .12f));
            float roofY = h + .23f;
            float parapetHeight = .27f;
            batch.Box(concrete,pos + new Vector3(0f,roofY,0f),
                new Vector3(w + .16f,.14f,d + .16f));
            batch.Box(body,pos + new Vector3(0f,roofY + .07f + parapetHeight * .5f,-d * .5f),
                new Vector3(w + .16f,parapetHeight,.1f));
            batch.Box(body,pos + new Vector3(0f,roofY + .07f + parapetHeight * .5f,d * .5f),
                new Vector3(w + .16f,parapetHeight,.1f));
            batch.Box(body,pos + new Vector3(-w * .5f,roofY + .07f + parapetHeight * .5f,0f),
                new Vector3(.1f,parapetHeight,d));
            batch.Box(body,pos + new Vector3(w * .5f,roofY + .07f + parapetHeight * .5f,0f),
                new Vector3(.1f,parapetHeight,d));

            // Restrained floor bands and framed, recessed glazing keep the facade from
            // reading as a single primitive block while all pieces remain one mesh batch.
            for (int floor = 0; floor < floors; floor++)
            {
                float y = .16f + floor * floorHeight + floorHeight * .55f;
                float windowHeight = Mathf.Min(.46f,floorHeight * .5f);
                float windowWidth = Mathf.Min(.5f,w * .18f);
                if (floor > 0)
                {
                    float bandY = .16f + floor * floorHeight;
                    batch.Box(windowFrame,pos + new Vector3(0f,bandY,-d * .5f - .018f),
                        new Vector3(w,.045f,.045f));
                    batch.Box(windowFrame,pos + new Vector3(0f,bandY,d * .5f + .018f),
                        new Vector3(w,.045f,.045f));
                }
                for (int col = 0; col < 3; col++)
                {
                    float x = (col - 1) * w * .29f;
                    if (floor == 0 && col == 1)
                    {
                        batch.Box(dark,pos + new Vector3(x,.68f,-d * .5f - .025f),new Vector3(.59f,1.04f,.045f));
                        batch.Box(accent,pos + new Vector3(x,.68f,-d * .5f - .052f),new Vector3(.48f,.9f,.035f));
                        batch.Box(windowFrame,pos + new Vector3(x,.68f,-d * .5f - .078f),
                            new Vector3(.055f,.92f,.035f));
                    }
                    else
                    {
                        Vector3 front = pos + new Vector3(x,y,-d * .5f);
                        FacadeWindow(batch,front,windowWidth,windowHeight,false,-1f);
                        if (floor == 0 && col == 0)
                        {
                            Vector3 shutter = front + new Vector3(0f,windowHeight * .2f,-.09f);
                            batch.Box(dark,shutter,new Vector3(windowWidth * .72f,.14f,.025f));
                            for (int slat = 0; slat < 3; slat++)
                                batch.Box(iron,shutter + Vector3.up * (-.04f + slat * .04f) +
                                    Vector3.forward * -.018f,
                                    new Vector3(windowWidth * .72f,.012f,.012f));
                        }
                        if (floor > 0 && col == (floor % 2 == 0 ? 0 : 2))
                            Balcony(batch,x,front.y,front.z,windowWidth * 1.6f);
                    }
                    if (col != 1)
                        FacadeWindow(batch,pos + new Vector3(x,y,d * .5f),
                            windowWidth,windowHeight,false,1f);
                }
                FacadeWindow(batch,pos + new Vector3(-w * .5f,y,0f),
                    Mathf.Min(.42f,d * .2f),windowHeight,true,-1f);
                FacadeWindow(batch,pos + new Vector3(w * .5f,y,0f),
                    Mathf.Min(.42f,d * .2f),windowHeight,true,1f);
            }
            WeatheredFacade(batch,pos,w,d,h);
        }

        private void WeatheredFacade(CityMeshBatch batch, Vector3 pos, float w, float d, float h)
        {
            int seed = Mathf.RoundToInt(w * 100f) * 73856 ^
                Mathf.RoundToInt(d * 100f) * 19349 ^
                Mathf.RoundToInt(h * 100f) * 8349;
            var random = new System.Random(seed);
            for (int patch = 0; patch < 3; patch++)
            {
                float side = patch == 1 ? 1f : -1f;
                float x = (random.Next(0,2) == 0 ? -1f : 1f) * w * .42f;
                float y = .3f + (float)random.NextDouble() * Mathf.Max(.1f,h - .55f);
                float patchWidth = w * (.065f + (float)random.NextDouble() * .04f);
                float patchHeight = .12f + (float)random.NextDouble() * .1f;
                Quaternion rotation = Quaternion.Euler(side < 0f ? 90f : -90f,0f,0f);
                batch.Add(geometry.BrokenConcrete,patina,
                    pos + new Vector3(x,y,side * (d * .5f + .004f)),
                    new Vector3(patchWidth,.012f,patchHeight),rotation);
            }
        }

        private void FacadeWindow(CityMeshBatch batch, Vector3 center, float width, float height,
            bool sideWall, float outward)
        {
            float frame = .045f;
            float paneWidth = Mathf.Max(.12f,width - frame * 2f);
            float paneHeight = Mathf.Max(.16f,height - frame * 2f);
            Vector3 normal = sideWall ? Vector3.right : Vector3.forward;
            Vector3 recess = center + normal * (outward * .012f);
            Vector3 glassPosition = center + normal * (outward * .036f);
            Vector3 framePosition = center + normal * (outward * .066f);
            batch.Box(dark,recess,sideWall ? new Vector3(.035f,height,width) :
                new Vector3(width,height,.035f));
            batch.Box(glass,glassPosition,sideWall ? new Vector3(.035f,paneHeight,paneWidth) :
                new Vector3(paneWidth,paneHeight,.035f));
            if (sideWall)
            {
                batch.Box(windowFrame,framePosition + Vector3.up * (height * .5f),
                    new Vector3(.055f,frame,width + frame));
                batch.Box(windowFrame,framePosition - Vector3.up * (height * .5f),
                    new Vector3(.055f,frame,width + frame));
                for (int edge = -1; edge <= 1; edge += 2)
                    batch.Box(windowFrame,framePosition + Vector3.forward * edge * width * .5f,
                        new Vector3(.055f,height,frame));
            }
            else
            {
                batch.Box(windowFrame,framePosition + Vector3.up * (height * .5f),
                    new Vector3(width + frame,frame,.055f));
                batch.Box(windowFrame,framePosition - Vector3.up * (height * .5f),
                    new Vector3(width + frame,frame,.055f));
                for (int edge = -1; edge <= 1; edge += 2)
                    batch.Box(windowFrame,framePosition + Vector3.right * edge * width * .5f,
                        new Vector3(frame,height,.055f));
            }
        }

        private void Balcony(CityMeshBatch batch, float x, float windowY, float facadeZ, float width)
        {
            float floorY = windowY - .3f;
            float frontZ = facadeZ - .34f;
            batch.Box(concrete,new Vector3(x,floorY,facadeZ - .19f),new Vector3(width,.09f,.43f));
            batch.Box(iron,new Vector3(x,floorY + .34f,frontZ),
                new Vector3(width,.035f,.035f));
            for (int post = -1; post <= 1; post++)
                batch.Box(iron,new Vector3(x + post * width * .5f,floorY + .19f,frontZ),
                    new Vector3(.035f,.3f,.035f));
            batch.Box(iron,new Vector3(x,floorY + .19f,frontZ),
                new Vector3(.025f,.035f,.035f));
        }

        private void Commerce(CityMeshBatch batch, Vector3 footprint, Material accent)
        {
            float w = footprint.x * .7f, d = footprint.z * .48f;
            Building(batch,new Vector3(0f,0f,.35f),w,d,1.55f,cream,accent,1);
            batch.Box(glass,new Vector3(0f,.85f,.35f - d * .5f - .05f),new Vector3(w * .82f,1f,.08f));
            for (int i = 0; i < 6; i++)
                batch.Box(i % 2 == 0 ? accent : cream,new Vector3(-w * .5f + (i + .5f) * w / 6f,1.35f,-d * .5f),
                    new Vector3(w / 6f,.16f,.95f));
            for (int i = 0; i < 3; i++)
            {
                batch.Box(terracotta,new Vector3(-w * .3f + i * w * .3f,.38f,-d * .5f - .6f),
                    new Vector3(w * .21f,.45f,.5f));
                batch.Round(i % 2 == 0 ? grass : yellow,new Vector3(-w * .3f + i * w * .3f,.65f,-d * .5f - .6f),
                    new Vector3(w * .19f,.16f,.42f));
            }
            SolarPanel(batch,new Vector3(0f,1.85f,.35f),Mathf.Min(w * .33f,.9f));
        }

        private void ServiceBuilding(CityMeshBatch batch, Vector3 footprint, Material accent, bool clinic)
        {
            float w = footprint.x * .73f, d = footprint.z * .64f;
            Building(batch,Vector3.zero,w,d,2.25f,cream,accent,2);
            batch.Box(accent,new Vector3(0f,1.15f,-d * .5f - .22f),new Vector3(.95f,1.95f,.4f));
            batch.Box(glass,new Vector3(0f,.6f,-d * .5f - .45f),new Vector3(.52f,.8f,.04f));
            batch.Box(white,new Vector3(0f,1.75f,-d * .5f - .45f),new Vector3(.48f,.13f,.04f));
            if (clinic) batch.Box(white,new Vector3(0f,1.75f,-d * .5f - .46f),new Vector3(.13f,.48f,.04f));
            else
            {
                batch.Box(yellow,new Vector3(0f,1.78f,-d * .5f - .47f),new Vector3(.25f,.22f,.04f));
                batch.Beam(iron,new Vector3(w * .65f,.2f,-d * .4f),new Vector3(w * .65f,2.6f,-d * .4f),.04f);
                batch.Box(teal,new Vector3(w * .65f + .22f,2.35f,-d * .4f),new Vector3(.45f,.3f,.035f));
            }
            Palm(batch,new Vector3(-w * .65f,.16f,-d * .3f),1.75f,25f);
        }

        private void Park(CityMeshBatch batch, Vector3 footprint, int district)
        {
            batch.Box(grass,new Vector3(0f,.16f,0f),new Vector3(footprint.x * .91f,.17f,footprint.z * .9f));
            batch.Box(sidewalk,new Vector3(0f,.27f,0f),new Vector3(footprint.x * .88f,.07f,.5f),22f);
            batch.Round(cream,new Vector3(0f,.38f,0f),new Vector3(1.2f,.22f,1.2f));
            batch.Round(water,new Vector3(0f,.51f,0f),new Vector3(.99f,.04f,.99f));
            batch.Round(cream,new Vector3(0f,.73f,0f),new Vector3(.18f,.45f,.18f));
            for (int i = 0; i < 4; i++)
                Palm(batch,new Vector3((i % 2 == 0 ? -1f : 1f) * footprint.x * .32f,.28f,
                    (i < 2 ? -1f : 1f) * footprint.z * .3f),1.9f + i % 2 * .45f,district * 24f + i * 80f);
            Bench(batch,new Vector3(-footprint.x * .19f,.25f,-footprint.z * .32f),0f);
            Bench(batch,new Vector3(footprint.x * .19f,.25f,footprint.z * .32f),180f);
        }

        private void Farm(CityMeshBatch batch, Vector3 footprint)
        {
            batch.Box(grass,new Vector3(0f,.15f,0f),new Vector3(footprint.x * .9f,.16f,footprint.z * .88f));
            for (int row = 0; row < 4; row++)
            {
                float z = -footprint.z * .32f + row * footprint.z * .18f;
                batch.Box(terracotta,new Vector3(-footprint.x * .1f,.26f,z),new Vector3(footprint.x * .59f,.11f,.22f));
                for (int col = 0; col < 5; col++)
                {
                    float x = -footprint.x * .33f + col * footprint.x * .115f;
                    batch.Add(geometry.Cone,leaf,new Vector3(x,.47f,z),new Vector3(.25f,.43f,.25f),Quaternion.identity);
                    if (row % 2 == 0) batch.Round(yellow,new Vector3(x,.47f,z - .12f),new Vector3(.1f,.13f,.1f));
                }
            }
            float w = footprint.x * .23f;
            batch.Box(cream,new Vector3(footprint.x * .3f,.7f,footprint.z * .12f),
                new Vector3(w,1.1f,footprint.z * .5f));
            batch.Add(geometry.Roof,teal,new Vector3(footprint.x * .3f,1.4f,footprint.z * .12f),
                new Vector3(w + .2f,.45f,footprint.z * .52f),Quaternion.identity);
            batch.Box(glass,new Vector3(footprint.x * .3f,.78f,-footprint.z * .14f),new Vector3(w * .5f,.6f,.05f));
            batch.Round(water,new Vector3(footprint.x * .3f,.5f,-footprint.z * .32f),new Vector3(.45f,.6f,.45f));
        }

        private void Workshop(CityMeshBatch batch, Vector3 footprint)
        {
            float w = footprint.x * .67f, d = footprint.z * .62f;
            batch.Box(cream,new Vector3(0f,.85f,0f),new Vector3(w,1.5f,d));
            batch.Add(geometry.Roof,teal,new Vector3(0f,1.85f,0f),new Vector3(w + .25f,.6f,d + .2f),Quaternion.identity);
            batch.Box(glass,new Vector3(0f,.75f,-d * .5f - .03f),new Vector3(w * .5f,1f,.06f));
            for (int n = 0; n < 4; n++)
                batch.Box(iron,new Vector3(0f,.42f + n * .23f,-d * .5f - .07f),new Vector3(w * .5f,.035f,.04f));
            batch.Round(terracotta,new Vector3(w * .45f,1.6f,d * .35f),new Vector3(.3f,2.8f,.3f));
            batch.Box(yellow,new Vector3(-w * .25f,.4f,-d * .7f),new Vector3(.5f,.5f,.5f));
        }

        private void Utility(CityMeshBatch batch, Vector3 footprint, bool isWater)
        {
            float x = footprint.x * .22f;
            if (isWater)
            {
                for (int i = 0; i < 4; i++)
                    batch.Box(iron,new Vector3((i % 2 == 0 ? -1f : 1f) * .6f,1f,
                        (i < 2 ? -1f : 1f) * .6f),new Vector3(.13f,1.8f,.13f));
                batch.Round(cream,new Vector3(0f,2.15f,0f),new Vector3(1.8f,1f,1.8f));
                batch.Round(teal,new Vector3(0f,2.72f,0f),new Vector3(1.9f,.15f,1.9f));
                batch.Beam(teal,new Vector3(.68f,.25f,0f),new Vector3(.68f,2.1f,0f),.12f);
                batch.Beam(teal,new Vector3(.68f,.25f,0f),new Vector3(x * 1.8f,.25f,0f),.12f);
                batch.Box(cream,new Vector3(-x,.5f,-footprint.z * .3f),new Vector3(.75f,.65f,.6f));
            }
            else
            {
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 2; col++)
                        SolarPanel(batch,new Vector3((col == 0 ? -1f : 1f) * x,.75f,
                            (row == 0 ? -1f : 1f) * footprint.z * .23f),Mathf.Min(1f,footprint.x * .2f));
                batch.Box(iron,new Vector3(0f,.5f,0f),new Vector3(.5f,.7f,.5f));
                batch.Box(yellow,new Vector3(0f,.66f,-.27f),new Vector3(.24f,.32f,.03f));
            }
        }

        private void SolarPanel(CityMeshBatch batch, Vector3 pos, float width)
        {
            batch.Box(iron,pos + Vector3.down * .25f,new Vector3(width * 1.2f,.5f,.08f));
            Quaternion tilt = Quaternion.Euler(-17f,0f,0f);
            batch.Add(geometry.Box,iron,pos,new Vector3(width * 1.65f,.08f,width),tilt);
            batch.Add(geometry.Box,glass,pos + Vector3.up * .055f,new Vector3(width * 1.54f,.035f,width * .9f),tilt);
            for (int i = 0; i < 3; i++)
                batch.Add(geometry.Box,teal,pos + new Vector3((i - 1) * width * .48f,.08f,0f),
                    new Vector3(.025f,.018f,width * .9f),tilt);
        }

        private void Landmark(CityMeshBatch batch, Vector3 footprint, Material accent, int district)
        {
            float w = footprint.x * .62f, d = footprint.z * .57f;
            Building(batch,Vector3.zero,w,d,1.8f,cream,accent,1);
            batch.Add(geometry.Cone,accent,new Vector3(0f,2.55f,0f),new Vector3(w * .8f,1.4f,w * .8f),Quaternion.identity);
            batch.Round(cream,new Vector3(w * .6f,1.7f,d * .25f),new Vector3(.5f,3.1f,.5f));
            batch.Round(accent,new Vector3(w * .6f,3.25f,d * .25f),new Vector3(.75f,.2f,.75f));
            batch.Add(geometry.Cone,terracotta,new Vector3(w * .6f,3.7f,d * .25f),
                new Vector3(.65f,.75f,.65f),Quaternion.identity);
            Palm(batch,new Vector3(-w * .65f,.16f,d * .15f),2f,district * 35f);
        }

        private void Hotel(CityMeshBatch batch, Vector3 footprint)
        {
            float w = footprint.x * .7f, d = footprint.z * .55f;
            Building(batch,new Vector3(0f,0f,footprint.z * .1f),w,d,4.8f,cream,teal,4);
            batch.Box(teal,new Vector3(0f,5.2f,footprint.z * .1f),new Vector3(w * .62f,.5f,d * .55f));
            batch.Box(sidewalk,new Vector3(0f,.25f,-footprint.z * .32f),new Vector3(w * .9f,.2f,footprint.z * .24f));
            batch.Box(water,new Vector3(0f,.37f,-footprint.z * .32f),new Vector3(w * .68f,.04f,footprint.z * .18f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = new Vector3(side * footprint.x * .35f,.15f,-footprint.z * .2f);
                Palm(batch,p,2.5f,side * 30f);
            }
        }

        private GameObject BuildCrane(Transform parent, Vector3 pos)
        {
            var batch = new CityMeshBatch(geometry);
            const float height = 6.4f;
            batch.Box(limestone,new Vector3(0f,.25f,0f),new Vector3(1.2f,.5f,1.2f));
            for (int corner = 0; corner < 4; corner++)
                batch.Box(yellow,new Vector3(corner % 2 == 0 ? -.24f : .24f,height * .5f,
                    corner < 2 ? -.24f : .24f),new Vector3(.08f,height,.08f));
            for (int i = 0; i < 6; i++)
            {
                float y = .5f + i;
                batch.Box(yellow,new Vector3(0f,y,0f),new Vector3(.58f,.07f,.58f));
                batch.Beam(yellow,new Vector3(-.24f,y,-.24f),new Vector3(.24f,y + .9f,-.24f),.055f);
            }
            batch.Box(yellow,new Vector3(-2.1f,height,0f),new Vector3(6.4f,.18f,.35f));
            batch.Box(yellow,new Vector3(-2.1f,height + .55f,0f),new Vector3(6.4f,.08f,.3f));
            for (int i = 0; i < 7; i++)
                batch.Beam(yellow,new Vector3(-5f + i * .85f,height,0f),
                    new Vector3(-4.6f + i * .85f,height + .55f,0f),.07f);
            batch.Box(limestone,new Vector3(.8f,height + .15f,0f),new Vector3(1f,.8f,.8f));
            batch.Box(glass,new Vector3(-.45f,height - .15f,0f),new Vector3(.65f,.65f,.55f));
            batch.Beam(dark,new Vector3(-4f,height,0f),new Vector3(-4f,2.8f,0f),.035f);
            batch.Beam(yellow,new Vector3(-4f,2.8f,0f),new Vector3(-3.8f,2.55f,0f),.09f);
            return batch.Build("Lattice tower crane",parent,pos);
        }

        private void Palm(CityMeshBatch batch, Vector3 pos, float height, float yaw)
        {
            Vector3 top = pos + new Vector3(.16f,height,.07f);
            batch.Beam(terracotta,pos,pos + new Vector3(.03f,height * .55f,0f),.16f);
            batch.Beam(terracotta,pos + new Vector3(.03f,height * .55f,0f),top,.12f);
            for (int i = 0; i < 7; i++)
                batch.Add(geometry.Leaf,leaf,top,new Vector3(height * .58f,height * .48f,height * .58f),
                    Quaternion.Euler(i % 2 == 0 ? -12f : 9f,yaw + i * 360f / 7f,0f));
            batch.Add(geometry.Cone,leaf,top + Vector3.up * .13f,new Vector3(.3f,.55f,.3f),Quaternion.identity);
        }

        private void Bench(CityMeshBatch batch, Vector3 pos, float yaw)
        {
            Quaternion rotation = Quaternion.Euler(0f,yaw,0f);
            batch.Add(geometry.Box,terracotta,pos + Vector3.up * .28f,new Vector3(.9f,.12f,.34f),rotation);
            batch.Add(geometry.Box,terracotta,pos + rotation * new Vector3(0f,.5f,.15f),new Vector3(.9f,.3f,.08f),rotation);
            for (int i = -1; i <= 1; i += 2)
                batch.Add(geometry.Box,dark,pos + rotation * new Vector3(i * .3f,.14f,0f),
                    new Vector3(.07f,.28f,.28f),rotation);
        }

        private void Lamp(CityMeshBatch batch, Vector3 pos, float yaw, float scale = 1f)
        {
            Vector3 arm = Quaternion.Euler(0f,yaw,0f) * new Vector3(.45f * scale,0f,0f);
            batch.Round(teal,pos + new Vector3(0f,1.35f * scale,0f),
                new Vector3(.085f,2.7f,.085f) * scale);
            batch.Beam(teal,pos + Vector3.up * (2.65f * scale),
                pos + Vector3.up * (2.65f * scale) + arm,.07f * scale);
            batch.Box(cream,pos + Vector3.up * (2.62f * scale) + arm,
                new Vector3(.3f,.13f,.2f) * scale,yaw);
        }

        private void Boat(CityMeshBatch batch, Vector3 pos, float yaw, float scale)
        {
            Quaternion rot = Quaternion.Euler(0f,yaw,0f);
            batch.Add(geometry.Box,teal,pos,new Vector3(.85f,.38f,2.5f) * scale,rot);
            batch.Add(geometry.Cone,teal,pos + rot * new Vector3(0f,0f,1.48f * scale),
                new Vector3(.85f,.9f,.38f) * scale,rot * Quaternion.Euler(90f,0f,0f));
            batch.Add(geometry.Box,cream,pos + rot * new Vector3(0f,.36f,-.3f) * scale,
                new Vector3(.7f,.55f,.8f) * scale,rot);
            batch.Add(geometry.Box,glass,pos + rot * new Vector3(0f,.42f,.12f) * scale,
                new Vector3(.53f,.24f,.035f) * scale,rot);
            batch.Beam(terracotta,pos + Vector3.up * .4f * scale,pos + Vector3.up * 2f * scale,.06f);
        }

        private void BuildFactorySite()
        {
            Vector3 depot = FactoryPosition();
            var batch = new CityMeshBatch(geometry);
            // A thin apron at native grade, not a raised plinth engulfing the fleet.
            // This child is scaled .2: pavement tops stay within .003 city units (6 cm).
            batch.Box(sidewalk,new Vector3(0f,.004f,0f),new Vector3(7.6f,.012f,12f));
            batch.Box(asphalt,new Vector3(-1.4f,.012f,0f),new Vector3(2.4f,.006f,11f));
            for (int i = 0; i < 4; i++)
            {
                batch.Box(white,new Vector3(-1.3f,.016f,-4f + i * 2.3f),new Vector3(2f,.003f,.08f));
                batch.Box(iron,new Vector3(2.3f,.372f,-3.8f + i * 2.3f),new Vector3(1.1f,.7f,1.5f));
                batch.Box(i % 2 == 0 ? teal : terracotta,new Vector3(2.3f,.762f,-3.8f + i * 2.3f),
                    new Vector3(1f,.06f,1.4f));
            }
            factorySite = batch.Build("Recycling depot • dispatch apron",cityRoot,depot);
            factorySite.transform.localScale = Vector3.one * .2f;
            factoryHit = AddHit(factorySite,0,-3,new Vector3(0f,1f,0f),new Vector3(7.6f,2f,12f));
        }

        private Vector3 FactoryPosition()
        {
            int count = GameCatalog.Districts.Length - 1;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < count; i++) center += Point(GameGeography.DistrictPoint(i));
            center /= count;
            Vector3 depot = urbanContext.GetDepotPosition(center, 1.5f);
            if (depot.x <= ShoreX(depot.z) + 1.5f)
                throw new InvalidOperationException("The sourced recycling depot must remain inland of the coast.");
            depot.y = CityGroundY;
            return depot;
        }

        private static float ShoreX(float z)
        {
            GeoPoint[] coast = GameGeography.Coastline;
            for (int i = 1; i < coast.Length; i++)
                if (z <= coast[i].z)
                    return ExtrapolateAtZ(coast[i - 1],coast[i],z).x;
            return ExtrapolateAtZ(coast[coast.Length - 2],coast[coast.Length - 1],z).x;
        }

        private void ReplaceFactory()
        {
            ReleaseVisual(factory);
            var batch = new CityMeshBatch(geometry);
            if (factoryLevel <= 0)
            {
                batch.Box(iron,new Vector3(.6f,.5f,2.4f),new Vector3(3f,.8f,2.5f));
                batch.Box(teal,new Vector3(.6f,1f,2.4f),new Vector3(3.1f,.15f,2.6f));
                batch.Box(yellow,new Vector3(.6f,.7f,1.1f),new Vector3(.65f,.5f,.08f));
            }
            else
            {
                Workshop(batch,new Vector3(5f,0f,5.8f));
                batch.Box(teal,new Vector3(0f,1.45f,-1.9f),new Vector3(3.2f,.5f,.1f));
                // Three-arrow recycle emblem, deliberately simple geometry.
                for (int i = 0; i < 3; i++)
                    batch.Box(white,new Vector3(-.35f + i * .35f,1.45f,-1.97f),new Vector3(.25f,.08f,.03f),i * 120f);
                batch.Beam(iron,new Vector3(1.9f,.65f,-2f),new Vector3(1.9f,1.5f,1.4f),.35f);
                batch.Box(dark,new Vector3(1.9f,1.03f,-.3f),new Vector3(.7f,.12f,3.7f));
                for (int level = 1; level < factoryLevel; level++)
                    batch.Round(level % 2 == 0 ? cream : teal,
                        new Vector3(-1.3f + (level - 1) % 2 * 1.5f,1.6f,3.3f + (level - 1) / 2 * 1.2f),
                        new Vector3(1f,2.7f,1f));
                RubblePile(batch,new Vector3(-1.7f,.15f,-3f),.9f,781);
            }
            factory = batch.Build("Recycling factory • level " + factoryLevel,factorySite.transform,
                new Vector3(0f,.25f,1.5f),true);
            if (factoryLevel > 0)
                CityFactoryMotion.Create(factory.transform, geometry, session.State,
                    new Vector3(1.9f, 1.03f, -.3f), 1, "central", false);
        }

        private void BuildSelection()
        {
            var ring = new CityMeshBatch(geometry);
            Outline(ring,yellow,1f,1f,.06f);
            selection = ring.Build("Selected plot • saffron outline",cityRoot,Vector3.zero);
            selection.SetActive(false);
        }

        private static void Outline(CityMeshBatch batch, Material material, float width, float depth, float line)
        {
            batch.Box(material,new Vector3(0f,.015f,-depth * .5f),new Vector3(width,.025f,line));
            batch.Box(material,new Vector3(0f,.015f,depth * .5f),new Vector3(width,.025f,line));
            batch.Box(material,new Vector3(-width * .5f,.015f,0f),new Vector3(line,.025f,depth));
            batch.Box(material,new Vector3(width * .5f,.015f,0f),new Vector3(line,.025f,depth));
        }

        public Vector3 DistrictPosition(int index)
        {
            if (districts == null || districts.Length == 0) return transform.position;
            return transform.TransformPoint(districts[Mathf.Clamp(index,0,districts.Length - 1)].center);
        }

        internal bool TryGetProjectPosition(int district, string projectId, out Vector3 position)
        {
            position = Vector3.zero;
            if (districts == null || district < 0 || district >= districts.Length) return false;
            foreach (var plot in districts[district].plots)
                if (plot != null && plot.definition.id == projectId)
                { position = plot.anchor.position; return true; }
            return false;
        }

        internal CityGeometry DevelopmentGeometry => geometry;
        internal CityBasemap DevelopmentMap => basemap;
        internal IList<CityUrbanBuildingPresentation> BackgroundBuildings => urbanContext.AllContextBuildings;

        internal void RefreshBackgroundRubble(IEnumerable<RubbleSiteState> sites)
        {
            bool changed = false;
            foreach (var site in sites)
                if (site.background && site.cleared && clearedBackground.Add(site.sourceBuildingId)) changed = true;
            if (changed) RebuildBackground();
        }

        private void RebuildBackground()
        {
            var root = new GameObject("Static damaged city background").transform;
            root.SetParent(cityRoot, false);
            var replacement = CityUrbanContext.Build(basemap, geometry, modelLibrary, root, urbanGround,
                backgroundOpen, asphalt, backgroundRoad, backgroundFootprint, backgroundRequests, clearedBackground);
            if (backgroundRoot != null)
            {
                backgroundRoot.gameObject.SetActive(false);
                foreach (var filter in backgroundRoot.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
                Destroy(backgroundRoot.gameObject);
            }
            backgroundRoot = root;
            urbanContext = replacement;
        }
        internal Vector3 CentralDepotPosition => factorySite.transform.position;
        internal Vector3 AggregateRubblePosition(int district) => districts[district].rubble.transform.position;

        internal CityDevelopmentParcel[] DevelopmentParcels()
        {
            var result = new List<CityDevelopmentParcel>();
            for (int d = 0; d < districts.Length; d++)
                for (int p = 0; p < districts[d].plots.Length; p++)
                {
                    var plot = districts[d].plots[p];
                    float yaw = plot.anchor.eulerAngles.y * Mathf.Deg2Rad;
                    float w = plot.size.x * .85f, h = plot.size.z * .83f;
                    var hit = plot.anchor.GetComponent<BoxCollider>();
                    result.Add(new CityDevelopmentParcel {
                        id = CityDevelopmentService.SiteId(d, p), district = d, plot = p,
                        sourceBuildingId = plot.sourceBuildingId,
                        position = plot.anchor.position, collider = hit,
                        width = Mathf.Abs(Mathf.Cos(yaw)) * w + Mathf.Abs(Mathf.Sin(yaw)) * h,
                        depth = Mathf.Abs(Mathf.Sin(yaw)) * w + Mathf.Abs(Mathf.Cos(yaw)) * h });
                }
            return result.ToArray();
        }

        public bool IsDistrictFogged(int index)
        {
            return districts != null && index >= 0 && index < districts.Length && districts[index].fogged;
        }

        public float DistrictViewingSize(int index)
        {
            if (districts == null || index < 0 || index >= districts.Length) return 24f;
            // Frame an urban region, not a miniature nine-building display tile.
            return Mathf.Clamp(districts[index].coverageRadius * .65f, 12f, 85f);
        }

        public float MapMinX { get { return basemap == null ? GameGeography.MapMinX : Mathf.Min(GameGeography.MapMinX,basemap.actualBounds.minX); } }
        public float MapMaxX { get { return basemap == null ? GameGeography.MapMaxX : Mathf.Max(GameGeography.MapMaxX,basemap.actualBounds.maxX); } }
        public float MapMinZ { get { return basemap == null ? GameGeography.MapMinZ : Mathf.Min(GameGeography.MapMinZ,basemap.actualBounds.minZ); } }
        public float MapMaxZ { get { return basemap == null ? GameGeography.MapMaxZ : Mathf.Max(GameGeography.MapMaxZ,basemap.actualBounds.maxZ); } }

        public void FocusDistrict(int index)
        {
            if (districts == null || index < 0 || index >= districts.Length) return;
            bool changed = selectedDistrict != index;
            selectedDistrict = index;
            if (changed) SetSelectedPlot(-1);
            else SetSelectedPlot(selectedPlot);
        }

        public void SetSelectedPlot(int index)
        {
            selectedPlot = index;
            if (selection == null || districts == null || selectedDistrict < 0) return;
            selection.SetActive(index != -1);
            if (index == -1) return;
            DistrictView district = districts[selectedDistrict];
            if (index == -3)
            {
                selection.transform.position = factorySite.transform.position + transform.up * .46f;
                selection.transform.rotation = factorySite.transform.rotation;
                selection.transform.localScale = new Vector3(7.7f,1f,12.1f);
            }
            else if (index == -2)
            {
                selection.transform.position = district.rubble.transform.position + transform.up * .2f;
                selection.transform.rotation = district.root.rotation;
                selection.transform.localScale = new Vector3(2.3f,1f,2.2f) * district.root.localScale.x;
            }
            else if (index >= 0 && index < district.plots.Length)
            {
                PlotView plot = district.plots[index];
                selection.transform.position = plot.anchor.position + transform.up * .18f;
                selection.transform.rotation = plot.anchor.rotation;
                selection.transform.localScale = new Vector3(plot.size.x * .9f,1f,
                    plot.size.z * .88f) * district.root.localScale.x;
            }
            else selection.SetActive(false);
        }

        public void PlayFinale()
        {
            if (finale || cityRoot == null) return;
            finale = true;
            // Visual celebration only: never alters completion flags or writes a save.
            var batch = new CityMeshBatch(geometry);
            GeoPoint[] route = GameGeography.RashidRoute;
            float length = PathLength(route);
            for (float d = 4f; d < length - 4f; d += 12f)
            {
                Vector3 direction;
                Vector3 p = RoutePoint(route,d,out direction) +
                    new Vector3(direction.z,.4f,-direction.x) * 3.9f;
                Vector3 along = direction * 4.5f;
                batch.Beam(teal,p,p + Vector3.up * 3.1f,.06f);
                batch.Box(((int)d / 12) % 2 == 0 ? terracotta : teal,p + new Vector3(.25f,2.8f,0f),
                    new Vector3(.5f,.45f,.035f));
                batch.Beam(cream,p + new Vector3(0f,2.9f,0f),p + Vector3.up * 2.9f + along,.025f);
                for (int j = 0; j < 4; j++)
                    batch.Add(geometry.Cone,j % 2 == 0 ? yellow : cream,
                        p + Vector3.up * 2.74f + direction * (.6f + j),
                        new Vector3(.2f,.3f,.15f),Quaternion.Euler(180f,0f,0f));
            }
            batch.Build("Finale • corniche celebration bunting",cityRoot,Vector3.zero);
            selection.SetActive(false);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Changed -= Refresh;
                session.PlotSelected -= SetSelectedPlot;
            }
            DisposeFogFields();
            DisposeConstructionCrews();
            if (seaSurfaceMaterial != null) Destroy(seaSurfaceMaterial);
            roadView?.Dispose();
            modelLibrary?.Dispose();
            modelLibrary = null;
            geometry?.Dispose();
        }
    }
}