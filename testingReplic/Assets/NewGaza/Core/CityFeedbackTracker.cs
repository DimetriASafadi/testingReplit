using System;
using System.Collections.Generic;

namespace NewGaza.Core
{
    public readonly struct CityFeedbackFrame
    {
        public readonly long coins;
        public readonly int completed;
        public readonly bool reward;
        public readonly int[] unlocked;
        public bool Any => coins > 0 || completed > 0 || reward || unlocked.Length > 0;
        public CityFeedbackFrame(long coins, int completed, bool reward, int[] unlocked)
        { this.coins = coins; this.completed = completed; this.reward = reward; this.unlocked = unlocked; }
    }

    /// <summary>Presentation-only changes; never advances simulation or mutates saves.</summary>
    public sealed class CityFeedbackTracker
    {
        private bool primed;
        private long coins, gift;
        private bool[] access, rewards;
        private HashSet<string> completed = new HashSet<string>();
        private HashSet<string> scratch = new HashSet<string>();
        private readonly Dictionary<string, int> roads = new Dictionary<string, int>();

        public CityFeedbackFrame Capture(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            bool initial = !primed || access.Length != state.districts.Length;
            if (initial) { access = new bool[state.districts.Length]; rewards = new bool[access.Length]; }
            scratch.Clear();
            int count = 0; bool reward = !initial && state.lastGiftUtc > gift;
            List<int> opened = null;
            for (int d = 0; d < state.districts.Length; d++)
            {
                var district = state.districts[d];
                if (!initial && !access[d] && district.unlocked)
                { if (opened == null) opened = new List<int>(); opened.Add(d); }
                reward |= !initial && !rewards[d] && district.rewardClaimed;
                access[d] = district.unlocked; rewards[d] = district.rewardClaimed;
                foreach (var project in district.projects)
                    if (project.completed) AddCompletion("p:" + d + ":" + project.id, initial, ref count);
            }
            if (state.development != null && state.development.initialized)
            {
                foreach (var building in state.development.buildings)
                    if (building.completed) AddCompletion("b:" + building.id, initial, ref count);
                foreach (var site in state.development.rubble)
                    if (site.cleared) AddCompletion("s:" + site.id, initial, ref count);
            }
            foreach (var road in state.roadSegments)
            {
                if (!initial && roads.TryGetValue(road.id, out int oldLevel) && road.level > oldLevel) count++;
                else if (!initial && !roads.ContainsKey(road.id) && road.level > 0) count++;
                roads[road.id] = road.level;
            }
            long gain = !initial && state.coins > coins ? state.coins - coins : 0;
            coins = state.coins; gift = state.lastGiftUtc; primed = true;
            var swap = completed; completed = scratch; scratch = swap;
            return new CityFeedbackFrame(gain, count, reward, opened == null ? Array.Empty<int>() : opened.ToArray());
        }

        private void AddCompletion(string key, bool initial, ref int count)
        { if (scratch.Add(key) && !initial && !completed.Contains(key)) count++; }
    }
}