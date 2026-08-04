using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    public enum InfoTier { Blind, Aware, Informed, Connected }

    [Serializable]
    public class Business
    {
        public string id;
        public string blockId;
        public string type;
        public string name;
        public bool isIllegal;
        public string ownerGang;
        public int profitGroup;
        public int runningCostGroup;
        public int capacity = 1;
        public bool active = true;
    }

    [Serializable]
    public class Block
    {
        public string id;
        public string name;
        public int row;
        public int col;
        public int landValue;
        public int population;
        public List<string> businesses = new();
        public List<string> npcs = new();
        public string ownerGang;
        public int extortionStrength;
        public bool isPlayerHq;
        public bool isRivalHq;
        public bool isPoliceStation;

        public InfoTier InfoTier
        {
            get
            {
                if (extortionStrength >= 67) return InfoTier.Connected;
                if (extortionStrength >= 34) return InfoTier.Informed;
                if (extortionStrength > 0) return InfoTier.Aware;
                return InfoTier.Blind;
            }
        }

        public List<(int row, int col)> AdjacentOffsets => new()
        {
            (row - 1, col), (row + 1, col),
            (row, col - 1), (row, col + 1)
        };
    }

    [Serializable]
    public class PoliceOfficer
    {
        public string id;
        public string name;
        public List<string> beat = new();
        public int bribeCost;
        public bool onPayroll;
        public string payrollGang;
    }

    public static class CityGen
    {
        private static Random rng = new();

        public static void SetSeed(int seed) => rng = new Random(seed);

        public static void GenerateCity(
            CityTemplate template,
            BusinessesData businessesData,
            ConstantsData constants,
            out Dictionary<string, Block> blocks,
            out Dictionary<string, Business> allBusinesses,
            out Dictionary<string, NPC> allNpcs,
            out List<PoliceOfficer> police)
        {
            blocks = new();
            allBusinesses = new();
            allNpcs = new();

            var bizDefs = new Dictionary<string, BusinessDef>();
            foreach (var b in businessesData.legalBusinesses) bizDefs[b.id] = b;
            var illegalDefs = new Dictionary<string, BusinessDef>();
            foreach (var b in businessesData.illegalBusinesses) illegalDefs[b.id] = b;

            foreach (var bd in template.blocks)
            {
                var block = new Block
                {
                    id = bd.id,
                    name = bd.name,
                    row = bd.row,
                    col = bd.col,
                    landValue = bd.landValue,
                    population = bd.population,
                    isPlayerHq = bd.playerHq,
                    isRivalHq = bd.rivalHq,
                    isPoliceStation = bd.policeStation
                };

                int bizCount = 0;
                foreach (var be in bd.businesses)
                {
                    string bizType = be.type;
                    bool isIllegal = be.illegal;
                    int count = be.count ?? 1;

                    for (int i = 0; i < count; i++)
                    {
                        string bizId = $"biz_{block.id}_{bizType}_{i}";
                        BusinessDef defn = null;
                        bool illegal = false;

                        if (isIllegal && illegalDefs.TryGetValue(bizType, out var idefn))
                        {
                            defn = idefn;
                            illegal = true;
                        }
                        else if (bizDefs.TryGetValue(bizType, out var ldefn))
                        {
                            defn = ldefn;
                        }
                        else continue;

                        var biz = new Business
                        {
                            id = bizId,
                            blockId = block.id,
                            type = bizType,
                            name = defn.name,
                            isIllegal = illegal,
                            profitGroup = defn.profitGroup,
                            runningCostGroup = illegal ? 0 : defn.runningCostGroup,
                            capacity = defn.capacity
                        };

                        allBusinesses[bizId] = biz;
                        block.businesses.Add(bizId);
                        bizCount++;
                    }
                }

                // Generate NPCs
                var npcs = CharacterGen.GenerateBlockNPCs(block.id, block.population, constants, bizCount);
                foreach (var npc in npcs)
                    allNpcs[npc.id] = npc;
                block.npcs = new List<string>(npcs.ConvertAll(n => n.id));

                blocks[block.id] = block;
            }

            // Generate police
            police = new List<PoliceOfficer>();
            foreach (var pd in template.policeBeats)
            {
                police.Add(new PoliceOfficer
                {
                    id = pd.officerId,
                    name = pd.name,
                    beat = new List<string>(pd.beat),
                    bribeCost = pd.bribeCost
                });
            }
        }

        public static List<Block> GetBlocksByOwner(Dictionary<string, Block> blocks, string gangId)
        {
            var result = new List<Block>();
            foreach (var b in blocks.Values)
                if (b.ownerGang == gangId) result.Add(b);
            return result;
        }

        public static List<Block> GetAdjacentBlocks(Dictionary<string, Block> blocks, Block block)
        {
            var result = new List<Block>();
            foreach (var (r, c) in block.AdjacentOffsets)
                foreach (var b in blocks.Values)
                    if (b.row == r && b.col == c) result.Add(b);
            return result;
        }
    }
}
