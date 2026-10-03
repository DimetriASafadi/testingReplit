using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>
    /// Real JsonUtility/Resources integration in Edit Mode only. No scene loading,
    /// GameSession/CityWorld creation, PlayerPrefs, save IO or geometry generation.
    /// </summary>
    public static class NewGazaBasemapGate
    {
        private const string AssetPath = "Assets/NewGaza/Resources/GazaBasemap.json";
        private const string Metadata =
            "\"schemaVersion\":1,\"metadata\":{\"projection\":{\"originLatitude\":31.515," +
            "\"originLongitude\":34.45,\"unitsPerKilometre\":50}}";
        private const string Legacy =
            "\"schema\":1,\"origin\":{\"latitude\":31.515,\"longitude\":34.45},\"unitsPerKm\":50";
        private const string Flat =
            "\"schema\":1,\"originLatitude\":31.515,\"originLongitude\":34.45,\"unitsPerKm\":50";
        // Small in-memory features test header compatibility, not surveyed/source geometry.
        private const string Features =
            ",\"roads\":[{\"id\":1,\"kind\":\"residential\",\"width\":1," +
            "\"points\":[{\"x\":0,\"z\":0},{\"x\":1,\"z\":1}]}]," +
            "\"buildings\":[{\"id\":2,\"center\":{\"x\":0,\"z\":0}," +
            "\"size\":{\"x\":1,\"z\":1},\"height\":1,\"outline\":[" +
            "{\"x\":0,\"z\":0},{\"x\":1,\"z\":0},{\"x\":0,\"z\":1}]}]," +
            "\"areas\":[{\"id\":3,\"kind\":\"park\",\"points\":[" +
            "{\"x\":0,\"z\":0},{\"x\":1,\"z\":0},{\"x\":0,\"z\":1}]}]";

        [Serializable]
        private sealed class CheckResult
        {
            public string name;
            public bool success;
            public string message;
        }

        [Serializable]
        private sealed class Result
        {
            public string nonce;
            public string unityVersion;
            public string runtimeAssembly;
            public string editorAssembly;
            public string scope = "basemap-integration";
            public string status = "failed";
            public string resourceAssetPath;
            public bool performedInEditMode;
            public bool success;
            public string message;
            public CheckResult[] checks;
        }

        public static void Run()
        {
            var checks = new List<CheckResult>();
            var result = new Result { unityVersion = Application.unityVersion };
            try
            {
                result.nonce = Argument("-newGazaCompilationNonce");
                string expected = Argument("-newGazaRequiredVersion");
                if (Application.unityVersion != expected)
                    throw new InvalidOperationException("Required Unity " + expected +
                        ", running " + Application.unityVersion);
                if (EditorApplication.isPlayingOrWillChangePlaymode ||
                    EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Gate requires compiled Edit Mode; Play Mode is forbidden.");
                result.performedInEditMode = true;
                result.runtimeAssembly = typeof(CityBasemap).Assembly.GetName().Name;
                result.editorAssembly = typeof(NewGazaBasemapGate).Assembly.GetName().Name;
                if (result.runtimeAssembly != "Assembly-CSharp" ||
                    result.editorAssembly != "Assembly-CSharp-Editor")
                    throw new InvalidOperationException("Runtime/Editor assembly boundary changed.");

                Check(checks, "actual-resources", () =>
                {
                    TextAsset asset = Resources.Load<TextAsset>("GazaBasemap");
                    Require(asset != null, "Resources/GazaBasemap TextAsset missing.");
                    result.resourceAssetPath = AssetDatabase.GetAssetPath(asset);
                    Require(result.resourceAssetPath == AssetPath,
                        "Resources resolved a different asset: " + result.resourceAssetPath);
                    // Inspect the real engine's metadata fields, independently of effective accessors.
                    CityBasemap raw = JsonUtility.FromJson<CityBasemap>(asset.text);
                    Require(raw != null && raw.metadata != null && raw.metadata.projection != null,
                        "JsonUtility did not deserialize sourced projection metadata.");
                    Require(Math.Abs(raw.metadata.projection.originLatitude - 31.515) < .00001 &&
                        Math.Abs(raw.metadata.projection.originLongitude - 34.45) < .00001 &&
                        Math.Abs(raw.metadata.projection.unitsPerKilometre - 50f) < .001f,
                        "Raw JsonUtility projection differs from sourced coordinates.");
                    CityBasemap map = CityBasemap.LoadFromResources();
                    Projection(map);
                    Require(map.schemaVersion == 1 && map.roads.Length == 4032 &&
                        map.buildings.Length == 12000 && map.areas.Length == 528,
                        "Sourced feature counts/schema differ from Tests/Basemap/Program.cs.");
                    Require(map.EffectiveAttribution.Contains("OpenStreetMap") &&
                        map.EffectiveSourceUrl.Contains("openstreetmap.org"),
                        "Source attribution/URL missing.");
                    Require(map.actualBounds.minX < -227f && map.actualBounds.maxX > 189f &&
                        map.actualBounds.minZ < -222f && map.actualBounds.maxZ > 261f,
                        "Measured feature extent differs from source fixture contract.");
                });
                Accept(checks, "metadata-only", Metadata);
                Accept(checks, "explicit-legacy", Legacy);
                Accept(checks, "flat-header", Flat);
                Accept(checks, "flat-long-scale", Flat.Replace("unitsPerKm", "unitsPerKilometre"));
                Accept(checks, "nested-origin-ignored", Metadata.Replace("\"projection\":",
                    "\"origin\":{\"latitude\":0,\"longitude\":0},\"projection\":"));
                Accept(checks, "escaped-string-ignored", Metadata +
                    ",\"note\":\"\\\"origin\\\":{\\\"latitude\\\":0,\\\"longitude\\\":0}\"");
                Accept(checks, "nested-metadata-ignored", Flat +
                    ",\"note\":{\"metadata\":{\"projection\":{\"originLatitude\":0,\"originLongitude\":0}}}");
                Reject(checks, "wrong-metadata-latitude", Metadata.Replace("31.515", "31.600"), "origin must be");
                Reject(checks, "wrong-metadata-longitude", Metadata.Replace("34.45", "34.60"), "origin must be");
                Reject(checks, "wrong-legacy-origin", Legacy.Replace("31.515", "0"), "origin must be");
                Reject(checks, "wrong-flat-origin", Flat.Replace("34.45", "0"), "origin must be");
                Reject(checks, "legacy-overrides-metadata", Metadata +
                    ",\"origin\":{\"latitude\":0,\"longitude\":0}", "origin must be");
                Reject(checks, "nested-origin-not-header",
                    "\"schema\":1,\"unitsPerKm\":50,\"note\":{\"origin\":{\"latitude\":31.515,\"longitude\":34.45}}",
                    "origin must be");
                Reject(checks, "nested-metadata-not-header",
                    "\"schemaVersion\":1,\"note\":{" + Metadata + "}", "origin must be");
                Reject(checks, "escaped-origin-not-header",
                    "\"schema\":1,\"unitsPerKm\":50,\"note\":\"\\\"origin\\\":{\\\"latitude\\\":31.515,\\\"longitude\\\":34.45}\"",
                    "origin must be");
                Reject(checks, "escaped-metadata-not-header",
                    "\"schemaVersion\":1,\"note\":\"\\\"metadata\\\":{\\\"projection\\\":{\\\"originLatitude\\\":31.515," +
                    "\\\"originLongitude\\\":34.45,\\\"unitsPerKilometre\\\":50}}\"", "origin must be");
                Reject(checks, "escaped-origin-key-not-header",
                    "\"schema\":1,\"unitsPerKm\":50,\"\\u006frigin\":{\"latitude\":31.515,\"longitude\":34.45}",
                    "city basemap");
                Reject(checks, "escaped-metadata-key-not-header",
                    Metadata.Replace("\"metadata\"", "\"\\u006detadata\""), "city basemap");
                Reject(checks, "array-nested-origin-not-header",
                    "\"schema\":1,\"unitsPerKm\":50,\"notes\":[{\"origin\":{\"latitude\":31.515,\"longitude\":34.45}}]",
                    "origin must be");
                Reject(checks, "wrong-scale", Metadata.Replace(":50", ":51"), "unitsPerKm must be 50");
                Reject(checks, "wrong-schema", Metadata.Replace(":1,", ":2,"), "schemaVersion must be 1");
                Check(checks, "missing-roads", () => MustReject(
                    "{" + Metadata + ",\"buildings\":[],\"areas\":[]}", "roads is missing"));
                Check(checks, "empty-json", () => MustReject("", "is empty"));
                Check(checks, "malformed-json", () => MustReject("{", "city basemap"));
                Check(checks, "missing-resource", () =>
                {
                    try { CityBasemap.LoadFromResources("__NewGazaMissingBasemapGateFixture__"); }
                    catch (InvalidOperationException error)
                    {
                        Require(error.Message.Contains("Missing required city basemap"), error.Message);
                        return;
                    }
                    throw new InvalidOperationException("Missing Resources asset unexpectedly loaded.");
                });
                result.success = checks.TrueForAll(check => check.success);
                result.status = result.success ? "passed" : "failed";
                result.message = result.success ? "Real Unity basemap checks passed; no Play Mode or game saves accessed."
                    : "Basemap integration checks failed; see checks and Editor.log.";
            }
            catch (Exception error)
            {
                result.message = error.ToString();
                Debug.LogException(error);
            }
            result.checks = checks.ToArray();
            try
            {
                File.WriteAllText(Argument("-newGazaCompilationResult"), JsonUtility.ToJson(result, true));
            }
            catch (Exception error)
            {
                result.success = false;
                Debug.LogException(error);
            }
            Debug.Log((result.success ? "NEW_GAZA_BASEMAP_PASS: " : "NEW_GAZA_BASEMAP_FAIL: ") +
                result.message + " Unity " + Application.unityVersion);
            EditorApplication.Exit(result.success ? 0 : 1);
        }

        private static void Accept(List<CheckResult> checks, string name, string header)
        {
            Check(checks, name, () => Projection(CityBasemap.ParseAndValidate(
                "{" + header + Features + "}", name)));
        }

        private static void Reject(List<CheckResult> checks, string name, string header, string diagnostic)
        {
            Check(checks, name, () => MustReject("{" + header + Features + "}", diagnostic));
        }

        private static void MustReject(string json, string diagnostic)
        {
            try { CityBasemap.ParseAndValidate(json, "Unity gate fixture"); }
            catch (InvalidOperationException error)
            {
                Require(error.Message.Contains(diagnostic), "Wrong rejection diagnostic: " + error.Message);
                return;
            }
            throw new InvalidOperationException("Invalid fixture unexpectedly accepted.");
        }

        private static void Projection(CityBasemap map)
        {
            Require(Math.Abs(map.EffectiveOriginLatitude - 31.515) < .00001 &&
                Math.Abs(map.EffectiveOriginLongitude - 34.45) < .00001 &&
                Math.Abs(map.EffectiveUnitsPerKilometre - 50f) < .001f,
                "Expected projection 31.515/34.45 at scale 50.");
        }

        private static void Check(List<CheckResult> checks, string name, Action action)
        {
            var check = new CheckResult { name = name };
            try { action(); check.success = true; check.message = "PASS"; }
            catch (Exception error) { check.message = error.ToString(); }
            checks.Add(check);
            Debug.Log("NEW_GAZA_BASEMAP_CASE " + name + ": " +
                (check.success ? "PASS" : "FAIL") + " " + check.message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing batch argument " + name);
            return args[index + 1];
        }
    }
}