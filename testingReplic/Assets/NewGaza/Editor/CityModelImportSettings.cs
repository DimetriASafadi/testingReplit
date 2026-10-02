using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Applies the runtime mesh and texture import contract to the city model assets.</summary>
    internal sealed class CityModelImportSettings : AssetPostprocessor
    {
        private const string ModelsPrefix = "Assets/NewGaza/Resources/Models/";
        private static readonly string[] ModelPaths =
        {
            ModelsPrefix + "apartment.obj",
            ModelsPrefix + "ruined_building.obj",
            ModelsPrefix + "rubble_heap.obj",
            ModelsPrefix + "apartment_context.obj",
            ModelsPrefix + "ruined_building_context.obj",
            ModelsPrefix + "ruin_shujaiya.obj",
            ModelsPrefix + "ruin_tuffah.obj",
            ModelsPrefix + "ruin_sheikh_radwan.obj",
            ModelsPrefix + "ruin_daraj.obj",
            ModelsPrefix + "ruin_karama.obj",
            ModelsPrefix + "ruin_old_city.obj",
            ModelsPrefix + "ruin_nasr.obj",
            ModelsPrefix + "ruin_sabra.obj",
            ModelsPrefix + "ruin_zeitoun.obj",
            ModelsPrefix + "ruin_rimal.obj",
            ModelsPrefix + "ruin_tel_al_hawa.obj",
            ModelsPrefix + "ruin_sheikh_ijlin.obj",
            ModelsPrefix + "ruin_rashid.obj",
            ModelsPrefix + "ruin_mosque.obj",
            ModelsPrefix + "ruin_school.obj",
            ModelsPrefix + "ruin_clinic.obj",
            ModelsPrefix + "ruin_civic.obj",
            ModelsPrefix + "ruin_wall.obj",
            ModelsPrefix + "ruin_car.obj",
            ModelsPrefix + "ruin_crater.obj",
            ModelsPrefix + "ruin_debris.obj"
        };
        private static readonly string[] TexturePaths =
        {
            ModelsPrefix + "apartment_albedo.png",
            ModelsPrefix + "ruined_building_albedo.png",
            ModelsPrefix + "rubble_heap_albedo.png"
        };
        private static readonly string[] DestroyedModelKeys =
        {
            "ruin_shujaiya", "ruin_tuffah", "ruin_sheikh_radwan", "ruin_daraj",
            "ruin_karama", "ruin_old_city", "ruin_nasr", "ruin_sabra",
            "ruin_zeitoun", "ruin_rimal", "ruin_tel_al_hawa", "ruin_sheikh_ijlin",
            "ruin_rashid", "ruin_mosque", "ruin_school", "ruin_clinic", "ruin_civic",
            "ruin_wall", "ruin_car", "ruin_crater", "ruin_debris"
        };
        private static readonly string[] DestroyedTexturePaths = BuildDestroyedTexturePaths();

        private void OnPreprocessModel()
        {
            if (!IsCityObj(assetPath)) return;
            Apply((ModelImporter)assetImporter);
        }

        private void OnPreprocessTexture()
        {
            if (!IsCityAlbedo(assetPath)) return;
            Apply((TextureImporter)assetImporter);
        }

        [MenuItem("New Gaza/Prepare imported city models", priority = 3)]
        private static void PrepareImportedCityModels()
        {
            bool success = true;
            foreach (string path in ModelPaths) success &= PrepareModel(path);
            foreach (string path in TexturePaths) success &= PrepareTexture(path);
            foreach (string path in DestroyedTexturePaths) success &= PrepareTexture(path);
            foreach (string key in CityHousingProfiles.ModelKeys)
            {
                success &= PrepareModel(ModelsPrefix + key + ".obj");
                success &= PrepareTexture(ModelsPrefix + key + "_albedo.png");
            }

            if (success)
                Debug.Log("New Gaza imported model assets are prepared. Original apartment, ruined_building, rubble_heap, and context resources remain intact; destroyed stage-zero profiles and details use their own Resources/Models OBJ and albedo pairs.");
        }

        private static bool PrepareModel(string path)
        {
            if (!EnsureImported(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("Expected a Unity ModelImporter for " + path + ".");
                return false;
            }
            if (Apply(importer)) importer.SaveAndReimport();
            return true;
        }

        private static bool PrepareTexture(string path)
        {
            if (!EnsureImported(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("Expected a Unity TextureImporter for " + path + ".");
                return false;
            }
            if (Apply(importer)) importer.SaveAndReimport();
            return true;
        }

        private static bool EnsureImported(string path)
        {
            string projectRelativePath = path.Substring("Assets/".Length)
                .Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(Application.dataPath, projectRelativePath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError("Missing New Gaza imported city asset: " + path +
                    ". Add the converted source asset before preparing the models.");
                return false;
            }
            if (AssetImporter.GetAtPath(path) == null)
                AssetDatabase.ImportAsset(path);
            return true;
        }

        private static bool IsCityObj(string path) =>
            path.StartsWith(ModelsPrefix, StringComparison.Ordinal) &&
            path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);

        private static string[] BuildDestroyedTexturePaths()
        {
            var paths = new string[21];
            for (int i = 0; i < paths.Length; i++)
                paths[i] = ModelsPrefix + DestroyedModelKeys[i] + "_albedo.png";
            return paths;
        }

        private static bool IsCityAlbedo(string path) =>
            path.StartsWith(ModelsPrefix, StringComparison.Ordinal) &&
            path.EndsWith("_albedo.png", StringComparison.OrdinalIgnoreCase);

        private static bool Apply(ModelImporter importer)
        {
            bool changed = false;
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            if (importer.importNormals != ModelImporterNormals.Import)
            {
                importer.importNormals = ModelImporterNormals.Import;
                changed = true;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }
            return changed;
        }

        private static bool Apply(TextureImporter importer)
        {
            bool changed = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }
            if (!importer.sRGBTexture) { importer.sRGBTexture = true; changed = true; }
            if (importer.maxTextureSize != 1024) { importer.maxTextureSize = 1024; changed = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Compressed)
            {
                importer.textureCompression = TextureImporterCompression.Compressed;
                changed = true;
            }
            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                changed = true;
            }
            return changed;
        }
    }
}