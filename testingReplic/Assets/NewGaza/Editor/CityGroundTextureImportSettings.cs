using UnityEditor;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Only the three authored ground albedos; leaves all other art untouched.</summary>
    public sealed class CityGroundTextureImportSettings : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetPath == null || !assetPath.StartsWith(
                "Assets/NewGaza/Resources/Ground/", System.StringComparison.OrdinalIgnoreCase) ||
                !assetPath.EndsWith("_Albedo.png", System.StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 1024,
                format = TextureImporterFormat.ETC2_RGB4, compressionQuality = 70
            });
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "iPhone", overridden = true, maxTextureSize = 1024,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 70
            });
        }
    }
}
