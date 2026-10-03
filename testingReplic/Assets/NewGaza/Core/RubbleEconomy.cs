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
            double size = Math.Max(45, Math.Min(1800, Reward(site) / 25d));
            double crew = Math.Sqrt(EquipmentEconomy.Capacity(state, "excavator") * EquipmentEconomy.Capacity(state, "bulldozer"));
            return Math.Max(30, (long)Math.Ceiling(size / crew));
        }
        public static long HaulingSeconds(GameState state) =>
            Math.Max(30, (long)Math.Ceiling(90 / EquipmentEconomy.Capacity(state, "truck")));
        public static long RecyclingSeconds(GameState state) => Math.Max(45, 120 / Math.Max(1, state.factoryLevel));
        public static long TotalSeconds(GameState state, RubbleSiteState site) =>
            ClearingSeconds(state, site) + HaulingSeconds(state) + RecyclingSeconds(state);
        public static ResourceStock Yield(GameState state, RubbleSiteState site)
        {
            int multiplier = state.factoryLevel;
            var stock = new ResourceStock { concrete = 40 * multiplier, iron = 15 * multiplier, wood = 12 * multiplier, other = 8 * multiplier };
            if (site == null) return stock; // existing imported-material contracts keep their semantics, no cash payment
            long budget = Reward(site) / 10;
            double ratio = Math.Min(1, budget / (2200d * Math.Max(1, multiplier)));
            stock.concrete = (int)(stock.concrete * ratio); stock.iron = (int)(stock.iron * ratio);
            stock.wood = (int)(stock.wood * ratio); stock.other = (int)(stock.other * ratio);
            if (stock.concrete + stock.iron + stock.wood + stock.other == 0 && budget >= 10) stock.other = 1;
            return stock;
        }
    }
}