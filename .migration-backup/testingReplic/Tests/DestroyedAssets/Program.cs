using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NewGaza;

internal static class Program
{
    private static readonly List<string> failures = new List<string>();
    private static readonly string[] districts =
    {
        "shujaiya", "tuffah", "sheikh-radwan", "daraj", "karama", "old-city",
        "nasr", "sabra", "zeitoun", "rimal", "tel-al-hawa", "sheikh-ijlin", "rashid"
    };
    private static readonly string[] destroyedKeys =
    {
        "ruin_shujaiya", "ruin_tuffah", "ruin_sheikh_radwan", "ruin_daraj",
        "ruin_karama", "ruin_old_city", "ruin_nasr", "ruin_sabra",
        "ruin_zeitoun", "ruin_rimal", "ruin_tel_al_hawa", "ruin_sheikh_ijlin",
        "ruin_rashid", "ruin_mosque", "ruin_school", "ruin_clinic", "ruin_civic",
        "ruin_wall", "ruin_car", "ruin_crater", "ruin_debris"
    };
    private static readonly byte[] pngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly byte[] fbxBinarySignature =
        Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");

    private static int Main(string[] args)
    {
        string root = FindProjectRoot(args);
        CheckProfiles();
        CheckRuntimeImportAllowlist(root);
        CheckAssets(root);
        if (failures.Count > 0)
        {
            Console.Error.WriteLine("Destroyed asset checks FAILED (" + failures.Count + "):");
            foreach (string failure in failures) Console.Error.WriteLine("  - " + failure);
            return 1;
        }
        Console.WriteLine("Destroyed asset checks passed: 13 stable district profiles, 21 site models and four destroyed-only context LOD asset sets.");
        return 0;
    }

    private static void CheckProfiles()
    {
        Require(CityRuinProfiles.Count == districts.Length, "Expected exactly 13 stable regional profiles.");
        Require(CityRuinProfiles.ModelKeys.Length == destroyedKeys.Length &&
            destroyedKeys.SequenceEqual(CityRuinProfiles.ModelKeys, StringComparer.Ordinal),
            "CityRuinProfiles.ModelKeys must be the exact ordered 21-resource runtime inventory.");
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (string district in districts)
        {
            CityRuinProfiles.Profile profile = CityRuinProfiles.ForDistrict(district);
            Require(profile.districtId == district, "Profile lookup did not preserve stable district ID " + district + ".");
            Require(profile.modelKey.StartsWith("ruin_", StringComparison.Ordinal) &&
                profile.maxHeightCityUnits > 0f && profile.maxHeightCityUnits <= 2f &&
                mapped.Add(profile.modelKey),
                "Regional profile is invalid or duplicated for " + district + ".");
            Require(CityRuinProfiles.StageZeroModel(district, "housing") == profile.modelKey,
                "Ordinary stage-zero plots must preserve the dominant regional profile in " + district + ".");
            Require(CityRuinProfiles.StageZeroDetail(district, 0).StartsWith("ruin_", StringComparison.Ordinal),
                "No deterministic stage-zero parcel detail is mapped for " + district + ".");
        }
        RequireThrows(() => CityRuinProfiles.ForDistrict("unknown"), "Unknown district IDs must fail explicitly.");

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (string district in districts)
        {
            reachable.Add(CityRuinProfiles.StageZeroModel(district, "housing"));
            reachable.Add(CityRuinProfiles.StageZeroModel(district, "services"));
            for (int plot = 0; plot < 4; plot++)
                reachable.Add(CityRuinProfiles.StageZeroDetail(district, plot));
        }
        Require(destroyedKeys.All(reachable.Contains),
            "Every shipped destroyed model must have a reachable stage-zero selection: " +
            string.Join(", ", destroyedKeys.Where(key => !reachable.Contains(key))));
        Require(destroyedKeys.Length == 21 && reachable.Count == destroyedKeys.Length &&
            destroyedKeys.All(reachable.Contains),
            "The stage-zero profile/detail selectors must cover exactly the 21 destroyed model keys.");
    }

