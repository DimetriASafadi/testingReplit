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
            ModelsPrefix + "rubble_heap.obj"
        };
        private static readonly string[] TexturePaths =
        {
            ModelsPrefix + "apartment_albedo.png",
            ModelsPrefix + "ruined_building_albedo.png",
            ModelsPrefix + "rubble_heap_albedo.png"
        };

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

            if (success)
                Debug.Log("New Gaza imported model assets are prepared. Resource names remain apartment, ruined_building, rubble_heap and their *_albedo textures.");
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