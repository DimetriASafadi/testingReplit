using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Runs only inside a real Unity editor. Does not grant currency or erase saves.</summary>
    [InitializeOnLoad]
    public static class NewGazaSmokeTest
    {
        private const string PendingKey = "NewGaza.Smoke.Pending";
        private const string BatchKey = "NewGaza.Smoke.Batch";
        private static double began;

        static NewGazaSmokeTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Check;
            if (SessionState.GetBool(PendingKey, false))
                began = EditorApplication.timeSinceStartup;
        }

        [MenuItem("New Gaza/Run play mode smoke test", priority = 4)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Stop Play Mode before running the New Gaza smoke test.");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(NewGazaProjectSetup.ScenePath);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(BatchKey, Application.isBatchMode);
            began = EditorApplication.timeSinceStartup;
            EditorApplication.EnterPlaymode();
        }

        public static void RunBatch() { Run(); }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) began = EditorApplication.timeSinceStartup;
        }

        private static void Check()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - began < 3) return;
            var session = GameSession.Instance;
            if (session == null || !session.Ready)
            {
                if (EditorApplication.timeSinceStartup - began > 30)
                    Finish(false, "GameSession did not become ready; inspect the Console startup error.");
                return;
            }
            try
            {
                Require(Camera.main != null && Camera.main.isActiveAndEnabled, "Missing active tagged camera.");
                var world = UnityEngine.Object.FindFirstObjectByType<CityWorld>();
                Require(world != null, "Missing city world.");
                Require(UnityEngine.Object.FindFirstObjectByType<CityHud>() != null, "Missing HUD.");
                Require(UnityEngine.Object.FindObjectsByType<CitySelectable>(FindObjectsSortMode.None).Length >= Core.GameCatalog.Districts.Length,
                    "Missing selectable city districts/plots.");
                Require(UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                    "Missing UI EventSystem.");
                Require(Resources.Load<Font>("NewGazaArabic") != null, "Arabic font did not import.");
                var lit = Resources.Load<Material>("NewGazaLit");
                Require(lit != null, "URP material did not import.");
                CheckImportedCityModels(world, lit);
                CheckSourcedCityBasemap();
                var fogShader = Resources.Load<Shader>("NewGazaFog");
                Require(fogShader != null && fogShader.isSupported, "District fog shader missing or unsupported.");
                for (int d = 0; d < session.State.districts.Length; d++)
                    Require(world.IsDistrictFogged(d) == !session.State.districts[d].unlocked,
                        "Fog/access mismatch in district " + d + "; unfinished unlocked districts must stay visible.");
                foreach (var fog in world.GetComponentsInChildren<DistrictFog>(true))
                {
                    Require(fog.GetComponentsInChildren<Collider>(true).Length == 0,
                        "Fog must not intercept city selection.");
                    foreach (var renderer in fog.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Require(renderer.sharedMaterial != null && renderer.sharedMaterial.shader == fogShader,
                            "Fog renderer does not use the retained fog shader.");
                        bool fogExpected = false;
                        for (int d = 0; d < session.State.districts.Length; d++)
                            if (fog.transform.parent != null &&
                                fog.transform.parent.name.StartsWith("District " + (d + 1) + " •", StringComparison.Ordinal))
                                fogExpected = !session.State.districts[d].unlocked;
                        Require(renderer.enabled == fogExpected,
                            "Fog renderer visibility does not match district access after startup fade.");
                    }
                }
                Require(world.GetComponentsInChildren<DistrictFog>(true).Length == Core.GameCatalog.Districts.Length,
                    "Expected one fog component per district, including Rashid.");
                foreach (var renderer in world.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled && renderer.transform.parent != null &&
                        renderer.transform.parent.name.StartsWith("District plot batch", StringComparison.Ordinal))
                        Require(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On,
                            "Visible district architecture batch must cast shadows.");
                Require(RenderSettings.sun != null, "Missing directional daylight.");
                var lightData = RenderSettings.sun.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                Require(lightData != null && !lightData.usePipelineSettings,
                    "URP is overriding the configured neighborhood light bias.");
                Require(string.IsNullOrEmpty(session.SaveError), "Save could not be written.");
                Require(session.State.districts.Length == Core.GameCatalog.Districts.Length,
                    "Expected all catalog neighborhoods and the final Rashid district.");
                Finish(true, "Startup, camera, city selection, HUD, font, retained fog shader, district fog/access and local save passed in Unity Play Mode.");
            }
            catch (Exception e) { Finish(false, e.Message); }
        }

        private static void CheckImportedCityModels(CityWorld world, Material lit)
        {
            Require(lit != null && lit.shader != null &&
                lit.shader.name == "Universal Render Pipeline/Lit",
                "NewGazaLit must retain the Universal Render Pipeline/Lit shader.");
            Require(world.ImportedModelCount == 5,
                "CityWorld must explicitly load all three primary models and both context LOD models without procedural fallback.");
            string[] keys =
            {
                "apartment", "ruined_building", "rubble_heap",
                "apartment_context", "ruined_building_context"
            };
            string[] albedoKeys =
            {
                "apartment", "ruined_building", "rubble_heap",
                "apartment", "ruined_building"
            };
            int[] triangleBudgets = { 2500, 4000, 1400, 350, 450 };
            var albedos = new Texture2D[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                GameObject source = Resources.Load<GameObject>("Models/" + key);
                Require(source != null, "Missing imported model resource Models/" + key + ".");
                if (source == null) continue;

                Texture2D albedo = Resources.Load<Texture2D>("Models/" + albedoKeys[i] + "_albedo");
                Require(albedo != null,
                    "Missing primary imported albedo resource Models/" + albedoKeys[i] + "_albedo.");
                if (albedo == null) continue;
                albedos[i] = albedo;
                Require(albedo.width <= 1024 && albedo.height <= 1024,
                    "Imported albedo exceeds its 1024-pixel mobile texture limit: " + key + ".");

                MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
                int meshCount = 0;
                int triangleCount = 0;
                bool hasDetailedMesh = false;
                for (int f = 0; f < filters.Length; f++)
                {
                    Mesh mesh = filters[f].sharedMesh;
                    if (mesh == null) continue;
                    meshCount++;
                    Require(mesh.isReadable,
                        "Imported mesh is not CPU-readable for CityMeshBatch.CombineMeshes: " + key + ".");
                    Vector3[] vertices = mesh.vertices;
                    Vector2[] uvs = mesh.uv;
                    Vector3[] normals = mesh.normals;
                    Require(uvs.Length == vertices.Length,
                        "Imported mesh UV count must equal its vertex count: " + key + ".");
                    Require(normals.Length == vertices.Length,
                        "Imported normals must be present for every source vertex: " + key + ".");
                    bool hasNormal = false;
                    for (int n = 0; n < normals.Length; n++)
                        if (normals[n].sqrMagnitude > .5f) { hasNormal = true; break; }
                    Require(hasNormal, "Imported mesh has no usable imported normals: " + key + ".");
                    if (vertices.Length > 64) hasDetailedMesh = true;
                    triangleCount += mesh.triangles.Length / 3;
                }

                Require(meshCount > 0, "Imported model contains no source meshes: " + key + ".");
                Require(hasDetailedMesh,
                    "Imported model must include a real mesh with more than 64 vertices: " + key + ".");
                Require(triangleCount <= Mathf.CeilToInt(triangleBudgets[i] * 1.01f),
                    "Imported model exceeds its mobile triangle budget of " +
                    triangleBudgets[i] + " triangles (+1% tolerance): " + key +
                    " has " + triangleCount + ".");
            }

            bool foundImportedRenderer = false;
            foreach (MeshRenderer renderer in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material == null || !material.name.StartsWith("New Gaza • imported ",
                    StringComparison.Ordinal)) continue;

                foundImportedRenderer = true;
                Require(material.shader == lit.shader,
                    "Imported model batch must use the retained URP Lit shader.");
                Require(material.enableInstancing,
                    "Imported model material must permit instancing.");
                int modelIndex = -1;
                for (int i = 0; i < keys.Length; i++)
                    if (material.name.Contains("imported " + keys[i] + " albedo",
                        StringComparison.Ordinal))
                        modelIndex = i;
                Require(modelIndex >= 0 && albedos[modelIndex] != null &&
                    material.GetTexture("_BaseMap") == albedos[modelIndex],
                    "Imported model material must bind its matching Resources albedo texture.");
                if (renderer.transform.parent != null &&
                    renderer.transform.parent.name.StartsWith("District plot batch",
                        StringComparison.Ordinal))
                    Require(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On,
                        "Imported district plot batches must cast shadows.");
            }
            Require(foundImportedRenderer,
                "CityWorld did not create a runtime renderer using an imported model material.");
            Require(albedos[3] == albedos[0] && albedos[4] == albedos[1],
                "Context LOD models must reuse the apartment and ruined-building primary albedo PNG resources.");
        }

        private static void CheckSourcedCityBasemap()
        {
            TextAsset source = Resources.Load<TextAsset>("GazaBasemap");
            Require(source != null, "Missing authentic GazaBasemap JSON Resources asset.");
            if (source == null) return;

            CityBasemap map = JsonUtility.FromJson<CityBasemap>(source.text);
            Require(map != null && (map.schemaVersion == 1 || map.schema == 1),
                "GazaBasemap must parse with the supported versioned city schema.");
            if (map == null) return;
            Require(map.roads != null && map.roads.Length >= 20 &&
                map.buildings != null && map.buildings.Length >= 100 &&
                map.areas != null && map.areas.Length >= 5,
                "GazaBasemap must include the sourced city roads, building footprints and landuse areas.");
            if (map.roads == null || map.buildings == null || map.areas == null) return;

            int roadPoints = 0, buildingPoints = 0, areaPoints = 0;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < map.roads.Length; i++)
            {
                CityBasemapRoad road = map.roads[i];
                Require(road != null && road.points != null && road.points.Length >= 2,
                    "Every sourced road must contain a valid point sequence.");
                if (road == null || road.points == null) continue;
                for (int p = 0; p < road.points.Length; p++)
                    CheckFinitePoint(road.points[p], ref roadPoints,
                        ref minX, ref maxX, ref minZ, ref maxZ);
            }
            for (int i = 0; i < map.buildings.Length; i++)
            {
                CityBasemapBuilding building = map.buildings[i];
                Require(building != null && building.center != null &&
                    building.outline != null && building.outline.Length >= 3,
                    "Every sourced building footprint must contain a center and polygon outline.");
                if (building == null) continue;
                CheckFinitePoint(building.center, ref buildingPoints,
                    ref minX, ref maxX, ref minZ, ref maxZ);
                if (building.outline == null) continue;
                for (int p = 0; p < building.outline.Length; p++)
                    CheckFinitePoint(building.outline[p], ref buildingPoints,
                        ref minX, ref maxX, ref minZ, ref maxZ);
            }
            for (int i = 0; i < map.areas.Length; i++)
            {
                CityBasemapArea area = map.areas[i];
                Require(area != null && area.points != null && area.points.Length >= 3,
                    "Every sourced landuse area must contain a polygon outline.");
                if (area == null || area.points == null) continue;
                for (int p = 0; p < area.points.Length; p++)
                    CheckFinitePoint(area.points[p], ref areaPoints,
                        ref minX, ref maxX, ref minZ, ref maxZ);
            }
            Require(roadPoints > 0 && buildingPoints > 0 && areaPoints > 0 &&
                maxX - minX > 150f && maxZ - minZ > 150f,
                "GazaBasemap must provide finite sourced coordinates spanning a contiguous urban area.");
        }

        private static void CheckFinitePoint(CityBasemapPoint point, ref int count,
            ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            bool finite = point != null &&
                !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.z);
            Require(finite, "GazaBasemap feature coordinates must be finite.");
            if (!finite) return;
            count++;
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minZ = Mathf.Min(minZ, point.z);
            maxZ = Mathf.Max(maxZ, point.z);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(PendingKey, false);
            if (success) Debug.Log("New Gaza smoke test PASS: " + message);
            else Debug.LogError("New Gaza smoke test FAIL: " + message);
            if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(success ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}