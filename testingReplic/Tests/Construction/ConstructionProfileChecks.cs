using System;
using System.Collections.Generic;
using NewGaza;
using NewGaza.Core;

internal static class ConstructionProfileChecks
{
    internal static void Run()
    {
        Thresholds();
        InvalidAndFutureTimes();
        CompletionAndOfflineVisibility();
        HousingProfiles();
    }

    private static void Thresholds()
    {
        var project = new ProjectState { startedUtc = 100, finishUtc = 200 };
        Equal(CityConstructionPhase.Foundation, CityConstructionVisuals.ResolvePhase(project, 100),
            "phase at start");
        Equal(CityConstructionPhase.Foundation, CityConstructionVisuals.ResolvePhase(project, 127),
            "phase below first threshold");
        Equal(CityConstructionPhase.Frame, CityConstructionVisuals.ResolvePhase(project, 128),
            "phase at first threshold");
        Equal(CityConstructionPhase.Frame, CityConstructionVisuals.ResolvePhase(project, 171),
            "phase below second threshold");
        Equal(CityConstructionPhase.Finishing, CityConstructionVisuals.ResolvePhase(project, 172),
            "phase at second threshold");
        Equal(CityConstructionPhase.Finishing, CityConstructionVisuals.ResolvePhase(project, 200),
            "timestamp alone does not complete project");
    }

    private static void InvalidAndFutureTimes()
    {
        Equal(CityConstructionPhase.Inactive, CityConstructionVisuals.ResolvePhase(null, 100),
            "missing project");
        Equal(CityConstructionPhase.Foundation,
            CityConstructionVisuals.ResolvePhase(new ProjectState { startedUtc = 100, finishUtc = 100 }, 200),
            "zero duration");
        Equal(CityConstructionPhase.Foundation,
            CityConstructionVisuals.ResolvePhase(new ProjectState { startedUtc = 100, finishUtc = 99 }, 200),
            "negative duration");
        Equal(CityConstructionPhase.Foundation,
            CityConstructionVisuals.ResolvePhase(new ProjectState { startedUtc = 0, finishUtc = 100 }, 50),
            "missing start");
        var future = new ProjectState { startedUtc = 200, finishUtc = 300 };
        Equal(CityConstructionPhase.Foundation, CityConstructionVisuals.ResolvePhase(future, 199),
            "future start is foundation");
        Equal(CityConstructionPhase.Finishing,
            CityConstructionVisuals.ResolvePhase(
                new ProjectState { startedUtc = long.MaxValue - 100, finishUtc = long.MaxValue },
                long.MaxValue), "large timestamps do not overflow");
        Equal(CityConstructionPhase.Foundation,
            CityConstructionVisuals.ResolvePhase(new ProjectState { startedUtc = 100, finishUtc = 200 }, -1),
            "negative clock clamps to foundation");
    }

    private static void CompletionAndOfflineVisibility()
    {
        var project = new ProjectState { startedUtc = 100, finishUtc = 200 };
        Equal(CityConstructionPhase.Finishing, CityConstructionVisuals.ResolvePhase(project, 900),
            "offline elapsed is finishing until session marks complete");
        Check(CityConstructionVisuals.ShouldShowCrew(project, 900, true, false),
            "unlocked offline-finishing project has crew");
        Check(!CityConstructionVisuals.ShouldShowCrew(project, 900, false, false),
            "locked crew is hidden");
        Check(!CityConstructionVisuals.ShouldShowCrew(project, 900, true, true),
            "fogged crew is hidden");
        Check(!CityConstructionVisuals.ShouldShowCrew(project, 99, true, false),
            "unstarted project has no crew");
        project.completed = true;
        Equal(CityConstructionPhase.Complete, CityConstructionVisuals.ResolvePhase(project, 100),
            "completed flag is authoritative");
        Check(!CityConstructionVisuals.ShouldShowCrew(project, 900, true, false),
            "completed crew is hidden");
        project.finishUtc = project.startedUtc;
        Equal(CityConstructionPhase.Complete, CityConstructionVisuals.ResolvePhase(project, 100),
            "completed flag remains authoritative for invalid timestamps");
    }

    private static void HousingProfiles()
    {
        string[] keys = CityHousingProfiles.ModelKeys;
        Equal(40, keys.Length, "forty phase OBJ keys");
        var unique = new HashSet<string>(keys, StringComparer.Ordinal);
        Equal(40, unique.Count, "phase keys are unique");
        Equal(10, CityHousingProfiles.MasterKeys.Length, "ten housing masters");
        Equal("house_traditional_stonearches_final",
            CityHousingProfiles.ModelKey("old-city", CityConstructionPhase.Complete),
            "old-city uses stone-arch profile by stable ID");
        Equal("house_coastal_white_pool_frame",
            CityHousingProfiles.ModelKey("sheikh-ijlin", CityConstructionPhase.Frame),
            "coastal profile phase key by stable ID");
        Equal("apartment_12floor_tower_finishing",
            CityHousingProfiles.ModelKey("rimal", CityConstructionPhase.Finishing),
            "western tower profile phase key by stable ID");
        string[] districtIds =
        {
            "shujaiya", "tuffah", "sheikh-radwan", "daraj", "karama", "old-city",
            "nasr", "sabra", "zeitoun", "rimal", "tel-al-hawa", "sheikh-ijlin"
        };
        var usedMasters = new HashSet<string>(StringComparer.Ordinal);
        foreach (string districtId in districtIds)
        {
            CityHousingProfiles.Profile profile = CityHousingProfiles.ForDistrict(districtId);
            usedMasters.Add(profile.masterKey);
            float[] dimensions =
            {
                profile.foundationHeightMeters, profile.frameHeightMeters,
                profile.finishingHeightMeters, profile.logicalHeightMeters
            };
            CityConstructionPhase[] phases =
            {
                CityConstructionPhase.Foundation, CityConstructionPhase.Frame,
                CityConstructionPhase.Finishing, CityConstructionPhase.Complete
            };
            for (int phase = 0; phase < phases.Length; phase++)
                Check(Math.Abs(profile.HeightCapFor(phases[phase]) / dimensions[phase] - .05f) < .000001f,
                    "phase logical cap must preserve the same 1/20 width/depth scale for " + districtId);
        }
        Equal(12, districtIds.Length, "one stable housing assignment per inland district");
        Equal(10, usedMasters.Count, "all ten authored housing master types are assigned");
        Check(CityHousingProfiles.ForDistrict("rimal").MaxCityHeight > 0f,
            "profile height cap is positive logical meters / 20");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + ": expected " + expected + ", got " + actual);
    }
}