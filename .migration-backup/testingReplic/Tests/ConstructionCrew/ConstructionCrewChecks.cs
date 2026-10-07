using System;
using System.IO;
using System.Text.Json;
using NewGaza;
using UnityEngine;

internal static class ConstructionCrewChecks
{
    private const int ExpectedVertices = 2736;
    private const int FramesPerPhase = 12;
    private static int assertions;

    internal static int Run()
    {
        assertions = 0;
        VerifyActualCrewActor();
        VerifyTaskRoles();
        WritePoseFixture();
        return assertions;
    }

    private static void VerifyActualCrewActor()
    {
        using (var geometry = new CityGeometry())
        {
            Material skin = MakeMaterial("skin", new Color(.67f, .49f, .37f));
            Material vest = MakeMaterial("vest", new Color(.97f, .26f, .05f));
            Material cloth = MakeMaterial("cloth", new Color(.08f, .25f, .62f));
            Material helmet = MakeMaterial("helmet", new Color(1f, .68f, .04f));
            Material tool = MakeMaterial("tool", new Color(.36f, .23f, .11f));
            Material[] inputMaterials = { skin, vest, cloth, helmet, tool };
            Vector3 footprint = new Vector3(7.8f, .1f, 6.2f);
            GameObject parent = new GameObject("Construction crew pose fixture parent");
            var crew = new CityConstructionCrew(parent.transform, footprint, geometry,
                skin, vest, cloth, helmet, tool);
            Transform root = parent.transform.GetChild(0);
            Check(Math.Abs(root.localScale.x - .05f) < .00001f &&
                Math.Abs(root.localScale.y - .05f) < .00001f &&
                Math.Abs(root.localScale.z - .05f) < .00001f,
                "workers and farming tools use the city's twenty-metres-per-unit scale, not building-sized humans");
            Check(!root.gameObject.activeSelf, "new crew begins hidden in the inactive phase");

            Mesh mesh = FindCrewMesh(root);
            MeshRenderer renderer = root.GetChild(0).GetComponent<MeshRenderer>();
            Check(mesh.vertices.Length == ExpectedVertices && mesh.vertices.Length < 3000,
                "the actual crew update path maintains a fixed sub-3000 vertex buffer");
            Check(mesh.subMeshCount == 8 && renderer.sharedMaterials.Length == 8,
                "crew retains the supplied human/tool materials plus distinguishable stone, paper and blue details");
            for (int i = 0; i < inputMaterials.Length; i++)
                Check(renderer.sharedMaterials[i] == inputMaterials[i],
                    "crew renderer references caller-owned material " + i);

            crew.SetPhase(CityConstructionPhase.Complete, true);
            crew.Update(.5f);
            Check(!root.gameObject.activeSelf, "complete projects hide the crew regardless of visibility request");
            Vector3[] completedPose = Copy(mesh.vertices);
            crew.Update(2f);
            Check(ExactlyEqual(completedPose, mesh.vertices), "complete crew geometry does not animate");

            crew.SetPhase(CityConstructionPhase.Foundation, true);
            Check(root.gameObject.activeSelf, "foundation visibility activates the real crew root");
            CheckPose(mesh, footprint, "foundation at phase start");
            CheckGroundFeet(mesh, "foundation at phase start");
            CheckGroundTool(mesh, CityConstructionPhase.Foundation);

            Vector3[] start = Copy(mesh.vertices);
            crew.Update(0f);
            crew.Update(-.5f);
            crew.Update(float.NaN);
            crew.Update(float.PositiveInfinity);
            Check(ExactlyEqual(start, mesh.vertices),
                "zero, negative, NaN, and infinite deltas do not mutate pose state");
            crew.Update(.13f);
            Check(!ExactlyEqual(start, mesh.vertices), "a positive delta advances walking and articulated actions");
            CheckPose(mesh, footprint, "foundation after positive update");

            PhaseSnapshot foundation = CapturePhase(crew, mesh, renderer, footprint,
                CityConstructionPhase.Foundation);
            CheckGroundTool(mesh, CityConstructionPhase.Foundation);
            PhaseSnapshot frame = CapturePhase(crew, mesh, renderer, footprint,
                CityConstructionPhase.Frame);
            PhaseSnapshot finishing = CapturePhase(crew, mesh, renderer, footprint,
                CityConstructionPhase.Finishing);
            CapturedPhases = new[] { foundation, frame, finishing };
            CheckGroundTool(mesh, CityConstructionPhase.Finishing);

            crew.SetPhase(CityConstructionPhase.Frame, true);
            var comparisonParent = new GameObject("Construction phase reset comparison");
            var comparisonCrew = new CityConstructionCrew(comparisonParent.transform, footprint,
                geometry, skin, vest, cloth, helmet, tool);
            comparisonCrew.SetPhase(CityConstructionPhase.Frame, true);
            Mesh comparisonMesh = FindCrewMesh(comparisonParent.transform.GetChild(0));
            Check(ExactlyEqual(mesh.vertices, comparisonMesh.vertices),
                "changing back to frame resets the animation clock to a reproducible phase-zero pose");
            comparisonCrew.Dispose();
            Check(((UnityEngine.Object)comparisonMesh).destroyed, "disposing a second crew destroys its owned dynamic mesh");

            crew.SetPhase(CityConstructionPhase.Frame, false);
            Check(!root.gameObject.activeSelf, "an invisible active phase hides its actor root");
            Vector3[] hiddenPose = Copy(mesh.vertices);
            crew.Update(.9f);
            Check(ExactlyEqual(hiddenPose, mesh.vertices), "hidden crew receives no animation updates");
            crew.SetPhase(CityConstructionPhase.Inactive, true);
            Check(!root.gameObject.activeSelf, "inactive phase always hides the actor root");

            Mesh sharedBox = geometry.Box;
            crew.Dispose();
            Check(root.gameObject.destroyed, "crew disposal destroys its root hierarchy");
            Check(((UnityEngine.Object)mesh).destroyed, "crew disposal destroys its owned dynamic mesh");
            Check(!sharedBox.destroyed, "crew disposal never destroys the shared CityGeometry box cache");
            for (int i = 0; i < inputMaterials.Length; i++)
                Check(!inputMaterials[i].destroyed, "crew disposal leaves caller-owned material " + i);
            for (int i = 0; i < inputMaterials.Length; i++) UnityEngine.Object.Destroy(inputMaterials[i]);
            UnityEngine.Object.Destroy(parent);
        }
    }

