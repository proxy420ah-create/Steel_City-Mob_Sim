using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    public static class EconomySystem
    {
        private static Random rng = new();

        public static void SetSeed(int seed) => rng = new Random(seed);

        public static int CalculateBusinessIncome(Business business, BusinessesData businessesData, int landValue, string ownerGang)
        {
            if (!business.active || business.ownerGang != ownerGang) return 0;

            string pg = business.profitGroup.ToString();
            int profitValue = businessesData.profitGroups.TryGetValue(pg, out var pv) ? pv : 0;

            double lvModifier = 1.0 + (landValue * 0.1);
            double fluctuation = rng.NextDouble() * 0.4 + 0.8; // 0.8 to 1.2

            return (int)(profitValue * lvModifier * fluctuation);
        }

        public static int CalculateRunningCosts(Business business, BusinessesData businessesData, int landValue)
        {
            string cg = business.runningCostGroup.ToString();
            int baseCost = businessesData.runningCostGroups.TryGetValue(cg, out var bc) ? bc : 0;
            double lvModifier = 1.0 + (landValue * 0.05);
            return (int)(baseCost * lvModifier);
        }

        public static int CalculateProtectionIncome(Block block, Dictionary<string, NPC> npcs)
        {
            int total = 0;
            foreach (var nid in block.npcs)
            {
                if (!npcs.TryGetValue(nid, out var npc) || !npc.alive || npc.npcType != "business_owner") continue;
                if (!npc.IsCompliant) continue;
                total += 20 + (npc.fear / 10);
            }
            double strengthFactor = block.extortionStrength / 100.0;
            return (int)(total * strengthFactor);
        }

        public static float CalculateMarketShareFactor(int businessesOwned)
        {
            if (businessesOwned <= 1) return 1.0f;
            if (businessesOwned <= 5) return 0.80f;
            if (businessesOwned <= 10) return 0.79f;
            if (businessesOwned <= 15) return 0.65f;
            if (businessesOwned <= 20) return 0.57f;
            if (businessesOwned <= 27) return 0.50f;
            if (businessesOwned <= 35) return 0.42f;
            return 0.03f;
        }

        public static (int income, int expenses, Dictionary<string, int> breakdown) CalculateGangFinances(
            string gangId, Dictionary<string, Block> blocks,
            Dictionary<string, Business> businesses, Dictionary<string, NPC> npcs,
            BusinessesData businessesData, List<PoliceOfficer> police)
        {
            int income = 0;
            int expenses = 0;
            var breakdown = new Dictionary<string, int>
            {
                ["business_income"] = 0,
                ["protection_income"] = 0,
                ["payroll"] = 0,
                ["running_costs"] = 0,
                ["bribes"] = 0
            };

            // Business income
            var ownedBusinesses = new List<Business>();
            foreach (var b in businesses.Values)
                if (b.ownerGang == gangId && !b.isIllegal && b.active)
                    ownedBusinesses.Add(b);

            float marketFactor = CalculateMarketShareFactor(ownedBusinesses.Count);

            foreach (var biz in ownedBusinesses)
            {
                if (!blocks.TryGetValue(biz.blockId, out var block)) continue;
                int gross = CalculateBusinessIncome(biz, businessesData, block.landValue, gangId);
                int net = (int)(gross * marketFactor);
                int costs = CalculateRunningCosts(biz, businessesData, block.landValue);
                income += net;
                expenses += costs;
                breakdown["business_income"] += net;
                breakdown["running_costs"] += costs;
            }

            // Protection income
            foreach (var block in blocks.Values)
            {
                if (block.ownerGang == gangId && block.extortionStrength > 0)
                {
                    int prot = CalculateProtectionIncome(block, npcs);
                    income += prot;
                    breakdown["protection_income"] += prot;
                }
            }

            // Police bribes
            foreach (var officer in police)
            {
                if (officer.onPayroll && officer.payrollGang == gangId)
                {
                    expenses += officer.bribeCost;
                    breakdown["bribes"] += officer.bribeCost;
                }
            }

            return (income, expenses, breakdown);
        }
    }
}
