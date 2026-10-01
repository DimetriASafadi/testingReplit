using UnityEditor;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Applies bounded, mobile-friendly settings only to authored city audio.</summary>
    public sealed class CityAudioImportSettings : AssetPostprocessor
    {
        private const string AudioFolder = "Assets/NewGaza/Resources/Audio/";

        private void OnPreprocessAudio()
        {
            if (assetPath == null || !assetPath.StartsWith(AudioFolder,
                    System.StringComparison.OrdinalIgnoreCase)) return;

            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            importer.preloadAudioData = true;
            importer.loadInBackground = false;

            var settings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate,
                sampleRateOverride = 24000,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.6f
            };
            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings("Android", settings);
            importer.SetOverrideSampleSettings("iPhone", settings);
        }
    }
}