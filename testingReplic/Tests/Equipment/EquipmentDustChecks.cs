using System;
using System.Collections.Generic;
using System.Reflection;
using NewGaza;
using UnityEngine;

internal static class EquipmentDustChecks
{
    private const float Scale = .07f;
    private static int assertions;

    internal static int Run()
    {
        assertions = 0;
        CheckActualDustRig();
        CheckFleetSurfaceClassifier();
        return assertions;
    }

    private static void CheckActualDustRig()
    {
        using (var geometry = new CityGeometry())
        {
            List<Vector3> shoeVertices = new List<Vector3>();
            EquipmentGeometry.TrackShoeLoop(geometry, "Dust ground calibration shoes",
                1.95f, .44f, .34f, shoeVertices);
            float lowestShoeY = shoeVertices[0].y;
            for (int i = 1; i < shoeVertices.Count; i++)
                lowestShoeY = Mathf.Min(lowestShoeY, shoeVertices[i].y);

            var frame = new GameObject("Dust test world frame");
            frame.transform.localPosition = new Vector3(13f, 4f, -9f);
            var vehicleObject = new GameObject("Dust test tracked machine");
            vehicleObject.transform.SetParent(frame.transform, false);
            vehicleObject.transform.localScale = Vector3.one * Scale;
            const float groundY = -2f;
            vehicleObject.transform.localPosition = new Vector3(3f,
                groundY - frame.transform.position.y - lowestShoeY * Scale, 2f);

            var rig = new EquipmentDustRig(geometry, vehicleObject.transform, Scale,
                lowestShoeY, "Dust checks");
            rig.Advance(0f, false);
            rig.Advance(.1f, false);
            Check(rig.ActiveCount == 0, "stationary dust rig must remain empty");

            vehicleObject.transform.localPosition += new Vector3(0f, 0f, .22f);
            rig.Advance(.1f, false);
            Check(rig.ActiveCount > 0, "actual unpaved vehicle travel must emit dust");
            Check(rig.ActiveCount <= EquipmentDustRig.ParticleCapacity,
                "particle count is bounded by the per-machine pool");

            Mesh mesh = (Mesh)GetField(rig, "mesh");
            Transform dustTransform = ((MeshRenderer)GetField(rig, "renderer")).transform;
            Check(mesh.vertices.Length == EquipmentDustRig.ParticleCapacity * 4,
                "dust mesh keeps a fixed 48-quad vertex buffer");
            Check(mesh.colors.Length == EquipmentDustRig.ParticleCapacity * 4,
                "dust mesh keeps a fixed 48-quad color buffer");
            Check(mesh.triangles.Length == EquipmentDustRig.ParticleCapacity * 6,
                "dust mesh keeps a fixed 48-quad triangle buffer");
            List<Mesh> geometryMeshes = (List<Mesh>)GetField(geometry, "owned");
            Check(geometryMeshes.Contains(mesh),
                "pooled dust mesh is registered with the native geometry owner");
            for (int i = 0; i < mesh.vertices.Length; i++)
                Check(Finite(mesh.vertices[i]) && Finite(mesh.colors[i].a) &&
                    mesh.colors[i].a >= 0f && mesh.colors[i].a <= 1f,
                    "pooled particle position and alpha remain finite");

            int liveQuad = FindLiveQuad(mesh);
            Vector3[] vertices = mesh.vertices;
            Vector3 worldA = dustTransform.TransformPoint(vertices[liveQuad * 4]);
            Vector3 worldB = dustTransform.TransformPoint(vertices[liveQuad * 4 + 1]);
            Vector3 worldC = dustTransform.TransformPoint(vertices[liveQuad * 4 + 2]);
            float quadWidth = Vector3.Distance(worldA, worldB);
            float quadDepth = Vector3.Distance(worldB, worldC);
            float cloudGround = vehicleObject.transform.position.y + lowestShoeY * Scale;
            Check(quadWidth >= .10f && quadWidth <= .25f &&
                quadDepth >= .10f && quadDepth <= .25f,
                "soft dust quads stay small and ground-scale");
            Check(worldA.y >= cloudGround + .009f && worldA.y <= cloudGround + .04f,
                "emitted dust stays anchored just above measured track-ground contact");

            Color[] beforePause = (Color[])mesh.colors.Clone();
            Vector3 beforeVehicleMove = worldA;
            vehicleObject.transform.localPosition += new Vector3(.45f, 0f, 0f);
            rig.Advance(0f, false);
            Check(rig.ActiveCount > 0, "zero delta time does not kill active dust");
            Check(Vector3.Distance(beforeVehicleMove,
                dustTransform.TransformPoint(mesh.vertices[liveQuad * 4])) < .0001f,
                "existing dust remains at its world coordinates when only the machine moves");
            for (int i = 0; i < beforePause.Length; i++)
                Check(Math.Abs(beforePause[i].a - mesh.colors[i].a) < .000001f,
                    "zero delta time does not advance particle lifetime or alpha");

            for (int sample = 0; sample < 50; sample++)
            {
                vehicleObject.transform.localPosition += new Vector3(0f, 0f, .10f);
                rig.Advance(.05f, false);
                Check(rig.ActiveCount <= EquipmentDustRig.ParticleCapacity,
                    "50 travel samples never grow beyond the fixed particle pool");
                Check(mesh.vertices.Length == EquipmentDustRig.ParticleCapacity * 4 &&
                    mesh.colors.Length == EquipmentDustRig.ParticleCapacity * 4,
                    "travel samples reuse fixed vertex and color arrays");
                for (int vertex = 0; vertex < mesh.vertices.Length; vertex++)
                    Check(Finite(mesh.vertices[vertex]) && Finite(mesh.colors[vertex].a) &&
                        mesh.colors[vertex].a >= 0f && mesh.colors[vertex].a <= 1f,
                        "50-sample particle positions and alpha remain finite");
            }

            rig.Advance(5.1f, false);
            Check(rig.ActiveCount == 0, "dust particle count decays to zero after its lifetime");
            vehicleObject.transform.localPosition += new Vector3(0f, 0f, .25f);
            rig.Advance(.1f, true);
            Check(rig.ActiveCount == 0, "paved travel emits no dust");

            vehicleObject.SetActive(false);
            vehicleObject.transform.localPosition += new Vector3(0f, 0f, .25f);
            rig.Advance(.1f, false);
            Check(rig.ActiveCount == 0, "hidden vehicle movement emits no dust");
            vehicleObject.SetActive(true);
            rig.Advance(0f, false);
            vehicleObject.transform.localPosition += new Vector3(0f, 0f, .10f);
            rig.Advance(.1f, false);
            Check(rig.ActiveCount > 0, "visible unpaved travel resumes emission after a hidden interval");

            UnityEngine.Object.Destroy(rig.DustMaterial);
            UnityEngine.Object.Destroy(rig.DustTexture);
            UnityEngine.Object.Destroy(frame);
            geometry.Dispose();
            Check(geometryMeshes.Count == 0,
                "disposing the native geometry owner releases the pooled dust mesh");
        }
    }

