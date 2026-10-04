using System;

namespace NewGaza.Core
{
    public static class RubbleEconomy
    {
        public static long BuildingPrice(RubbleSiteState site)
        {
            if (site == null) return 0;
            var definition = Array.Find(GameCatalog.Districts[site.district].projects, p => p.id == site.projectId);
            if (definition == null) throw new InvalidOperationException("سعر المبنى المرتبط بالركام غير معروف");
            return definition.cost;
        }
        public static long Reward(RubbleSiteState site) => BuildingPrice(site) / 3;
        public static RubbleSiteState Active(GameState state)
        {
            var data = state.development;
            if (data == null || !data.initialized || data.activeRubbleId == null) return null;
            return Array.Find(data.rubble, site => site.id == data.activeRubbleId && !site.cleared);
        }
        public static long ClearingSeconds(GameState state, RubbleSiteState site)
        {
            double crew = Math.Sqrt(EquipmentEconomy.Capacity(state, "excavator") *
                EquipmentEconomy.Capacity(state, "bulldozer"));
            return Math.Max(30, (long)Math.Ceiling(60 / crew)); // One minute with starter equipment, after arrival.
        }
        public static long HaulingSeconds(GameState state) =>
            Math.Max(30, (long)Math.Ceiling(90 / EquipmentEconomy.Capacity(state, "truck")));
        public static long RecyclingSeconds(GameState state) => 1;
        public static long TotalSeconds(GameState state, RubbleSiteState site) =>
            ClearingSeconds(state, site) + HaulingSeconds(state) + RecyclingSeconds(state);
        public static ResourceStock Yield(GameState state, RubbleSiteState site)
        {
            int multiplier = state.factoryLevel;
            var stock = new ResourceStock { concrete = 40 * multiplier, iron = 15 * multiplier, wood = 12 * multiplier, other = 8 * multiplier };
            if (site == null) return stock; // existing imported-material contracts keep their semantics, no cash payment
            // The entire reward is saleable material, never an automatic cash grant.
            long budget = Reward(site) / 10;
            stock.concrete = (int)(budget / 5 / 2);
            stock.iron = (int)(budget / 5 / 6);
            stock.wood = (int)(budget / 5 / 7) * 2;
            stock.other = (int)(budget - stock.concrete * 2L - stock.iron * 6L - stock.wood * 7L / 2);
            return stock;
        }
    }
}