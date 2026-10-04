namespace NewGaza.Core
{
    /// <summary>Canonicalize empty optional references, never rebuild earned progress.</summary>
    public static class DevelopmentSaveCompatibility
    {
        public static string NormalizeForLoad(GameState state)
        {
            var data = state?.development;
            if (data == null) return null;
            bool changed = false;
            // Unity JSON can represent a null string as "". Both mean no target;
            // nonempty IDs (including whitespace) must still pass strict validation.
            if (data.activeRubbleId == "") { data.activeRubbleId = null; changed = true; }
            if (data.dispatchDepotId == "") { data.dispatchDepotId = null; changed = true; }
            return changed ? "تم توحيد المراجع الفارغة لموقع العمل والمستودع في الحفظ؛ لم تُحذف مواقع أو مبانٍ أو تقدم." : null;
        }
    }
}