    private static void CheckAssets(string root)
    {
        string models = Path.Combine(root, "Assets", "NewGaza", "Resources", "Models");
        string fbxDirectory = Path.Combine(root, "Assets", "NewGaza", "Art", "DestroyedFBX");
        var measuredHeights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string key in destroyedKeys.Concat(CityRuinProfiles.ContextModelKeys))
        {
            string objPath = Path.Combine(models, key + ".obj");
            string atlasPath = Path.Combine(models, key + "_albedo.png");
            string manifestPath = Path.Combine(models, key + "_manifest.json");
            string fbxPath = Path.Combine(fbxDirectory, key + ".fbx");
            double? objHeight = CheckObj(objPath, key);
            if (objHeight.HasValue) measuredHeights[key] = objHeight.Value;
            CheckPng(atlasPath, key);
            CheckManifest(manifestPath, key);
            CheckFbx(fbxPath, key);
            Require(!File.Exists(objPath + ".meta") && !File.Exists(atlasPath + ".meta") &&
                !File.Exists(manifestPath + ".meta") && !File.Exists(fbxPath + ".meta"),
                "Destroyed asset outputs must not depend on checked-in Unity .meta files: " + key + ".");
        }
        foreach (CityRuinProfiles.Profile profile in CityRuinProfiles.Profiles)
        {
            if (!measuredHeights.TryGetValue(profile.modelKey, out double measuredHeight)) continue;
            Require(Math.Abs(profile.maxHeightCityUnits - measuredHeight / 20d) <= .00006d,
                "Regional max height must match the generated OBJ's measured vertical extent converted at 20 m per city unit: " +
                profile.modelKey + ".");
        }
    }

    private static void CheckRuntimeImportAllowlist(string root)
    {
        string sourceRoot = Path.Combine(root, "Assets", "NewGaza");
        string editorPath = Path.Combine(sourceRoot, "Editor", "CityModelImportSettings.cs");
        string libraryPath = Path.Combine(sourceRoot, "World", "CityModelLibrary.cs");
        if (!File.Exists(editorPath) || !File.Exists(libraryPath))
        {
            Require(false, "Missing native Resources/Models importer or runtime model library.");
            return;
        }
        string importer = File.ReadAllText(editorPath).Replace(" ", "").Replace("\r", "").Replace("\n", "");
        string library = File.ReadAllText(libraryPath).Replace(" ", "").Replace("\r", "").Replace("\n", "");
        Require(importer.Contains("ModelsPrefix=\"Assets/NewGaza/Resources/Models/\"", StringComparison.Ordinal) &&
            importer.Contains("path.StartsWith(ModelsPrefix,StringComparison.Ordinal)&&path.EndsWith(\".obj\",StringComparison.OrdinalIgnoreCase)",
                StringComparison.Ordinal) &&
            !importer.Contains(".fbx", StringComparison.OrdinalIgnoreCase),
            "The native Unity model importer must be restricted to runtime OBJ assets below Resources/Models.");
        Require(destroyedKeys.All(key => importer.Contains("\"" + key + "\"", StringComparison.Ordinal)),
            "All destroyed model OBJ/albedo keys must be included in the scoped import allowlist.");
        Require(library.Contains("stringresourcePath=\"Models/\"+key", StringComparison.Ordinal) &&
            library.Contains("Resources.Load<GameObject>(resourcePath)", StringComparison.Ordinal) &&
            library.Contains("Resources.Load<Texture2D>(textureResourcePath+\"_albedo\")", StringComparison.Ordinal),
            "Runtime destroyed models must load Resources OBJ models and their individual albedo atlases by key.");
    }

    private static double? CheckObj(string path, string key)
    {
        if (!File.Exists(path))
        {
            Require(false, "Missing runtime OBJ for " + key + ": " + path);
            return null;
        }
        int vertices = 0, uvs = 0, normals = 0, triangles = 0;
        double[] minimum = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        double[] maximum = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        try
        {
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields[0] == "v" && fields.Length >= 4)
                {
                    double x = Number(fields[1]), y = Number(fields[2]), z = Number(fields[3]);
                    double[] value = { x, y, z };
                    for (int axis = 0; axis < 3; axis++)
                    {
                        minimum[axis] = Math.Min(minimum[axis], value[axis]);
                        maximum[axis] = Math.Max(maximum[axis], value[axis]);
                    }
                    vertices++;
                }
                else if (fields[0] == "vt" && fields.Length >= 3)
                {
                    Number(fields[1]); Number(fields[2]); uvs++;
                }
                else if (fields[0] == "vn" && fields.Length >= 4)
                {
                    double nx = Number(fields[1]), ny = Number(fields[2]), nz = Number(fields[3]);
                    Require(nx * nx + ny * ny + nz * nz > 1e-10,
                        "OBJ has a zero-length imported normal: " + key + ".");
                    normals++;
                }
                else if (fields[0] == "f")
                {
                    Require(fields.Length == 4, "OBJ contains a non-triangle face: " + key + ".");
                    triangles += fields.Length == 4 ? 1 : 0;
                    for (int i = 1; i < fields.Length; i++)
                    {
                        string[] indices = fields[i].Split('/');
                        Require(indices.Length == 3 && indices.All(index => index.Length > 0),
                            "Every OBJ face corner must retain position/UV/normal indices: " + key + ".");
                        if (indices.Length != 3 || indices.Any(index => index.Length == 0)) continue;
                        CheckObjIndex(indices[0], vertices, key);
                        CheckObjIndex(indices[1], uvs, key);
                        CheckObjIndex(indices[2], normals, key);
                    }
                }
            }
        }
        catch (Exception error)
        {
            Require(false, "Could not validate OBJ " + key + ": " + error.Message);
            return null;
        }
        Require(vertices > 0 && uvs > 0 && normals > 0 && triangles > 0,
            "OBJ must include triangulated geometry, normals, and UV coordinates: " + key + ".");
        Require(Enumerable.Range(0, 3).All(axis =>
                !double.IsInfinity(minimum[axis]) && maximum[axis] - minimum[axis] > 1e-6),
            "OBJ must have nonzero finite bounds on all axes: " + key + ".");
        return maximum[1] - minimum[1];
    }

    private static void CheckObjIndex(string value, int available, string key)
    {
        int index = int.Parse(value, CultureInfo.InvariantCulture);
        int resolved = index > 0 ? index : available + index + 1;
        Require(index != 0 && resolved > 0 && resolved <= available,
            "OBJ face references an invalid/out-of-order index for " + key + ".");
    }

    private static double Number(string value)
    {
        double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new InvalidDataException("Non-finite OBJ number.");
        return number;
    }

    private static void CheckPng(string path, string key)
    {
        if (!File.Exists(path))
        {
            Require(false, "Missing runtime albedo atlas for " + key + ": " + path);
            return;
        }
        bool valid = false;
        try
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                valid = reader.ReadBytes(pngSignature.Length).SequenceEqual(pngSignature);
                bool hasHeader = false, hasImageData = false, hasEnd = false;
                while (valid && reader.BaseStream.Position + 12 <= reader.BaseStream.Length)
                {
                    uint length = ReadBigEndianUInt32(reader);
                    if (length > reader.BaseStream.Length - reader.BaseStream.Position - 8)
                    {
                        valid = false;
                        break;
                    }
                    string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (chunk == "IHDR")
                    {
                        valid &= length == 13;
                        uint width = ReadBigEndianUInt32(reader);
                        uint height = ReadBigEndianUInt32(reader);
                        valid &= width > 0 && height > 0;
                        reader.BaseStream.Seek(5, SeekOrigin.Current);
                        hasHeader = true;
                    }
                    else
                    {
                        if (chunk == "IDAT") hasImageData = true;
                        if (chunk == "IEND") hasEnd = length == 0;
                        reader.BaseStream.Seek(length, SeekOrigin.Current);
                    }
                    reader.BaseStream.Seek(4, SeekOrigin.Current);
                    if (hasEnd) break;
                }
                valid &= hasHeader && hasImageData && hasEnd;
            }
        }
        catch (Exception)
        {
            valid = false;
        }
        Require(valid, "Albedo atlas is not a complete PNG with image data: " + key + ".");
    }

    private static uint ReadBigEndianUInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4) throw new EndOfStreamException();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) | bytes[3];
    }

    private static void CheckManifest(string path, string key)
    {
        if (!File.Exists(path))
        {
            Require(false, "Missing generated runtime manifest for " + key + ": " + path);
            return;
        }
        try
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                string json = document.RootElement.GetRawText();
                Require(json.IndexOf(key, StringComparison.Ordinal) >= 0,
                    "Manifest must identify its generated asset key: " + key + ".");
                Require(json.IndexOf(".meta", StringComparison.OrdinalIgnoreCase) < 0,
                    "Manifest must not encode Unity .meta/GUID dependencies: " + key + ".");
            }
        }
        catch (Exception error)
        {
            Require(false, "Manifest is not valid JSON for " + key + ": " + error.Message);
        }
    }

    private static void CheckFbx(string path, string key)
    {
        if (!File.Exists(path))
        {
            Require(false, "Missing editable destroyed FBX master for " + key + ": " + path);
            return;
        }
        byte[] prefix = new byte[4096];
        int read;
        using (var stream = File.OpenRead(path)) read = stream.Read(prefix, 0, prefix.Length);
        bool binary = read >= fbxBinarySignature.Length &&
            prefix.Take(fbxBinarySignature.Length).SequenceEqual(fbxBinarySignature);
        string text = Encoding.UTF8.GetString(prefix, 0, read);
        bool ascii = text.StartsWith("; FBX", StringComparison.Ordinal) &&
            text.Contains("FBXHeaderExtension", StringComparison.Ordinal);
        Require(binary || ascii, "FBX master must contain a genuine FBX binary or ASCII header: " + key + ".");
    }

    private static string FindProjectRoot(string[] args)
    {
        if (args.Length > 0 && Directory.Exists(args[0])) return Path.GetFullPath(args[0]);
        string nestedProject = Path.Combine(Environment.CurrentDirectory, "testingReplic");
        if (Directory.Exists(Path.Combine(nestedProject, "Assets", "NewGaza", "Resources", "Models")))
            return nestedProject;
        for (DirectoryInfo directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets", "NewGaza", "Resources", "Models")))
                return directory.FullName;
        throw new DirectoryNotFoundException(
            "Run from the testingReplic project root or pass that root as the first argument.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }

    private static void RequireThrows(Action action, string message)
    {
        try { action(); Require(false, message); }
        catch (ArgumentException) { }
    }
}