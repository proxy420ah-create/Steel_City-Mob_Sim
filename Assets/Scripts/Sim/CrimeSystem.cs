using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    [Serializable]
    public class Investigation
    {
        public string id;
        public string blockId;
        public List<string> crimes = new();
        public int leads;
        public int leadsThreshold = 100;
        public List<string> targetHoods = new();
        public string status = "active"; // active, closed, resulted_in_arrest
        public string detectiveId;
    }

    [Serializable]
    public class CrimeEvent
    {
        public string id;
        public string crimeType;
        public string blockId;
        public string hoodId;
        public string gangId;
        public int suspicion;
        public int sentence;
        public int investigationDifficulty;
        public string result = "";
        public string details = "";
        public bool squealGenerated;
        public int week;
    }

    public static class CrimeSystem
    {
        private static Random rng = new();

        public static void SetSeed(int seed) => rng = new Random(seed);

        public static (bool success, string details, NPC target) ResolveExtortion(
            Hood hood, Block block, Dictionary<string, NPC> npcs,
            Dictionary<string, Business> businesses, ConstantsData constants)
        {
            var bizNpcs = new List<NPC>();
            foreach (var nid in block.npcs)
            {
                if (npcs.TryGetValue(nid, out var npc) && npc.npcType == "business_owner" && npc.alive)
                    bizNpcs.Add(npc);
            }

            if (bizNpcs.Count == 0)
                return (false, "No business owners to extort in this block.", null);

            var target = bizNpcs[rng.Next(bizNpcs.Count)];
            int hoodIntimidation = hood.GetSkill("intimidation");
            int pressure = hoodIntimidation + rng.Next(0, 21);
            int resistance = target.hostility + rng.Next(0, 21);

            if (target.fear > target.hostility)
            {
                target.fear = Math.Min(255, target.fear + 5);
                return (true, $"{target.name} paid up without trouble (fear {target.fear} > hostility {target.hostility}).", target);
            }

            if (pressure > resistance)
            {
                int fearGain = rng.Next(15, 36);
                int hostilityGain = rng.Next(0, 11);
                target.fear = Math.Min(255, target.fear + fearGain);
                target.hostility = Math.Min(255, target.hostility + hostilityGain);
                return (true, $"{target.name} paid after pressure (fear +{fearGain}, now {target.fear}). Some resentment (hostility +{hostilityGain}).", target);
            }

            return (false, $"{target.name} refused to pay (pressure {pressure} vs resistance {resistance}). Fear {target.fear}, Hostility {target.hostility}.", target);
        }

        public static (bool success, string details, NPC target) ResolveIntimidation(
            Hood hood, Block block, Dictionary<string, NPC> npcs, NPC targetNpc = null)
        {
            NPC target = targetNpc;
            if (target == null)
            {
                var bizNpcs = new List<NPC>();
                foreach (var nid in block.npcs)
                {
                    if (npcs.TryGetValue(nid, out var npc) && npc.npcType == "business_owner" && npc.alive)
                        bizNpcs.Add(npc);
                }
                if (bizNpcs.Count == 0)
                    return (false, "No one to intimidate in this block.", null);
                target = bizNpcs[rng.Next(bizNpcs.Count)];
            }

            int pressure = hood.GetSkill("intimidation") + rng.Next(10, 31);
            int resistance = target.hostility + rng.Next(0, 16);

            int fearGain = rng.Next(20, 51);
            int hostilityGain = rng.Next(5, 21);
            target.fear = Math.Min(255, target.fear + fearGain);
            target.hostility = Math.Min(255, target.hostility + hostilityGain);

            if (target.fear > target.hostility)
                return (true, $"{target.name} is now compliant (fear {target.fear} > hostility {target.hostility}). But remembers the threat.", target);
            return (false, $"{target.name} still refusing (fear {target.fear}, hostility {target.hostility}). The threat made them angrier.", target);
        }

        public static List<string> GenerateSqueal(CrimeEvent crime, Block block, Dictionary<string, NPC> npcs, ConstantsData constants)
        {
            var squealers = new List<string>();
            foreach (var nid in block.npcs)
            {
                if (!npcs.TryGetValue(nid, out var npc) || !npc.alive || npc.npcType == "police")
                    continue;

                int squealValue = npc.squeal;
                if (npc.fear > 150)
                    squealValue = (int)(squealValue * 1.3);

                int roll = rng.Next(0, 256);
                if (roll < squealValue)
                    squealers.Add(npc.id);
            }
            return squealers;
        }

        public static Investigation CreateInvestigation(string investId, CrimeEvent crime, List<string> squealers, Dictionary<string, NPC> npcs)
        {
            int leads = crime.investigationDifficulty * 10;
            return new Investigation
            {
                id = investId,
                blockId = crime.blockId,
                crimes = new List<string> { crime.id },
                leads = leads,
                targetHoods = new List<string> { crime.hoodId }
            };
        }

        public static List<Investigation> UpdateInvestigations(Dictionary<string, Investigation> investigations, int week)
        {
            var arrests = new List<Investigation>();
            foreach (var inv in investigations.Values)
            {
                if (inv.status != "active") continue;
                inv.leads = Math.Max(0, inv.leads - 5);
                if (inv.leads >= inv.leadsThreshold)
                {
                    inv.status = "resulted_in_arrest";
                    arrests.Add(inv);
                }
            }
            return arrests;
        }
    }
}
