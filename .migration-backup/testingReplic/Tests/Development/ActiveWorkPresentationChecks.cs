using System;
using NewGaza.Core;

internal static class ActiveWorkPresentationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var state = GameCatalog.CreateNew(200000);
        check(ActiveWorkPresentation.Status(state, 200000) == "", "finished/idle work leaves no stale marker");
        state.development = new CityDevelopmentState { activeRubbleId = "site", crewArrived = false };
        state.jobStage = JobStage.Clearing;
        check(ActiveWorkPresentation.Status(state, 200000).Contains("الطريق"), "marker distinguishes traveling from digging");
        state.development.crewArrived = true;
        check(ActiveWorkPresentation.Status(state, 200000).Contains("إزالة"), "arrived crew shows actual clearing");
        state.jobStage = JobStage.Hauling; state.jobFinishUtc = 200001;
        check(ActiveWorkPresentation.Status(state, 200000).Contains("نقل"), "hauling phase labeled");
        check(ActiveWorkPresentation.Status(state, 200002).Contains("بانتظار"), "return gate is not falsely shown as completed");
        state.jobStage = JobStage.Recycling;
        check(ActiveWorkPresentation.Status(state, 200000).Contains("تدوير"), "recycling phase labeled");
        check(ActiveWorkPresentation.Status(state, 200002).Contains("المخزن ممتلئ"), "blocked delivery explains how to finish");
        state.development.activeRubbleId = null; state.jobStage = JobStage.Clearing;
        check(ActiveWorkPresentation.Status(state, 200000).Contains("إزالة"), "legacy work with no local site still has clear status");
        float portrait = ActiveWorkPresentation.ViewingSize(.5f, .5f, .5f);
        float landscape = ActiveWorkPresentation.ViewingSize(.5f, .5f, 2f);
        check(portrait > landscape && landscape >= .9f && portrait < 3, "close work zoom frames site plus machinery on phones");
        check(ActiveWorkPresentation.ViewingSize(2, 4, .5f) > portrait, "large work sites zoom out enough to fit");
        check(ActiveWorkPresentation.ViewingSize(float.NaN, float.PositiveInfinity, 0) >= .9f,
            "invalid optional framing data cannot corrupt camera zoom");
    }
}