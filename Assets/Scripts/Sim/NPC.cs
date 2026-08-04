using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    [Serializable]
    public class NPC
    {
        public string id;
        public string name;
        public string npcType; // business_owner, civilian, police
        public string blockId;
        public string businessId;
        public int fear = 100;
        public int hostility = 50;
        public int squeal = 100;
        public bool alive = true;

        public bool IsCompliant => fear > hostility;
    }

    public static class CharacterGen
    {
        public static readonly string[] Skills =
        {
            "organisation", "business", "firearms", "fists", "knives",
            "arson", "explosives", "intimidation", "driving", "stealth"
        };

        private static readonly string[] HoodNames =
        {
            "Vinny Moretti", "Frankie Russo", "Sal Bianchi", "Tony Caruso",
            "Mikey Falcone", "Nicky Lombardi", "Eddie Greco", "Paulie Vitale",
            "Joey Marino", "Carmine Romano", "Luigi Esposito", "Dominic Ricci"
        };

        private static readonly string[] NpcNames =
        {
            "Tony the Butcher", "Old Man Patterson", "Mrs. O'Sullivan",
            "Jimmy the Baker", "Sal the Barber", "Katherine Doyle",
            "Eddie the Mechanic", "Rose Calabrese", "Pat Flanagan",
            "Angie Morretti", "Tom Kelly", "Maria Costa"
        };

        private static Random rng = new();

        public static void SetSeed(int seed) => rng = new Random(seed);

        public static Hood GenerateHood(string hoodId, string gangId, ArchetypeData[] archetypes)
        {
            // Weighted random selection
            int totalWeight = 0;
            foreach (var a in archetypes) totalWeight += a.weight;
            int roll = rng.Next(totalWeight);
            int acc = 0;
            ArchetypeData chosen = archetypes[0];
            foreach (var a in archetypes)
            {
                acc += a.weight;
                if (roll < acc) { chosen = a; break; }
            }

            int intelligence = chosen.intelligence.baseVal + rng.Next(0, chosen.intelligence.range + 1);

            var skills = new Dictionary<string, int>();
            foreach (var skillName in Skills)
            {
                var s = chosen.skills[skillName];
                skills[skillName] = Math.Clamp(s.baseVal + rng.Next(0, s.range + 1), 0, 63);
            }

            return new Hood
            {
                id = hoodId,
                name = HoodNames[rng.Next(HoodNames.Length)],
                intelligence = intelligence,
                skills = skills,
                gangId = gangId
            };
        }

        public static List<Hood> GenerateStartingHoods(string gangId, int count, ArchetypeData[] archetypes, int startId = 0)
        {
            var hoods = new List<Hood>();
            for (int i = 0; i < count; i++)
                hoods.Add(GenerateHood($"hood_{gangId}_{startId + i:D3}", gangId, archetypes));
            return hoods;
        }

        public static NPC GenerateNPC(string npcId, string npcType, string blockId, ConstantsData constants, string businessId = null)
        {
            var fearBase = constants.fearBase.TryGetValue(npcType, out var fb)
                ? fb
                : constants.fearBase["civilian"];

            int baseFear = fearBase.baseVal + fearBase.modifier;
            int squealVal = constants.squeal.TryGetValue(npcType, out var sv) ? sv : constants.squeal["civilian"];

            return new NPC
            {
                id = npcId,
                name = NpcNames[rng.Next(NpcNames.Length)],
                npcType = npcType,
                blockId = blockId,
                businessId = businessId,
                fear = Math.Max(0, baseFear + rng.Next(-20, 21)),
                hostility = rng.Next(30, 71),
                squeal = squealVal
            };
        }

        public static List<NPC> GenerateBlockNPCs(string blockId, int population, ConstantsData constants, int businessCount = 0)
        {
            var npcs = new List<NPC>();
            for (int i = 0; i < businessCount; i++)
                npcs.Add(GenerateNPC($"npc_{blockId}_biz_{i}", "business_owner", blockId, constants));
            for (int i = 0; i < population - businessCount; i++)
                npcs.Add(GenerateNPC($"npc_{blockId}_civ_{i}", "civilian", blockId, constants));
            return npcs;
        }
    }
}
