using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    // --- Data model classes for JSON deserialization ---

    [Serializable]
    public class ConstantsData
    {
        public Dictionary<string, FearBase> fearBase = new();
        public Dictionary<string, int> squeal = new();
    }

    [Serializable]
    public class FearBase
    {
        public int baseVal;
        public int modifier;
    }

    [Serializable]
    public class ArchetypeData
    {
        public string id;
        public int weight;
        public IntRange intelligence;
        public Dictionary<string, SkillRange> skills;
    }

    [Serializable]
    public class IntRange
    {
        public int baseVal;
        public int range;
    }

    [Serializable]
    public class SkillRange
    {
        public int baseVal;
        public int range;
    }

    [Serializable]
    public class ArchetypesFile
    {
        public List<ArchetypeData> archetypes;
    }

    [Serializable]
    public class CrimesData
    {
        public List<CrimeDef> crimes;
    }

    [Serializable]
    public class CrimeDef
    {
        public string id;
        public int suspicion;
        public int sentence;
        public int investigation;
    }

    [Serializable]
    public class WeaponsData { }

    [Serializable]
    public class BusinessesData
    {
        public List<BusinessDef> legalBusinesses;
        public List<BusinessDef> illegalBusinesses;
        public Dictionary<string, int> profitGroups;
        public Dictionary<string, int> runningCostGroups;
    }

    [Serializable]
    public class BusinessDef
    {
        public string id;
        public string name;
        public int profitGroup;
        public int runningCostGroup;
        public int capacity = 1;
    }

    [Serializable]
    public class CityTemplate
    {
        public List<BlockTemplate> blocks;
        public List<PoliceBeatTemplate> policeBeats;
    }

    [Serializable]
    public class BlockTemplate
    {
        public string id;
        public string name;
        public int row;
        public int col;
        public int landValue;
        public int population;
        public bool playerHq;
        public bool rivalHq;
        public bool policeStation;
        public List<BusinessEntry> businesses;
    }

    [Serializable]
    public class BusinessEntry
    {
        public string type;
        public bool illegal;
        public int? count;
    }

    [Serializable]
    public class PoliceBeatTemplate
    {
        public string officerId;
        public string name;
        public List<string> beat;
        public int bribeCost;
    }

    // --- Game Data container ---

    public class GameData
    {
        public ConstantsData constants;
        public ArchetypeData[] archetypes;
        public CrimesData crimes;
        public WeaponsData weapons;
        public BusinessesData businesses;
        public CityTemplate cityTemplate;
    }
}
