using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewGaza;
using NewGaza.Core;
using UnityEngine;

internal static class Program
{
    private const long Now = 200000;
    private static int checks;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        catch (InvalidOperationException) { Check(true, message); return; }
        throw new Exception(message);
    }

    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "new-gaza-persistence-" + Guid.NewGuid().ToString("N"));
        try
        {
            Application.persistentDataPath = root;
            Directory.CreateDirectory(root);
            BuildingsAndTimers();
            FreshDirectory(root, "travel");
            TravelAndArrival();
            FreshDirectory(root, "legacy");
            LegacyAndRewards();
            FreshDirectory(root, "recovery");
            Recovery();
            FreshDirectory(root, "presentation");
            PresentationRecovery();
            Console.WriteLine("PASS persistence filesystem/domain fixture / " + checks + " assertions.");
            Console.WriteLine("Uses a .NET serializer contract, NOT Unity JsonUtility; Unity Editor/Play Mode/shaders NOT RUN.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void FreshDirectory(string root, string name)
    {
        Application.persistentDataPath = Path.Combine(root, name);
        Directory.CreateDirectory(Application.persistentDataPath);
    }

    private static CityDevelopmentService Setup(out EconomyService economy)
    {
        var state = GameCatalog.CreateNew(Now);
        state.coins = 10000000;
        state.stock.concrete = state.stock.iron = 10000;
        state.development.requiresPlacedFactory = false;
        economy = new EconomyService(state);
        return new CityDevelopmentService(economy);
    }

    private static string Land(CityBuildingDefinition definition, int district, float x, float z, int turn) => null;

    private static void BuildingsAndTimers()
    {
        var rules = Setup(out var economy);
        var state = economy.State;
        long coins = state.coins;
        int concrete = state.stock.concrete, iron = state.stock.iron;
        var definition = CityBuildingCatalog.Find("small_house");
        Check(rules.Build(definition.id, 0, 10, 20, 1, Now, Land).success, "Build rotated house");
        string id = rules.Data.buildings[0].id;
        long deadline = rules.Data.buildings[0].finishUtc;
        state.camera = new CameraSaveState { x = 22, z = -33, zoom = 17, yaw = 91 };
        state.fleet = Fleet(state);
        GameSaveStore.Save(state);
        var loaded = GameSaveStore.Load(Now + 1, out var warning);
        Check(warning == null, "Clean load");
        Check(loaded.coins == coins - definition.cost && loaded.stock.concrete == concrete - definition.concrete &&
            loaded.stock.iron == iron - definition.iron, "Cost charged exactly once across reopen");
        economy = new EconomyService(loaded);
        rules = new CityDevelopmentService(economy);
        var building = rules.Building(id);
        Check(building.x == 10 && building.z == 20 && building.quarterTurn == 1 &&
            building.startedUtc == Now && building.finishUtc == deadline, "Placement and original deadline preserved");
        Check(loaded.camera.x == 22 && loaded.camera.z == -33 && loaded.camera.zoom == 17 &&
            loaded.camera.yaw == 91 && loaded.fleet.truck.x == 5, "Camera and fleet positions round trip");
        economy.Tick(deadline - 1);
        Check(!building.completed, "No early finish");
        economy.Tick(deadline + 7201);
        Check(building.completed && CityDevelopmentService.PendingIncome(building, loaded.lastSeenUtc) ==
            definition.hourlyIncome * 2, "Offline construction and two hours income use original deadline");
        Check(rules.Collect(id, loaded.lastSeenUtc).success, "Collect accrued income");
        long collectedCoins = loaded.coins;
        GameSaveStore.Save(loaded);
        loaded = GameSaveStore.Load(deadline + 7201, out warning);
        economy = new EconomyService(loaded);
        rules = new CityDevelopmentService(economy);
        Check(!rules.Collect(id, loaded.lastSeenUtc).success && loaded.coins == collectedCoins, "No repeated collection after reopening");
        economy.Tick(Now);
        Check(loaded.lastSeenUtc == deadline + 7201 && loaded.coins == collectedCoins, "Clock rollback doesn't undo progress or create income");
        economy.Tick(deadline + 10800);
        Check(rules.Collect(id, loaded.lastSeenUtc).success &&
            loaded.coins == collectedCoins + definition.hourlyIncome, "Remainder retained for next income period");
        loaded.camera.zoom = float.NaN;
        Reject(() => GameSaveStore.Save(loaded), "Invalid camera rejected before write");
    }

    private static FleetSaveState Fleet(GameState state) => new FleetSaveState {
        district = state.jobDistrict, stage = state.jobStage,
        rubbleId = state.development?.activeRubbleId, depotId = state.development?.dispatchDepotId,
        excavator = new VehicleSaveState { x = 1, y = .03f, z = 2, yaw = 11 },
        truck = new VehicleSaveState { x = 5, y = .04f, z = 6, yaw = 22 },
        bulldozer = new VehicleSaveState { x = 8, y = .05f, z = 9, yaw = 33 }
    };

    private static void TravelAndArrival()
    {
        var rules = Setup(out var economy);
        var state = economy.State;
        state.factoryLevel = state.excavators = state.trucks = state.bulldozers = 1;
        state.equipmentUnits = null; // Legacy counters are migrated to independent units.
        economy.ClearingCrewReady = () => false;
        string site = CityDevelopmentService.SiteId(0, 0);
        Check(rules.Clear(site, "central", Now).success, "Dispatch to chosen rubble");
        state.fleet = Fleet(state);
        GameSaveStore.Save(state);
        state = GameSaveStore.Load(Now + 10000, out _);
        economy = new EconomyService(state);
        rules = new CityDevelopmentService(economy);
        economy.ClearingCrewReady = () => false;
        economy.Tick(Now + 10000);
        Check(!rules.Site(site).cleared && !state.development.crewArrived &&
            state.stock.concrete == 10000 && state.development.dispatchDepotId == "central" &&
            state.fleet.rubbleId == site, "Travel reopen retains target/depot and gives no remote clearance");
        economy.ClearingCrewReady = () => true;
        economy.Tick(Now + 10001);
        long deadline = state.jobFinishUtc;
        Check(state.development.crewArrived && deadline > state.lastSeenUtc, "Arrival starts full duration");
        state.fleet = Fleet(state);
        GameSaveStore.Save(state);
        state = GameSaveStore.Load(deadline - 1, out _);
        economy = new EconomyService(state);
        rules = new CityDevelopmentService(economy);
        economy.ClearingCrewReady = () => false;
        economy.Tick(deadline - 1);
        Check(state.jobFinishUtc == deadline && state.jobStage == JobStage.Clearing, "Arrived clearing timer not reset on reopen");
        economy.Tick(deadline + 10000);
        var yield = RubbleEconomy.Yield(state, rules.Site(site));
        long paid = RubbleEconomy.Reward(rules.Site(site));
        Check(state.jobStage == JobStage.Idle && rules.Site(site).cleared &&
            state.stock.concrete == 10000 + yield.concrete && state.stock.iron == 10000 + yield.iron &&
            state.coins == 10000000 + paid, "Offline hauling/recycling credited once");
        GameSaveStore.Save(state);
        state = GameSaveStore.Load(deadline + 10001, out _);
        economy = new EconomyService(state);
        economy.Tick(deadline + 20000);
        Check(state.stock.concrete == 10000 + yield.concrete && state.stock.iron == 10000 + yield.iron &&
            state.coins == 10000000 + paid, "No repeat recycling credit after another reopen");
    }

    private static void LegacyAndRewards()
    {
        var state = GameCatalog.CreateNew(Now);
        state.development = null;
        state.coins = 765432;
        state.lastGiftUtc = 170000;
        state.districts[0].clearedLoads = GameCatalog.Districts[0].rubbleLoads;
        foreach (var definition in GameCatalog.Districts[0].projects)
        {
            var project = Array.Find(state.districts[0].projects, p => p.id == definition.id);
            if (definition.id == "farm" || definition.id == "industry")
            {
                project.completed = true;
                project.lastIncomeUtc = 180000;
            }
            else
            {
                project.completed = true;
                project.startedUtc = definition.prerequisite == null ? 10000 :
                    Array.Find(state.districts[0].projects, p => p.id == definition.prerequisite).finishUtc + 1000;
                project.finishUtc = project.startedUtc + definition.durationSeconds;
                project.lastIncomeUtc = project.finishUtc;
            }
        }
        state.districts[0].rewardClaimed = true;
        state.districts[1].unlocked = true;
        var water = Array.Find(state.districts[1].projects, p => p.id == "water");
        water.startedUtc = Now;
        water.finishUtc = Now + GameCatalog.Districts[1].projects[0].durationSeconds;
        GameSaveStore.Save(state);
        state = GameSaveStore.Load(Now, out _);
        var economy = new EconomyService(state);
        var rules = new CityDevelopmentService(economy);
        Check(rules.Data.legacyProgress && state.coins == 765432 && state.lastGiftUtc == 170000 &&
            state.districts[0].rewardClaimed && state.districts[1].unlocked, "Old save retains cleanup, access, rewards and gift");
        Check(!economy.ClaimDistrictReward(0, Now).success && state.coins == 765432, "Old reward cannot be collected twice");
        Check(state.districts[1].projects[0].startedUtc == Now, "Old project start survives module initialization");
        GameSaveStore.Save(state);
        var again = GameSaveStore.Load(Now + 1, out _);
        Check(again.development.initialized && again.development.legacyProgress &&
            again.districts[0].projects[0].lastIncomeUtc == state.districts[0].projects[0].lastIncomeUtc,
            "Migrated module and income watermark survive subsequent save");
    }

    private static void Recovery()
    {
        var rules = Setup(out var economy);
        Check(rules.Build("small_house", 0, 10, 20, 0, Now, Land).success, "First valid building");
        Check(rules.Build("equipment_store", 0, 30, 40, 1, Now, Land).success, "Second valid building");
        var state = economy.State;
        GameSaveStore.Save(state);
        string valid = File.ReadAllText(GameSaveStore.SavePath);
        state.playerName = "updated";
        GameSaveStore.Save(state);
        Check(File.ReadAllText(GameSaveStore.SavePath + ".bak") == valid, "Atomic replace keeps previous valid backup");
        state.development.buildings[1].x = state.development.buildings[0].x;
        state.development.buildings[1].z = state.development.buildings[0].z;
        WriteEnvelope(state);
        string overlapping = File.ReadAllText(GameSaveStore.SavePath);
        var recovered = GameSaveStore.Load(Now, out var warning);
        Check(warning != null && recovered.development.buildings[1].x == 30 &&
            recovered.coins == state.coins, "Checksummed but overlapping primary recovers progress from backup");
        Check(File.ReadAllText(GameSaveStore.SavePath + ".damaged") == overlapping, "Invalid primary retained");
        Check(File.ReadAllText(GameSaveStore.SavePath) == valid, "Healthy backup restored as primary");
        File.Delete(GameSaveStore.SavePath);
        recovered = GameSaveStore.Load(Now, out warning);
        Check(warning != null && File.Exists(GameSaveStore.SavePath) && recovered.coins == state.coins,
            "Missing primary recovered without resetting progress");
        File.Delete(GameSaveStore.SavePath);
        File.Delete(GameSaveStore.SavePath + ".bak");
        File.Delete(GameSaveStore.SavePath + ".damaged");
        File.WriteAllText(GameSaveStore.SavePath + ".tmp", valid);
        recovered = GameSaveStore.Load(Now, out warning);
        Check(warning != null && recovered.development.buildings.Length == 2, "Interrupted first save recovered from validated temp");
        File.WriteAllText(GameSaveStore.SavePath, "broken primary");
        File.WriteAllText(GameSaveStore.SavePath + ".bak", "broken backup");
        File.WriteAllText(GameSaveStore.SavePath + ".tmp", "broken temp");
        Reject(() => GameSaveStore.Load(Now, out _), "All-invalid saves fail rather than create new game");
        Check(File.ReadAllText(GameSaveStore.SavePath) == "broken primary" &&
            File.ReadAllText(GameSaveStore.SavePath + ".bak") == "broken backup", "Unreadable evidence not overwritten");
    }

    private static void PresentationRecovery()
    {
        var rules = Setup(out var economy);
        var state = economy.State;
        Check(rules.Build("small_house", 0, 10, 20, 0, Now, Land).success, "Presentation fixture owns a real building");
        long balance = state.coins;
        string id = rules.Data.buildings[0].id;
        long deadline = rules.Data.buildings[0].finishUtc;
        // A healthy older backup must NOT replace newer gameplay when only the
        // optional view is invalid in the primary.
        GameSaveStore.Save(state);
        state.playerName = "newer progress";
        GameSaveStore.Save(state);
        string oldBackup = File.ReadAllText(GameSaveStore.SavePath + ".bak");
        state.camera = new CameraSaveState(); // Unity-style materialized absent camera.
        WriteEnvelope(state);
        string original = File.ReadAllText(GameSaveStore.SavePath);
        var loaded = GameSaveStore.Load(Now, out string warning);
        Check(warning != null && loaded.camera == null && loaded.playerName == "newer progress" &&
            loaded.coins == balance && loaded.development.buildings[0].id == id &&
            loaded.development.buildings[0].finishUtc == deadline, "Zero camera recovers newest progress, not older backup");
        Check(File.ReadAllText(GameSaveStore.SavePath) == original &&
            File.ReadAllText(GameSaveStore.SavePath + ".presentation-backup") == original &&
            File.ReadAllText(GameSaveStore.SavePath + ".bak") == oldBackup,
            "Recovery retains original payload and older backup without writes to progress");
        loaded = GameSaveStore.Load(Now, out warning);
        Check(loaded.camera == null && warning != null, "Presentation recovery is repeatable");
        foreach (var camera in new[] {
            new CameraSaveState { zoom = -1 }, new CameraSaveState { zoom = 10001 },
            new CameraSaveState { zoom = 20, x = 1000001 },
            new CameraSaveState { zoom = 20, yaw = 361 } })
        {
            state.camera = camera;
            WriteEnvelope(state);
            loaded = GameSaveStore.Load(Now, out warning);
            Check(loaded.camera == null && warning != null && loaded.coins == balance &&
                loaded.development.buildings[0].id == id, "Invalid view alone doesn't discard earned progress");
        }
        loaded.camera = new CameraSaveState { x = 22, z = -33, zoom = .6f, yaw = 360 };
        GameSaveStore.Save(loaded);
        loaded = GameSaveStore.Load(Now, out warning);
        Check(warning == null && loaded.camera.zoom == .6f && loaded.camera.yaw == 360,
            "Valid camera restored unchanged without recovery warning");
        Check(File.ReadAllText(GameSaveStore.SavePath + ".presentation-backup") == original,
            "Later saves retain first affected original");
        // Exercise the deserialization difference that normal .NET round trips
        // miss: an absent optional camera can emerge as a non-null zero object.
        loaded.camera = null;
        WriteEnvelope(loaded, true);
        JsonUtility.MaterializeAbsentCamera = true;
        try
        {
            loaded = GameSaveStore.Load(Now, out warning);
            Check(loaded.camera == null && warning != null && loaded.coins == balance &&
                loaded.development.buildings[0].id == id,
                "Materialized camera from an absent JSON field recovers earned progress");
        }
        finally { JsonUtility.MaterializeAbsentCamera = false; }
        loaded.camera = new CameraSaveState { zoom = 20 };
        loaded.camera.zoom = float.NaN;
        Reject(() => GameSaveStore.Save(loaded), "Invalid new camera still rejected on save");
        loaded.camera = null;
        loaded.fleet = new FleetSaveState(); // Missing optional poses.
        WriteEnvelope(loaded);
        loaded = GameSaveStore.Load(Now, out warning);
        Check(loaded.fleet == null && warning != null && loaded.coins == balance &&
            loaded.development.buildings[0].id == id, "Absent fleet poses recover without clearing buildings");
        // Recovery must not make corrupted GAMEPLAY valid, even when camera is bad.
        File.Delete(GameSaveStore.SavePath + ".bak");
        loaded.coins = -1;
        loaded.camera = new CameraSaveState();
        WriteEnvelope(loaded);
        string corrupt = File.ReadAllText(GameSaveStore.SavePath);
        Reject(() => GameSaveStore.Load(Now, out _), "Gameplay corruption still fails with an invalid camera");
        Check(File.ReadAllText(GameSaveStore.SavePath) == corrupt, "Invalid gameplay never rewritten as a fresh game");
        File.Delete(GameSaveStore.SavePath);
        Reject(() => GameSaveStore.Load(Now, out _), "Orphan presentation evidence never silently starts a fresh campaign");
    }

    private static void WriteEnvelope(GameState state, bool omitCamera = false)
    {
        string payload = JsonSerializer.Serialize(state, Json);
        if (omitCamera)
        {
            var fields = new System.Collections.Generic.Dictionary<string, JsonElement>();
            using (var document = JsonDocument.Parse(payload))
                foreach (var field in document.RootElement.EnumerateObject())
                    if (field.Name != "camera") fields.Add(field.Name, field.Value.Clone());
            payload = JsonSerializer.Serialize(fields);
        }
        string checksum = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        File.WriteAllText(GameSaveStore.SavePath, JsonSerializer.Serialize(new { schema = 1, payload, checksum }));
    }
}