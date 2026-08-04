using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelCity.Sim
{
    public static class RivalAI
    {
        private static Random rng = new();

        public static void SetSeed(int seed) => rng = new Random(seed);

        public static List<Order> TakeTurn(
            string gangId, Dictionary<string, Block> blocks,
            List<Hood> hoods, Dictionary<string, Business> businesses,
            CrimesData crimesData, int week)
        {
            var orders = new List<Order>();
            var availableHoods = hoods.Where(h => h.gangId == gangId && h.IsAvailable).ToList();

            if (availableHoods.Count == 0) return orders;

            var ownedBlocks = blocks.Values.Where(b => b.ownerGang == gangId).ToList();

            // Find unowned non-police blocks
            var targetBlocks = blocks.Values
                .Where(b => b.ownerGang == null && !b.isPoliceStation)
                .ToList();

            // Prioritize adjacent to owned territory
            var adjacentTargets = new List<Block>();
            foreach (var owned in ownedBlocks)
            {
                foreach (var (r, c) in owned.AdjacentOffsets)
                {
                    foreach (var b in blocks.Values)
                    {
                        if (b.row == r && b.col == c && b.ownerGang == null && !adjacentTargets.Contains(b))
                            adjacentTargets.Add(b);
                    }
                }
            }

            var targets = adjacentTargets.Count > 0 ? adjacentTargets : targetBlocks.Take(3).ToList();

            for (int i = 0; i < availableHoods.Count; i++)
            {
                var hood = availableHoods[i];
                if (i < targets.Count)
                {
                    var target = targets[i % targets.Count];
                    orders.Add(new Order
                    {
                        hoodId = hood.id,
                        blockId = target.id,
                        orderType = "extort",
                        gangId = gangId,
                        week = week
                    });
                }
                else if (ownedBlocks.Count > 0)
                {
                    var patrolTarget = ownedBlocks[rng.Next(ownedBlocks.Count)];
                    orders.Add(new Order
                    {
                        hoodId = hood.id,
                        blockId = patrolTarget.id,
                        orderType = "patrol",
                        gangId = gangId,
                        week = week
                    });
                }
            }

            return orders;
        }
    }
}