    private static void VerifyTaskRoles()
    {
        using (var geometry = new CityGeometry())
        {
            Material skin = MakeMaterial("role skin", new Color(.67f, .49f, .37f));
            Material clothing = MakeMaterial("role clothes", new Color(.22f, .45f, .26f));
            for (int farming = 0; farming < 2; farming++)
            {
                var parent = new GameObject("Role-specific crew");
                var footprint = new Vector3(2, 0, 2);
                var crew = new CityConstructionCrew(parent.transform, footprint, geometry,
                    skin, clothing, clothing, clothing, clothing, farming == 1);
                var root = parent.transform.GetChild(0);
                var mesh = FindCrewMesh(root);
                crew.SetPhase(CityConstructionPhase.Frame, true);
                var first = Copy(mesh.vertices);
                crew.Update(.31f);
                var next = Copy(mesh.vertices);
                const int toolOffset = 20 * 3 * 24;
                const int masonryOffset = toolOffset + 5 * 3 * 24;
                const int paperOffset = masonryOffset + 4 * 3 * 24;
                const int waterOffset = paperOffset + 3 * 24;
                for (int worker = 1; worker < 3; worker++)
                {
                    int boot = 7 * 3 * 24 + (worker * 11 + 5) * 24;
                    for (int vertex = 0; vertex < 24; vertex++)
                        Check(SamePoint(first[boot + vertex], next[boot + vertex]),
                            "stationary task workers keep their feet planted rather than circle the building");
                }
                if (farming == 0)
                {
                    Check(Center(next, toolOffset + 6 * 24).y > Center(first, toolOffset + 6 * 24).y + .1f,
                        "hammer worker actually lifts and strikes");
                    Check(Center(next, paperOffset + 2 * 24).y > .9f,
                        "engineer has a held white paper plan");
                    Check(!SamePoint(Center(first, 11 * 24), Center(next, 11 * 24)),
                        "engineer's pointing hand changes while the plan stays held");
                    crew.Update(3);
                    var loaded = Copy(mesh.vertices);
                    Check(!SamePoint(Center(first, 5 * 24 + 7 * 3 * 24),
                        Center(loaded, 5 * 24 + 7 * 3 * 24)), "stone carrier travels during the loaded leg");
                    crew.Update(7);
                    Check(Span(mesh.vertices, masonryOffset, 24) < .01f,
                        "carrier returns without the masonry load");
                }
                else
                {
                    Check(!SamePoint(Center(first, masonryOffset + 24), Center(next, masonryOffset + 24)),
                        "farmer scatters separate moving seeds from the hand");
                    Check(!SamePoint(Center(first, waterOffset + 8 * 24), Center(next, waterOffset + 8 * 24)),
                        "watering can has a moving visible water stream");
                    Check(MinY(next, toolOffset + 11 * 24, 24) <= .015f,
                        "hoe blade reaches the soil while cultivating");
                    Check(Span(next, paperOffset + 2 * 24, 24) < .01f,
                        "agricultural sites do not show construction plans instead of farming tools");
                    for (int frame = 0; frame < 35; frame++)
                    {
                        crew.Update(.17f);
                        CheckPose(mesh, footprint, "agricultural pose");
                        CheckGroundFeet(mesh, "agricultural pose");
                        CheckTriangleIntegrity(mesh, "agricultural pose");
                    }
                }
                crew.SetPhase(CityConstructionPhase.Complete, true);
                Check(!root.gameObject.activeSelf, "both kinds of crew disappear on completion");
                var owned = root.GetChild(0).GetComponent<MeshRenderer>().sharedMaterials;
                crew.Dispose();
                for (int material = 5; material < 8; material++)
                    Check(owned[material].destroyed, "crew releases its own detail materials");
                UnityEngine.Object.Destroy(parent);
            }
            UnityEngine.Object.Destroy(skin);
            UnityEngine.Object.Destroy(clothing);
        }
    }

