using System;

namespace NewGaza.Core
{
    public static class ActiveWorkPresentation
    {
        public static string Status(GameState state, long now)
        {
            if (state == null || state.jobStage == JobStage.Idle) return "";
            if (state.jobStage == JobStage.Clearing)
                return state.development != null && !string.IsNullOrEmpty(state.development.activeRubbleId) &&
                    !state.development.crewArrived ? "الآليات في الطريق إلى الموقع" : "إزالة الدمار جارية";
            if (state.jobStage == JobStage.Hauling)
                return state.jobFinishUtc <= now ? "بانتظار عودة الآليات وتسليم الحمولة" : "نقل الركام إلى المصنع";
            return state.jobFinishUtc <= now ? "المخزن ممتلئ — بع الموارد لإتمام التسليم" : "إعادة تدوير الركام";
        }

        public static float ViewingSize(float width, float depth, float aspect)
        {
            if (!Finite(width) || width <= 0) width = .4f;
            if (!Finite(depth) || depth <= 0) depth = .4f;
            if (!Finite(aspect) || aspect <= 0) aspect = 1;
            // Circumscribed footprint plus fleet working clearance, including portrait screens.
            float radius = (float)Math.Sqrt((double)width * width + (double)depth * depth) * .5f + .35f;
            return Math.Min(125, Math.Max(.9f, radius * 1.2f / Math.Max(.25f, Math.Min(1, aspect))));
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}