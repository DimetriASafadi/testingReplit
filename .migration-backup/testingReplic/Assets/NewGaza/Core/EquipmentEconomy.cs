using System;
using System.Collections.Generic;

namespace NewGaza.Core
{
    public static class EquipmentEconomy
    {
        public static long Price(string kind) => kind == "excavator" ? GameCatalog.ExcavatorCost :
            kind == "truck" ? GameCatalog.TruckCost : kind == "bulldozer" ? GameCatalog.BulldozerCost : 0;
        public static string Name(string kind) => kind == "excavator" ? "حفارة" : kind == "truck" ? "شاحنة" : "جرافة";
        public static long UpgradeCost(EquipmentUnitState unit) => unit.purchasePrice / 2;
        public static bool CanBuy(GameState state) => state.development == null || !state.development.requiresPlacedFactory ||
            state.factoryLevel > 0 || Array.Exists(state.development.buildings, b => b.definitionId == "recycling");

        public static void Ensure(GameState state)
        {
            if (state.equipmentUnits != null) return;
            var units = new List<EquipmentUnitState>();
            foreach (string kind in new[] { "excavator", "truck", "bulldozer" })
            {
                int count = kind == "excavator" ? state.excavators : kind == "truck" ? state.trucks : state.bulldozers;
                for (int i = 0; i < count; i++)
                    units.Add(new EquipmentUnitState { id = kind + "-legacy-" + i, kind = kind,
                        purchasePrice = Price(kind), level = Math.Min(4, state.equipmentLevel),
                        legacyPower = state.equipmentLevel });
            }
            state.equipmentUnits = units.ToArray();
        }

        public static void Add(GameState state, string kind, long paid)
        {
            var units = new EquipmentUnitState[state.equipmentUnits.Length + 1];
            Array.Copy(state.equipmentUnits, units, units.Length - 1);
            units[units.Length - 1] = new EquipmentUnitState { id = Guid.NewGuid().ToString("N"), kind = kind, purchasePrice = paid };
            state.equipmentUnits = units;
        }

        public static double Capacity(GameState state, string kind)
        {
            double power = 0;
            if (state.equipmentUnits != null)
            {
                foreach (var unit in state.equipmentUnits)
                    if (unit.kind == kind) power += unit.legacyPower > 0 ?
                        unit.legacyPower + Math.Max(0, unit.level - Math.Min(4, unit.legacyPower)) * .25 :
                        1 + (unit.level - 1) * .25;
            }
            else
                power = (kind == "excavator" ? state.excavators : kind == "truck" ? state.trucks : state.bulldozers) * state.equipmentLevel;
            return Math.Max(1, power <= 4 ? power : 4 + Math.Log(power / 4));
        }

        internal static void Validate(GameState state)
        {
            if (state.equipmentUnits == null) return; // pre-module saves are migrated only after full validation
            int excavators = 0, trucks = 0, bulldozers = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in state.equipmentUnits)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.id) || !ids.Add(unit.id) ||
                    Price(unit.kind) == 0 || unit.purchasePrice < 2 || unit.level < 1 || unit.level > 4 ||
                    unit.legacyPower < 0 || unit.legacyPower > 5)
                    throw new InvalidOperationException("بيانات المعدات غير صالحة؛ تم الحفاظ على الحفظ");
                if (unit.kind == "excavator") excavators++;
                else if (unit.kind == "truck") trucks++;
                else bulldozers++;
            }
            if (excavators != state.excavators || trucks != state.trucks || bulldozers != state.bulldozers)
                throw new InvalidOperationException("عدد المعدات لا يطابق سجل الآلات؛ تم الحفاظ على الحفظ");
        }
    }
}