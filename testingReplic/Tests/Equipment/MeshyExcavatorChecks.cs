using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NewGaza;
using UnityEngine;

internal static class MeshyExcavatorChecks
{
    private static int assertions;
    private const string Setting = "Equipment/ExcavatorAppearance";
    internal static int Run()
    {
        assertions = 0;
        Resources.TextOverrides.TryGetValue(Setting, out TextAsset saved);
        bool had = Resources.TextOverrides.ContainsKey(Setting);
        try
        {
            Resources.TextOverrides[Setting] = new TextAsset("{\"model\":\"meshy\"}");
            TextAsset source = Resources.Load<TextAsset>(MeshyExcavatorVisuals.ResourcePath);
            Require(source != null, "Install the prepared Meshy asset before running replacement checks.");
            using (var geometry = new CityGeometry())
            {
                CityFleet fleet = Make(geometry);
                Transform root = Field(fleet, "excavator");
                MeshFilter[] filters = Meshy(root);
                Require(filters.Length == 10, "All seven Meshy parts / ten material groups must render.");
                Require(filters.Sum(f => f.sharedMesh.triangles.Length / 3) == 19500, "Triangle budget matches asset.");
                string[] names = { "Undercarriage", "Track_Left", "Track_Right", "UpperBody", "Boom", "Stick", "Bucket" };
                Require(names.All(n => filters.Any(f => f.gameObject.name.Contains(n))), "Seven part names retained.");
                Require(filters.All(f => f.gameObject.GetComponent<MeshRenderer>().enabled), "New surfaces enabled.");
                Transform payload = Field(fleet, "bucketPayload");
                foreach (MeshRenderer renderer in root.gameObject.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.gameObject.name.StartsWith("Meshy •")) continue;
                    bool fragment = Ancestor(renderer.transform, payload);
                    Require(renderer.enabled == fragment, "Original surfaces hidden; carried rubble stays visible.");
                }
                string[] jointFields = { "excavator", "turret", "boom", "stick", "bucket" };
                foreach (MeshFilter filter in filters)
                {
                    int index = filter.gameObject.name.Contains("UpperBody") ? 1 :
                        filter.gameObject.name.Contains("Boom") ? 2 :
                        filter.gameObject.name.Contains("Stick") ? 3 :
                        filter.gameObject.name.Contains("Bucket") ? 4 : 0;
                    Require(filter.transform.parent == Field(fleet, jointFields[index]), "Visual bound to existing native joint.");
                    Require(filter.sharedMesh.uv.Length == filter.sharedMesh.vertices.Length, "UV data retained.");
                    Require(filter.sharedMesh.normals.Length == filter.sharedMesh.vertices.Length, "Normals retained.");
                }
                MeshFilter bucket = filters.Single(f => f.gameObject.name.Contains("Bucket"));
                Vector3 lip = bucket.sharedMesh.vertices.OrderBy(v =>
                    Vector3.Distance(v, new Vector3(0f,-.1964f,.52f))).First();
                Require(Vector3.Distance(lip, new Vector3(0f,-.1964f,.52f)) < .00001f, "Actual Meshy tooth matches native IK contact.");
                float trackBottom = filters.Where(f => f.gameObject.name.Contains("Track_"))
                    .SelectMany(f => f.sharedMesh.vertices).Min(v => v.y);
                Require(Math.Abs(trackBottom + .0375f) < .001f, "New tracks use the existing support plane.");
                for (int sample = 0; sample < 81; sample++)
                {
                    float seconds = sample * EquipmentMotion.DigCycleSeconds / 80f;
                    EquipmentMotion.ExcavationPose pose = EquipmentMotion.Dig(seconds);
                    Field(fleet,"turret").localRotation = Quaternion.Euler(0,pose.turretYaw,0);
                    Field(fleet,"boom").localRotation = Quaternion.Euler(pose.boomAngle,0,0);
                    Field(fleet,"stick").localRotation = Quaternion.Euler(pose.stickAngle,0,0);
                    Field(fleet,"bucket").localRotation = Quaternion.Euler(pose.bucketAngle,0,0);
                    Vector3 actual = root.InverseTransformPoint(bucket.transform.TransformPoint(lip));
                    Require(Vector3.Distance(actual,EquipmentMotion.BucketToothTip(pose)) < .0001f,
                        "Retargeted rendered lip follows the original full-cycle solver.");
                    Require(float.IsFinite(actual.x) && float.IsFinite(actual.y) && float.IsFinite(actual.z), "Finite animated visual.");
                }
                CityFleet second = Make(geometry);
                MeshFilter[] shared = Meshy(Field(second, "excavator"));
                Require(ReferenceEquals(filters[0].sharedMesh, shared[0].sharedMesh), "Parallel teams share city-owned imported meshes.");
                Resources.TextOverrides[Setting] = new TextAsset("{\"model\":\"original\"}");
                CityFleet original = Make(geometry);
                Transform originalRoot = Field(original, "excavator");
                Require(Meshy(originalRoot).Length == 0, "Rollback creates no Meshy renderers.");
                Require(originalRoot.gameObject.GetComponentsInChildren<MeshRenderer>(true).All(r => r.enabled),
                    "Original geometry remains available and visible.");
            }
            Resources.TextOverrides[Setting] = new TextAsset("{\"model\":\"meshy\"}");
            Resources.TextOverrides[MeshyExcavatorVisuals.ResourcePath] = null;
            using (var geometry = new CityGeometry())
                CheckFallback(geometry,"Missing asset");
            Resources.TextOverrides[MeshyExcavatorVisuals.ResourcePath] = new TextAsset(new byte[] { 1,2,3,4,5,6,7,8 });
            using (var geometry = new CityGeometry())
                CheckFallback(geometry,"Corrupt asset");
            // Exercise failed initialization too: malformed preference remains an
            // explicit error, but Unity's next frame must not update half-built rigs.
            Resources.TextOverrides[Setting] = new TextAsset("{\"model\":\"unknown\"}");
            using (var geometry = new CityGeometry())
            {
                var partial = new GameObject("Failed initialization fixture").AddComponent<CityFleet>();
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Throws<InvalidOperationException>(() =>
                    partial.Initialize(geometry,material,material,material,material,material,material),
                    "Invalid model setting reports the primary initialization error.");
                Require(Field(partial,"cargo") == null,"Fixture reproduces the reported missing cargo.");
                Tick(partial);
                Require(true,"Update safely stops after initialization failed.");
            }
            Tick(new GameObject("Uninitialized component fixture").AddComponent<CityFleet>());
            Require(true,"Update before Initialize safely stops.");
            Resources.TextOverrides[Setting] = new TextAsset("{\"model\":\"meshy\"}");
            Resources.TextOverrides.Remove(MeshyExcavatorVisuals.ResourcePath);
            using (var geometry = new CityGeometry())
            {
                CityFleet rebuilt = Make(geometry);
                Require(Meshy(Field(rebuilt,"excavator")).Length == 10, "New city obtains valid meshes after previous owner disposal.");
            }
        }
        finally
        {
            Resources.TextOverrides.Remove(MeshyExcavatorVisuals.ResourcePath);
            if (had) Resources.TextOverrides[Setting] = saved;
            else Resources.TextOverrides.Remove(Setting);
        }
        return assertions;
    }
    private static CityFleet Make(CityGeometry geometry)
    {
        CityFleet fleet = new GameObject("Excavator replacement fixture").AddComponent<CityFleet>();
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        fleet.Initialize(geometry,material,material,material,material,material,material);
        return fleet;
    }
    private static MeshFilter[] Meshy(Transform root) => root.gameObject.GetComponentsInChildren<MeshFilter>(true)
        .Where(f => f.gameObject.name.StartsWith("Meshy •")).ToArray();
    private static Transform Field(CityFleet fleet,string name) =>
        (Transform)typeof(CityFleet).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fleet);
    private static bool Ancestor(Transform node,Transform target)
    {
        for (; node != null; node = node.parent) if (node == target) return true;
        return false;
    }
    private static void CheckFallback(CityGeometry geometry,string reason)
    {
        CityFleet fleet = Make(geometry);
        Require(Meshy(Field(fleet,"excavator")).Length == 0,reason + ": original excavator retained.");
        Require(!string.IsNullOrEmpty(fleet.ExcavatorAppearanceWarning),reason + ": actionable warning retained.");
        Require(Field(fleet,"truck") != null && Field(fleet,"cargo") != null && Field(fleet,"bulldozer") != null,
            reason + ": truck cargo and bulldozer still finish initialization.");
        Require(Field(fleet,"excavator").gameObject.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.enabled),
            reason + ": original renderers remain enabled.");
        Tick(fleet);
        Require(true,reason + ": next frame runs without null references.");
    }
    private static void Tick(CityFleet fleet) =>
        typeof(CityFleet).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fleet,null);
    private static void Require(bool valid,string message)
    {
        assertions++;
        if (!valid) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action run,string message) where T : Exception
    {
        try { run(); } catch (T) { assertions++; return; }
        throw new InvalidOperationException(message);
    }
}
