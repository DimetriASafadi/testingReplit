using NewGaza.Core;

namespace NewGaza
{
    /// <summary>Pure presentation timing rules for active construction projects.</summary>
    public enum CityConstructionPhase
    {
        Inactive = 0,
        Foundation = 1,
        Frame = 2,
        Finishing = 3,
        Complete = 4
    }

    public static class CityConstructionVisuals
    {
        /// <summary>
        /// Resolves the visible build phase from persisted project timestamps. It never changes
        /// project state; only the saved completed flag represents completion.
        /// </summary>
        public static CityConstructionPhase ResolvePhase(ProjectState project, long nowUtc)
        {
            if (project == null) return CityConstructionPhase.Inactive;
            if (project.completed) return CityConstructionPhase.Complete;
            if (project.startedUtc <= 0 || project.finishUtc <= project.startedUtc)
                return CityConstructionPhase.Foundation;

            long durationSeconds = project.finishUtc - project.startedUtc;
            if (durationSeconds <= 0)
                return CityConstructionPhase.Foundation;

            if (nowUtc <= project.startedUtc) return CityConstructionPhase.Foundation;
            long elapsedSeconds = nowUtc - project.startedUtc;
            double progress = (double)elapsedSeconds / durationSeconds;
            if (progress < .28d) return CityConstructionPhase.Foundation;
            if (progress < .72d) return CityConstructionPhase.Frame;
            return CityConstructionPhase.Finishing;
        }

        public static bool ShouldShowCrew(ProjectState project, long nowUtc,
            bool districtUnlocked, bool fogged)
        {
            if (!districtUnlocked || fogged || project == null || project.completed ||
                project.startedUtc <= 0 || nowUtc < project.startedUtc)
                return false;
            CityConstructionPhase phase = ResolvePhase(project, nowUtc);
            return phase == CityConstructionPhase.Foundation ||
                phase == CityConstructionPhase.Frame ||
                phase == CityConstructionPhase.Finishing;
        }
    }
}