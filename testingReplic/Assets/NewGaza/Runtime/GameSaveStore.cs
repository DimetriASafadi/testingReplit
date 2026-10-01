using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Local, checksummed saves. A corrupt save is never replaced with a fresh game.</summary>
    public static class GameSaveStore
    {
        [Serializable]
        private class Envelope
        {
            public int schema = 1;
            public string payload;
            public string checksum;
        }

        public static string SavePath => Path.Combine(Application.persistentDataPath, "new-gaza.json");

        public static GameState Load(long now, out string warning)
        {
            warning = null;
            if (!File.Exists(SavePath))
            {
                if (File.Exists(SavePath + ".bak"))
                {
                    warning = "تم استرجاع النسخة الاحتياطية للحفظ.";
                    return Read(SavePath + ".bak");
                }
                return GameCatalog.CreateNew(now);
            }
            try { return Read(SavePath); }
            catch (Exception original)
            {
                if (File.Exists(SavePath + ".bak"))
                {
                    try
                    {
                        var restored = Read(SavePath + ".bak");
                        // Preserve the invalid file for diagnosis rather than overwrite it.
                        File.Copy(SavePath, SavePath + ".damaged", true);
                        File.Copy(SavePath + ".bak", SavePath, true);
                        warning = "تعذّر قراءة آخر حفظ. تم استرجاع النسخة الاحتياطية.";
                        return restored;
                    }
                    catch (Exception backupError)
                    {
                        throw new InvalidDataException("Both save files are unreadable. Your files have been preserved.", backupError);
                    }
                }
                throw new InvalidDataException("Save is unreadable. Your file has been preserved.", original);
            }
        }

        private static GameState Read(string path)
        {
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
            if (envelope == null || envelope.schema != 1 || string.IsNullOrEmpty(envelope.payload)
                || envelope.checksum != Hash(envelope.payload))
                throw new InvalidDataException("Unsupported or damaged save envelope.");
            var state = JsonUtility.FromJson<GameState>(envelope.payload);
            Validate(state);
            // Domain validation is authoritative; invalid primary files must also try the backup.
            new EconomyService(state);
            return state;
        }

        public static void Validate(GameState state)
        {
            if (state == null || state.version != 1 || state.coins < 0 || state.stock == null
                || state.stock.concrete < 0 || state.stock.iron < 0 || state.stock.wood < 0 || state.stock.other < 0
                || state.districts == null || state.districts.Length != GameCatalog.Districts.Length
                || state.selectedDistrict < 0 || state.selectedDistrict >= state.districts.Length
                || state.factoryLevel < 0 || state.factoryLevel > 5
                || state.excavators < 0 || state.trucks < 0 || state.bulldozers < 0
                || state.equipmentLevel < 1
                || !Enum.IsDefined(typeof(JobStage), state.jobStage))
                throw new InvalidDataException("Invalid saved game state.");
            for (int i = 0; i < state.districts.Length; i++)
            {
                var district = state.districts[i];
                var definition = GameCatalog.Districts[i];
                if (district == null || district.projects == null || district.projects.Length != definition.projects.Length
                    || district.clearedLoads < 0 || district.clearedLoads > definition.rubbleLoads)
                    throw new InvalidDataException("Invalid district save.");
                for (int p = 0; p < district.projects.Length; p++)
                    if (district.projects[p] == null || district.projects[p].id != definition.projects[p].id)
                        throw new InvalidDataException("Project IDs do not match the current catalog.");
            }
            if (!state.districts[0].unlocked || !state.districts[state.selectedDistrict].unlocked
                || (state.jobStage != JobStage.Idle && (state.jobDistrict < 0 || state.jobDistrict >= state.districts.Length)))
                throw new InvalidDataException("Invalid district selection.");
        }

        public static void Save(GameState state)
        {
            Validate(state);
            Directory.CreateDirectory(Application.persistentDataPath);
            var payload = JsonUtility.ToJson(state);
            var envelope = new Envelope { payload = payload, checksum = Hash(payload) };
            string temporary = SavePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(envelope), Encoding.UTF8);
            if (File.Exists(SavePath))
                File.Replace(temporary, SavePath, SavePath + ".bak");
            else
                File.Move(temporary, SavePath);
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
    }
}