    private static Vector3 Center(Vector3[] vertices, int offset)
    {
        Vector3 point = Vector3.zero;
        for (int vertex = 0; vertex < 24; vertex++) point += vertices[offset + vertex];
        return point * (1f / 24);
    }
    private static bool SamePoint(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-9f;
    private static float Span(Vector3[] vertices, int offset, int count)
    {
        Vector3 center = Center(vertices, offset); float maximum = 0;
        for (int i = 0; i < count; i++) maximum = Math.Max(maximum, (vertices[offset + i] - center).magnitude);
        return maximum;
    }

    private static PhaseSnapshot CapturePhase(CityConstructionCrew crew, Mesh mesh,
        MeshRenderer renderer, Vector3 footprint, CityConstructionPhase phase)
    {
        crew.SetPhase(phase, true);
        var result = new PhaseSnapshot
        {
            phase = phase.ToString(),
            vertexCount = mesh.vertices.Length,
            trianglesByMaterial = new int[mesh.subMeshCount][],
            materialColors = new float[renderer.sharedMaterials.Length][]
        };
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            result.trianglesByMaterial[submesh] = mesh.GetTriangles(submesh);
            Check(result.trianglesByMaterial[submesh].Length > 0,
                phase + " has triangles in material group " + submesh);
        }
        for (int material = 0; material < renderer.sharedMaterials.Length; material++)
        {
            Color color = renderer.sharedMaterials[material].GetColor("_BaseColor");
            result.materialColors[material] = new[] { color.r, color.g, color.b, color.a };
        }

        result.frames = new PoseFrame[FramesPerPhase];
        for (int frame = 0; frame < FramesPerPhase; frame++)
        {
            if (frame > 0) crew.Update(.13f);
            CheckPose(mesh, footprint, phase + " frame " + frame);
            CheckGroundFeet(mesh, phase + " frame " + frame);
            CheckTriangleIntegrity(mesh, phase + " frame " + frame);
            result.frames[frame] = new PoseFrame
            {
                seconds = frame * .13f,
                vertices = PackVertices(mesh.vertices)
            };
        }
        Check(!ExactlyEqual(result.frames[0].vertices, result.frames[FramesPerPhase - 1].vertices),
            phase + " changes articulated pose and perimeter position across a full action cycle");
        return result;
    }

    private static void CheckPose(Mesh mesh, Vector3 footprint, string label)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            Check(IsFinite(vertices[i]), label + " vertex " + i + " remains finite");

