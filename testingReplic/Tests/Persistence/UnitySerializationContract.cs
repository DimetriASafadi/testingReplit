using System.Text.Json;
using System.Text.Json.Serialization;

// Filesystem/domain fixture only. This is NOT Unity's JsonUtility, nor engine evidence.
namespace UnityEngine
{
    internal static class Application
    {
        public static string persistentDataPath;
    }

    internal static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
    }
}