    private static void CheckFleetSurfaceClassifier()
    {
        string previousResourceRoot = Resources.RootDirectory;
        Resources.RootDirectory = AppContext.BaseDirectory;
        CityBasemap basemap = CityBasemap.LoadFromResources();
        var roads = new CityRoadNetwork(basemap);
        Vector3 onRibbon;
        Vector3 alongRibbon;
        Vector3 outsideRibbon;
        string roadId;
        FindRibbonSamples(roads, out roadId, out onRibbon, out alongRibbon, out outsideRibbon);

        using (var geometry = new CityGeometry())
        {
            var fleetObject = new GameObject("Equipment dust classifier fixture");
            CityFleet fleet = fleetObject.AddComponent<CityFleet>();
            Expect<ArgumentNullException>(() => fleet.ConfigureTravelSurface(null),
                "travel surface classifier rejects null");
            for (int i = 0; i < 3; i++)
                Check(fleet.ActiveDustParticleCountForMachine(i) == 0,
                    "per-machine dust count accepts documented machine index");
            Expect<ArgumentOutOfRangeException>(() => fleet.ActiveDustParticleCountForMachine(-1),
                "per-machine dust count rejects a negative index");
            Expect<ArgumentOutOfRangeException>(() => fleet.ActiveDustParticleCountForMachine(3),
                "per-machine dust count rejects an index above two");

            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fleet.Initialize(geometry, material, material, material, material, material, material);
            Transform excavator = (Transform)GetField(fleet, "excavator");
            float lowestShoeY = ((EquipmentTrackRig)GetField(fleet, "excavatorTracks"))
                .LowestShoeVertexYModel;
            excavator.gameObject.SetActive(true);
            ((Transform)GetField(fleet, "truck")).gameObject.SetActive(true);
            ((Transform)GetField(fleet, "bulldozer")).gameObject.SetActive(true);
            SetField(fleet, "excavatorOwned", true);
            SetField(fleet, "truckOwned", true);
            SetField(fleet, "bulldozerOwned", true);
            int classifierCalls = 0;
            Vector3 lastClassified = Vector3.zero;
            fleet.ConfigureTravelSurface(position =>
            {
                classifierCalls++;
                lastClassified = position;
                return roads.RoadIdUnder(position) != null;
            });

            SetFleetExcavatorPosition(excavator, onRibbon, lowestShoeY);
            InvokeEffects(fleet, 0f);
            Check(classifierCalls >= 3 && Vector3.Distance(lastClassified,
                ((Transform)GetField(fleet, "bulldozer")).position) < .0001f,
                "configured classifier receives the current visible machine world position");
            Check(roads.RoadIdUnder(onRibbon) == roadId &&
                roads.RoadIdUnder(outsideRibbon) == null,
                "paved classification follows the sourced road ribbon's actual half-width");

            SetFleetExcavatorPosition(excavator, alongRibbon, lowestShoeY);
            InvokeEffects(fleet, .1f);
            Check(fleet.ActiveDustParticleCount == 0,
                "fleet surface classifier suppresses movement while it remains on a paved ribbon");

            SetFleetExcavatorPosition(excavator, outsideRibbon, lowestShoeY);
            InvokeEffects(fleet, 0f);
            excavator.localPosition += new Vector3(0f, 0f, .10f);
            InvokeEffects(fleet, .1f);
            Check(fleet.ActiveDustParticleCount > 0,
                "fleet emits dust after the live classifier reports off-ribbon travel");

            List<Material> materials = (List<Material>)GetField(fleet, "ownedMaterials");
            List<Texture2D> textures = (List<Texture2D>)GetField(fleet, "ownedTextures");
            EquipmentDustRig[] rigs = (EquipmentDustRig[])GetField(fleet, "dustRigs");
            bool registeredAllDustResources = rigs != null && rigs.Length == 3;
            if (registeredAllDustResources)
            {
                for (int i = 0; i < rigs.Length; i++)
                    registeredAllDustResources &= materials.Contains(rigs[i].DustMaterial) &&
                        textures.Contains(rigs[i].DustTexture);
            }
            Check(registeredAllDustResources,
                "fleet registers every dust material and procedural texture for cleanup");
            InvokePrivate(fleet, "OnDestroy");
            Check(materials.Count == 0 && textures.Count == 0,
                "fleet destruction releases and clears owned dust material and texture references");
            UnityEngine.Object.Destroy(fleetObject);
        }
        Resources.RootDirectory = previousResourceRoot;
    }