        int skinLength = 4 * 3 * 24;
        int vestOffset = skinLength;
        int clothOffset = vestOffset + 3 * 3 * 24;
        int clothLength = 11 * 3 * 24;
        int helmetOffset = clothOffset + clothLength;
        float humanTop = float.NegativeInfinity;
        for (int group = 0; group < 4; group++)
        {
            int offset = group == 0 ? 0 : group == 1 ? vestOffset :
                group == 2 ? clothOffset : helmetOffset;
            int length = group == 0 ? skinLength : group == 1 ? 3 * 3 * 24 :
                 group == 2 ? clothLength : 6 * 24;
            for (int i = offset; i < offset + length; i++)
            {
                Vector3 point = vertices[i];
                humanTop = Math.Max(humanTop, point.y);
                bool insidePlot = Math.Abs(point.x) < footprint.x / CityConstructionCrew.ModelMetresToCity * .5f - .0001f &&
                    Math.Abs(point.z) < footprint.z / CityConstructionCrew.ModelMetresToCity * .5f - .0001f;
                Check(!insidePlot, label + " human/helmet vertices stay outside the plot footprint");
            }
        }
        Check(humanTop > 1.69f && humanTop < 1.76f,
            label + " hardhat/head silhouette measures approximately 1.7 physical metres");
        float worldHeight = humanTop * CityConstructionCrew.ModelMetresToCity;
        Check(worldHeight >= .084f && worldHeight <= .088f && worldHeight / .15f < .60f,
            label + " worker height is about 1.7m and less than sixty percent of a three-metre building storey");
    }

    private static void CheckGroundFeet(Mesh mesh, string label)
    {
        Vector3[] vertices = mesh.vertices;
        const int clothOffset = (4 + 3) * 3 * 24;
        const int clothBoxesPerWorker = 11;
        for (int worker = 0; worker < 3; worker++)
        {
            for (int boot = 5; boot <= 6; boot++)
            {
                int offset = clothOffset + (worker * clothBoxesPerWorker + boot) * 24;
                float lowest = float.PositiveInfinity;
                for (int i = offset; i < offset + 24; i++) lowest = Math.Min(lowest, vertices[i].y);
                Check(lowest >= -.00001f && lowest <= .00001f,
                    label + " worker " + worker + " boot vertices stay on the ground");
            }
        }
    }

    private static void CheckGroundTool(Mesh mesh, CityConstructionPhase phase)
    {
        Vector3[] vertices = mesh.vertices;
        int toolOffset = (4 + 3 + 11 + 2) * 3 * 24;
        float legBase = MinY(vertices, toolOffset + (5 + 3) * 24, 24);
        Check(legBase >= -.00001f && legBase <= .01f,
            phase + " hammer worker's workbench stands on the ground");
    }

    private static void CheckTriangleIntegrity(Mesh mesh, string label)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        for (int vertex = 0; vertex < normals.Length; vertex++)
            Check(IsFinite(normals[vertex]) && normals[vertex].sqrMagnitude > .5f,
                label + " recalculated mesh normal " + vertex + " is finite and nonzero");
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Check(a >= 0 && b >= 0 && c >= 0 &&
                    a < vertices.Length && b < vertices.Length && c < vertices.Length,
                    label + " triangle indices remain within the shared dynamic vertex buffer");
                Vector3 area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                Check(IsFinite(area) && area.sqrMagnitude > 1e-18f,
                    label + " triangle " + (i / 3) + " is finite and nondegenerate");
            }
        }
    }

    private static Mesh FindCrewMesh(Transform root)
    {
        if (root.childCount != 1) throw new InvalidOperationException("Crew root should contain one render object.");
        MeshFilter filter = root.GetChild(0).GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            throw new InvalidOperationException("Crew render object must reference its actual dynamic mesh.");
        return filter.sharedMesh;
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var material = new Material(Shader.Find("NewGaza/Construction fixture"))
        {
            name = name
        };
        material.SetColor("_BaseColor", color);
        return material;
    }

    private static Vector3[] Copy(Vector3[] source) { return (Vector3[])source.Clone(); }

    private static float[] PackVertices(Vector3[] source)
    {
        var packed = new float[source.Length * 3];
        for (int i = 0; i < source.Length; i++)
        {
            packed[i * 3] = source[i].x;
            packed[i * 3 + 1] = source[i].y;
            packed[i * 3 + 2] = source[i].z;
        }
        return packed;
    }

    private static float MinY(Vector3[] vertices, int offset, int length)
    {
        float result = float.PositiveInfinity;
        for (int i = offset; i < offset + length; i++) result = Math.Min(result, vertices[i].y);
        return result;
    }

    private static bool ExactlyEqual(Vector3[] a, Vector3[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i].x != b[i].x || a[i].y != b[i].y || a[i].z != b[i].z) return false;
        return true;
    }

    private static bool ExactlyEqual(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
    private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

    private static void WritePoseFixture()
    {
        var payload = new PoseFixture
        {
            title = "Construction crew source-mesh motion capture fixture",
            source = "Generated by executing CityConstructionCrew and CityConstructionWorkerRig with a deterministic Unity API fixture; not an offline illustration.",
            coordinateUnits = "physical metres in plot-local X,Y,Z; plot centered at origin",
            phases = CapturedPhases
        };
        JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(payload, options);
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "testingReplic")))
            directory = directory.Parent;
        if (directory == null) throw new DirectoryNotFoundException("Could not locate repository root for crew fixture export.");
        string outputDirectory = Path.Combine(directory.FullName, "exports", "housing-assets");
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "Construction-Crew-Frames.json");
        File.WriteAllText(path, json);
        Console.WriteLine("CREW_FIXTURE " + path + " (" + new FileInfo(path).Length + " bytes)");
    }

    private static PhaseSnapshot[] CapturedPhases;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Construction crew check failed: " + message);
    }

    private sealed class PoseFixture
    {
        public string title;
        public string source;
        public string coordinateUnits;
        public PhaseSnapshot[] phases;
    }

    private sealed class PhaseSnapshot
    {
        public string phase;
        public int vertexCount;
        public int[][] trianglesByMaterial;
        public float[][] materialColors;
        public PoseFrame[] frames;
    }

    private sealed class PoseFrame
    {
        public float seconds;
        public float[] vertices;
    }
}