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

        // Explicit reset only: keep a restorable copy, and remove every recovery file
        // so Load cannot resurrect the previous city from .bak or .tmp.
        public static string ArchiveAndClear()
        {
            string folder = Application.persistentDataPath;
            string archive = Path.Combine(folder, "ResetBackups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Directory.CreateDirectory(archive);
            string[] files = Directory.GetFiles(folder, "new-gaza.json*");
            foreach (string file in files) File.Copy(file, Path.Combine(archive, Path.GetFileName(file)), false);
            foreach (string file in files) File.Delete(file);
            return archive;
        }

        public static GameState Load(long now, out string warning)
        {
            warning = null;
            Exception failure = null;
            if (File.Exists(SavePath))
            {
                try { return Read(SavePath, out warning); }
                catch (Exception exception) { failure = exception; }
            }
            // A validated backup is preferred to an interrupted write. A temporary
            // file also rescues a first-ever save interrupted before its atomic move.
            foreach (string recovery in new[] { SavePath + ".bak", SavePath + ".tmp" })
            {
                if (!File.Exists(recovery)) continue;
                try
                {
                    var restored = Read(recovery, out var presentationWarning);
                    if (File.Exists(SavePath))
                        File.Copy(SavePath, SavePath + ".damaged", true);
                    // Restore the primary as well: the next Save must not rotate
                    // a missing/damaged primary over the only healthy backup.
                    File.Copy(recovery, SavePath, true);
                    warning = "تم استرجاع النسخة الاحتياطية للحفظ." +
                        (presentationWarning == null ? "" : "\n" + presentationWarning);
                    return restored;
                }
                catch (Exception exception) { failure = exception; }
            }
            if (failure != null || File.Exists(SavePath + ".damaged") ||
                File.Exists(SavePath + ".presentation-backup") ||
                File.Exists(SavePath + ".bak.presentation-backup") ||
                File.Exists(SavePath + ".tmp.presentation-backup") ||
                File.Exists(SavePath + ".compatibility-backup") ||
                File.Exists(SavePath + ".bak.compatibility-backup") ||
                File.Exists(SavePath + ".tmp.compatibility-backup"))
                throw new InvalidDataException("Save files are unreadable. Your progress has NOT been reset; files have been preserved.", failure);
            return GameCatalog.CreateNew(now);
        }

        private static GameState Read(string path, out string warning)
        {
            warning = null;
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
            if (envelope == null || envelope.schema != 1 || string.IsNullOrEmpty(envelope.payload)
                || envelope.checksum != Hash(envelope.payload))
                throw new InvalidDataException("Unsupported or damaged save envelope.");
            var state = JsonUtility.FromJson<GameState>(envelope.payload);
            string compatibilityWarning = DevelopmentSaveCompatibility.NormalizeForLoad(state);
            // Camera/fleet poses are optional presentation, not earned progress.
            // Normalize only these after verifying the ORIGINAL payload checksum.
            // Unity may create a zero-zoom camera for an absent legacy field.
            string presentationWarning = PresentationSaveValidation.RecoverForLoad(state);
            // Verify the original payload checksum above, then validate the frozen old schema
            // before migrating. Failures still propagate through Load's backup recovery.
            state = GameStateMigration.Upgrade(state);
            // Domain validation is authoritative; invalid primary files must also try the backup.
            Validate(state);
            if (compatibilityWarning != null)
            {
                string original = path + ".compatibility-backup";
                if (!File.Exists(original)) File.Copy(path, original, false);
                warning = compatibilityWarning;
            }
            if (presentationWarning != null)
            {
                // Retain raw evidence before any later normal Save rotates files.
                // Do not overwrite the first original affected save on repeat loads.
                string original = path + ".presentation-backup";
                if (!File.Exists(original)) File.Copy(path, original, false);
                warning = (warning == null ? "" : warning + "\n") + presentationWarning;
            }
            return state;
        }

        public static void Validate(GameState state)
        {
            if (state == null || state.version != 2 || state.coins < 0 || state.stock == null
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
                if (district == null || district.id != definition.id || district.projects == null || district.projects.Length != definition.projects.Length
                    || district.clearedLoads < 0 || district.clearedLoads > definition.rubbleLoads)
                    throw new InvalidDataException("Invalid district save.");
                for (int p = 0; p < district.projects.Length; p++)
                    if (district.projects[p] == null || district.projects[p].id != definition.projects[p].id)
                        throw new InvalidDataException("Project IDs do not match the current catalog.");
            }
            if (!state.districts[0].unlocked || !state.districts[state.selectedDistrict].unlocked
                || (state.jobStage != JobStage.Idle && (state.jobDistrict < 0 || state.jobDistrict >= state.districts.Length)))
                throw new InvalidDataException("Invalid district selection.");
            new EconomyService(state);
            CityDevelopmentService.ValidateSpatial(state);
        }

        public static void Save(GameState state)
        {
            Validate(state);
            Directory.CreateDirectory(Application.persistentDataPath);
            var payload = JsonUtility.ToJson(state);
            var envelope = new Envelope { payload = payload, checksum = Hash(payload) };
            string temporary = SavePath + ".tmp";
            // Flush bytes before the atomic replace, rather than relying on quit
            // or a buffered writer completing after mobile suspension.
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(file, new UTF8Encoding(false), 4096, true))
                {
                    writer.Write(JsonUtility.ToJson(envelope));
                    writer.Flush();
                }
                file.Flush(true);
            }
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