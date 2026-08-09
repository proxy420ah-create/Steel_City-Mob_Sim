using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    /// <summary>
    /// Loads JSON game data files from StreamingAssets or a data folder.
    /// Uses JsonUtility for simple types and manual parsing for Dictionary-based data.
    /// </summary>
    public static class DataLoader
    {
        /// <summary>
        /// Load all game data from the given directory path.
        /// Expects: constants.json, archetypes.json, crimes.json, weapons.json, businesses.json, city_template.json
        /// </summary>
        public static GameData LoadAll(string dataDir)
        {
            var data = new GameData
            {
                constants = LoadConstants(Path.Combine(dataDir, "constants.json")),
                archetypes = LoadArchetypes(Path.Combine(dataDir, "archetypes.json")),
                crimes = LoadCrimes(Path.Combine(dataDir, "crimes.json")),
                weapons = LoadWeapons(Path.Combine(dataDir, "weapons.json")),
                businesses = LoadBusinesses(Path.Combine(dataDir, "businesses.json")),
                cityTemplate = LoadCityTemplate(Path.Combine(dataDir, "city_template.json"))
            };
            return data;
        }

        private static string ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[DataLoader] File not found: {path}");
                return null;
            }
            return File.ReadAllText(path);
        }

        private static ConstantsData LoadConstants(string path)
        {
            var json = ReadFile(path);
            if (json == null) return new ConstantsData();

            // JsonUtility doesn't support Dictionary, so we parse manually
            var data = new ConstantsData();
            var root = JSONNode.Parse(json);

            // fear_base
            var fearBase = root["fear_base"];
            if (fearBase != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in (JSONObject)fearBase)
                {
                    data.fearBase[kv.Key] = new FearBase
                    {
                        baseVal = kv.Value["base"].AsInt,
                        modifier = kv.Value["modifier"].AsInt
                    };
                }
            }

            // squeal
            var squeal = root["squeal"];
            if (squeal != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in (JSONObject)squeal)
                {
                    data.squeal[kv.Key] = kv.Value.AsInt;
                }
            }

            return data;
        }

        private static ArchetypeData[] LoadArchetypes(string path)
        {
            var json = ReadFile(path);
            if (json == null) return new ArchetypeData[0];

            var root = JSONNode.Parse(json);
            var archetypesNode = root["archetypes"].AsArray;
            var result = new ArchetypeData[archetypesNode.Count];

            for (int i = 0; i < archetypesNode.Count; i++)
            {
                var node = archetypesNode[i];
                var arch = new ArchetypeData
                {
                    id = node["name"]?.Value ?? "",
                    weight = node["weight"]?.AsInt ?? 0,
                    intelligence = new IntRange
                    {
                        baseVal = node["intelligence"]?["base"]?.AsInt ?? 0,
                        range = node["intelligence"]?["range"]?.AsInt ?? 0
                    },
                    skills = new System.Collections.Generic.Dictionary<string, SkillRange>()
                };

                var skillsNode = node["skills"];
                if (skillsNode != null)
                {
                    foreach (KeyValuePair<string, JSONNode> kv in (JSONObject)skillsNode)
                    {
                        arch.skills[kv.Key] = new SkillRange
                        {
                            baseVal = kv.Value["base"]?.AsInt ?? 0,
                            range = kv.Value["range"]?.AsInt ?? 0
                        };
                    }
                }

                result[i] = arch;
            }

            return result;
        }

        private static CrimesData LoadCrimes(string path)
        {
            var json = ReadFile(path);
            if (json == null) return new CrimesData();

            var root = JSONNode.Parse(json);
            var crimesNode = root["crimes"].AsArray;
            var data = new CrimesData { crimes = new System.Collections.Generic.List<CrimeDef>() };

            for (int i = 0; i < crimesNode.Count; i++)
            {
                var node = crimesNode[i];
                data.crimes.Add(new CrimeDef
                {
                    id = node["id"].Value,
                    suspicion = node["suspicion"].AsInt,
                    sentence = node["sentence"].AsInt,
                    investigation = node["investigation"].AsInt
                });
            }

            return data;
        }

        private static WeaponsData LoadWeapons(string path)
        {
            return new WeaponsData(); // Placeholder — weapons not yet used
        }

        private static BusinessesData LoadBusinesses(string path)
        {
            var json = ReadFile(path);
            if (json == null) return new BusinessesData();

            var root = JSONNode.Parse(json);
            var data = new BusinessesData
            {
                legalBusinesses = new System.Collections.Generic.List<BusinessDef>(),
                illegalBusinesses = new System.Collections.Generic.List<BusinessDef>(),
                profitGroups = new System.Collections.Generic.Dictionary<string, int>(),
                runningCostGroups = new System.Collections.Generic.Dictionary<string, int>()
            };

            // Legal businesses
            var legal = root["legal_businesses"].AsArray;
            if (legal != null)
            {
                for (int i = 0; i < legal.Count; i++)
                {
                    var node = legal[i];
                    data.legalBusinesses.Add(new BusinessDef
                    {
                        id = node["id"].Value,
                        name = node["name"].Value,
                        profitGroup = node["profit_group"].AsInt,
                        runningCostGroup = node["running_cost_group"].AsInt,
                        capacity = node["capacity"]?.AsInt ?? 1
                    });
                }
            }

            // Illegal businesses
            var illegal = root["illegal_businesses"].AsArray;
            if (illegal != null)
            {
                for (int i = 0; i < illegal.Count; i++)
                {
                    var node = illegal[i];
                    data.illegalBusinesses.Add(new BusinessDef
                    {
                        id = node["id"].Value,
                        name = node["name"].Value,
                        profitGroup = node["profit_group"].AsInt,
                        runningCostGroup = 0,
                        capacity = node["capacity"]?.AsInt ?? 1
                    });
                }
            }

            // Profit groups
            var pg = root["profit_groups"];
            if (pg != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in (JSONObject)pg)
                    data.profitGroups[kv.Key] = kv.Value.AsInt;
            }

            // Running cost groups
            var rcg = root["running_cost_groups"];
            if (rcg != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in (JSONObject)rcg)
                    data.runningCostGroups[kv.Key] = kv.Value.AsInt;
            }

            return data;
        }

        private static CityTemplate LoadCityTemplate(string path)
        {
            var json = ReadFile(path);
            if (json == null) return new CityTemplate();

            var root = JSONNode.Parse(json);
            var data = new CityTemplate
            {
                blocks = new System.Collections.Generic.List<BlockTemplate>(),
                policeBeats = new System.Collections.Generic.List<PoliceBeatTemplate>()
            };

            var blocksNode = root["blocks"].AsArray;
            if (blocksNode != null)
            {
                for (int i = 0; i < blocksNode.Count; i++)
                {
                    var node = blocksNode[i];
                    var bt = new BlockTemplate
                    {
                        id = node["id"].Value,
                        name = node["name"].Value,
                        row = node["row"].AsInt,
                        col = node["col"].AsInt,
                        landValue = node["land_value"].AsInt,
                        population = node["population"].AsInt,
                        playerHq = node["player_hq"]?.AsBool ?? false,
                        rivalHq = node["rival_hq"]?.AsBool ?? false,
                        policeStation = node["police_station"]?.AsBool ?? false,
                        businesses = new System.Collections.Generic.List<BusinessEntry>()
                    };

                    var bizNode = node["businesses"].AsArray;
                    if (bizNode != null)
                    {
                        for (int j = 0; j < bizNode.Count; j++)
                        {
                            var bn = bizNode[j];
                            bt.businesses.Add(new BusinessEntry
                            {
                                type = bn["type"].Value,
                                illegal = bn["illegal"]?.AsBool ?? false,
                                count = bn["count"]?.AsInt
                            });
                        }
                    }

                    data.blocks.Add(bt);
                }
            }

            var beatsNode = root["police_beats"]?.AsArray;
            if (beatsNode != null)
            {
                for (int i = 0; i < beatsNode.Count; i++)
                {
                    var node = beatsNode[i];
                    var beat = new List<string>();
                    var beatArr = node["beat"].AsArray;
                    if (beatArr != null)
                        for (int j = 0; j < beatArr.Count; j++)
                            beat.Add(beatArr[j].Value);

                    data.policeBeats.Add(new PoliceBeatTemplate
                    {
                        officerId = node["officer_id"].Value,
                        name = node["name"].Value,
                        beat = beat,
                        bribeCost = node["bribe_cost"].AsInt
                    });
                }
            }

            return data;
        }
    }
}
