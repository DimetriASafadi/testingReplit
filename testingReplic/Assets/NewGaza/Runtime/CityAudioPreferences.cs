using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Audio-only local preferences; deliberately separate from game saves.</summary>
    public static class CityAudioPreferences
    {
        private const string Prefix = "NewGaza.audio.v1.";

        public const float DefaultMaster = CityAudioMix.DefaultMasterVolume;
        public const float DefaultEquipment = CityAudioMix.DefaultEquipmentVolume;
        public const float DefaultAmbience = CityAudioMix.DefaultAmbienceVolume;
        public const float DefaultInterface = CityAudioMix.DefaultInterfaceVolume;

        public static float LoadVolume(CityAudioChannel channel)
        {
            string key;
            float fallback;
            switch (channel)
            {
                case CityAudioChannel.Master: key = "master"; fallback = DefaultMaster; break;
                case CityAudioChannel.Equipment: key = "equipment"; fallback = DefaultEquipment; break;
                case CityAudioChannel.Ambience: key = "ambience"; fallback = DefaultAmbience; break;
                case CityAudioChannel.Interface: key = "interface"; fallback = DefaultInterface; break;
                default: return 0f;
            }

            float value = PlayerPrefs.GetFloat(Prefix + key, fallback);
            return CityAudioMix.SanitizePreference(value, fallback);
        }

        public static void SaveVolume(CityAudioChannel channel, float value)
        {
            string key;
            switch (channel)
            {
                case CityAudioChannel.Master: key = "master"; break;
                case CityAudioChannel.Equipment: key = "equipment"; break;
                case CityAudioChannel.Ambience: key = "ambience"; break;
                case CityAudioChannel.Interface: key = "interface"; break;
                default: return;
            }
            PlayerPrefs.SetFloat(Prefix + key, CityAudioMix.SanitizePreference(value, 0f));
            PlayerPrefs.Save();
        }

        public static bool LoadMuted()
        {
            return PlayerPrefs.GetInt(Prefix + "muted", 0) != 0;
        }

        public static void SaveMuted(bool muted)
        {
            PlayerPrefs.SetInt(Prefix + "muted", muted ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}