    private static void FindRibbonSamples(CityRoadNetwork roads, out string roadId,
        out Vector3 center, out Vector3 along, out Vector3 outside)
    {
        for (int segmentIndex = 0; segmentIndex < roads.Segments.Length; segmentIndex++)
        {
            CityRoadSegment segment = roads.Segments[segmentIndex];
            for (int pointIndex = 1; pointIndex < segment.Points.Length; pointIndex++)
            {
                Vector3 start = segment.Points[pointIndex - 1];
                Vector3 end = segment.Points[pointIndex];
                Vector3 direction = end - start;
                float length = new Vector2(direction.x, direction.z).magnitude;
                if (length < .5f) continue;
                direction = new Vector3(direction.x / length, 0f, direction.z / length);
                Vector3 midpoint = (start + end) * .5f;
                string foundId = roads.RoadIdUnder(midpoint);
                if (string.IsNullOrEmpty(foundId) || foundId != segment.Definition.id) continue;
                float halfWidth = Mathf.Max(.12f, segment.Width) * .5f;
                Vector3 normal = new Vector3(-direction.z, 0f, direction.x);
                Vector3 candidate = midpoint + normal * (halfWidth + .45f);
                if (roads.RoadIdUnder(candidate) != null)
                    candidate = midpoint - normal * (halfWidth + .45f);
                if (roads.RoadIdUnder(candidate) != null) continue;
                roadId = foundId;
                center = midpoint;
                along = midpoint + direction * Mathf.Min(.10f, length * .2f);
                outside = candidate;
                if (roads.RoadIdUnder(along) == roadId) return;
            }
        }
        throw new InvalidOperationException(
            "Unable to find a sourced road ribbon with an adjacent off-ribbon dust test point.");
    }

    private static void SetFleetExcavatorPosition(Transform excavator, Vector3 position,
        float lowestShoeY)
    {
        excavator.localPosition = new Vector3(position.x, -lowestShoeY * Scale, position.z);
    }

    private static void InvokeEffects(CityFleet fleet, float dt)
    {
        Time.deltaTime = dt;
        InvokePrivate(fleet, "UpdateCityFleetEffects", dt);
    }

    private static void InvokePrivate(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null) throw new InvalidOperationException("Missing production method: " + method);
        info.Invoke(target, arguments);
    }

    private static object GetField(object target, string field)
    {
        FieldInfo info = target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null) throw new InvalidOperationException("Missing production field: " + field);
        return info.GetValue(target);
    }

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null) throw new InvalidOperationException("Missing production field: " + field);
        info.SetValue(target, value);
    }

    private static int FindLiveQuad(Mesh mesh)
    {
        for (int quad = 0; quad < EquipmentDustRig.ParticleCapacity; quad++)
            if (mesh.colors[quad * 4].a > .001f) return quad;
        throw new InvalidOperationException("Expected at least one live soft dust quad.");
    }

    private static bool Finite(Vector3 value)
    {
        return Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            Check(true, message);
            return;
        }
        Check(false, message);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Equipment dust check failed: " + message);
    }
}