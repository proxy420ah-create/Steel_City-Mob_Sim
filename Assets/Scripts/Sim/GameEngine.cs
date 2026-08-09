using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelCity.Sim
{
    [Serializable]
    public class Gang
    {
        public string id;
        public string name;
        public int money;
        public List<Hood> hoods = new();
        public bool isPlayer;
    }

    public class GameEngine
    {
        public GameData data;
        public int week = 0;
        public Dictionary<string, Block> blocks = new();
        public Dictionary<string, Business> businesses = new();
        public Dictionary<string, NPC> npcs = new();
        public List<PoliceOfficer> police = new();
        public Dictionary<string, Gang> gangs = new();
        public Dictionary<string, Investigation> investigations = new();
        public List<Order> pendingOrders = new();
        public EventStream eventStream;
        public List<CrimeEvent> weekCrimes = new();

        private int _crimeCounter = 0;
        private int _investCounter = 0;

        public GameEngine(GameData data)
        {
            this.data = data;
        }

        public void Setup()
        {
            CityGen.GenerateCity(
                data.cityTemplate, data.businesses, data.constants,
                out blocks, out businesses, out npcs, out police
            );

            // Player gang — single hood: Vinny
            var player = new Gang { id = "player", name = "Moretti Family", money = 3000, isPlayer = true };
            player.hoods = CharacterGen.GenerateStartingHoods("player", 1, data.archetypes);
            if (player.hoods.Count > 0)
                player.hoods[0].name = "Vinny Moretti";
            gangs["player"] = player;

            // Assign player HQ
            foreach (var block in blocks.Values)
            {
                if (block.isPlayerHq)
                {
                    block.ownerGang = "player";
                    block.extortionStrength = 50;
                    foreach (var bizId in block.businesses)
                    {
                        if (businesses.TryGetValue(bizId, out var biz) && !biz.isIllegal)
                        {
                            biz.ownerGang = "player";
                            break;
                        }
                    }

                    // Player hoods start located at HQ, inside the tenement (not yet on the sidewalk)
                    foreach (var hood in player.hoods)
                    {
                        hood.currentBlockId = block.id;
                        hood.isInsideBuilding = true;
                    }
                }
            }

            // Rival gang — single hood for simplified test
            var rival = new Gang { id = "rival", name = "Falcone Syndicate", money = 3000 };
            rival.hoods = CharacterGen.GenerateStartingHoods("rival", 1, data.archetypes);
            gangs["rival"] = rival;

            // Assign rival HQ
            foreach (var block in blocks.Values)
            {
                if (block.isRivalHq)
                {
                    block.ownerGang = "rival";
                    block.extortionStrength = 50;
                    foreach (var bizId in block.businesses)
                    {
                        if (businesses.TryGetValue(bizId, out var biz) && !biz.isIllegal)
                        {
                            biz.ownerGang = "rival";
                            break;
                        }
                    }
                }
            }

            week = 1;
        }

        public bool AssignOrder(string hoodId, string blockId, string orderType, string gangId = "player")
        {
            var hood = FindHood(hoodId);
            if (hood == null || !hood.IsAvailable) return false;

            var order = new Order { hoodId = hoodId, blockId = blockId, orderType = orderType, gangId = gangId, week = this.week };
            pendingOrders.Add(order);
            hood.status = HoodStatus.Assigned;
            hood.assignedOrder = order;
            return true;
        }

        public bool BribeOfficer(string officerId, string gangId = "player")
        {
            var officer = police.FirstOrDefault(o => o.id == officerId);
            if (!gangs.TryGetValue(gangId, out var gang)) return false;
            if (officer == null || gang.money < officer.bribeCost) return false;

            gang.money -= officer.bribeCost;
            officer.onPayroll = true;
            officer.payrollGang = gangId;
            return true;
        }

        public EventStream RunWorkingWeek()
        {
            eventStream = new EventStream(week);

            // Rival AI orders
            var rivalOrders = RivalAI.TakeTurn(
                "rival", blocks, gangs["rival"].hoods, businesses, data.crimes, week
            );
            var allOrders = new List<Order>(pendingOrders);
            allOrders.AddRange(rivalOrders);

            // Resolve orders
            foreach (var order in allOrders)
            {
                ResolveOrder(order);
                eventStream.AdvanceTime(1.0f);
            }

            // Squeal
            ProcessSqueal();

            // Investigations
            var arrests = CrimeSystem.UpdateInvestigations(investigations, week);
            foreach (var inv in arrests)
            {
                foreach (var hoodId in inv.targetHoods)
                {
                    var hood = FindHood(hoodId);
                    if (hood != null && hood.status != HoodStatus.Dead)
                    {
                        hood.status = HoodStatus.Arrested;
                        eventStream.Add("arrest", new Dictionary<string, object>
                        {
                            ["hood_name"] = hood.name,
                            ["gang_id"] = hood.gangId
                        });
                    }
                }
            }

            // Economy
            ProcessEconomy();

            // Clear pending orders
            pendingOrders.Clear();
            foreach (var gang in gangs.Values)
            {
                foreach (var hood in gang.hoods)
                {
                    if (hood.status == HoodStatus.Assigned)
                    {
                        hood.status = HoodStatus.Available;
                        hood.assignedOrder = null;
                    }
                }
            }

            week++;
            return eventStream;
        }

        private void ResolveOrder(Order order)
        {
            var hood = FindHood(order.hoodId);
            if (!blocks.TryGetValue(order.blockId, out var block)) return;
            if (hood == null) return;

            string hoodName = hood.name;
            string blockName = block.name;

            switch (order.orderType)
            {
                case "extort":
                    var (success, details, targetNpc) = CrimeSystem.ResolveExtortion(
                        hood, block, npcs, businesses, data.constants
                    );

                    if (success)
                    {
                        if (block.ownerGang == null || block.extortionStrength < 30)
                        {
                            block.ownerGang = order.gangId;
                            block.extortionStrength = Math.Max(block.extortionStrength, 20);
                            eventStream.Add("territory_change", new Dictionary<string, object>
                            {
                                ["block_name"] = blockName,
                                ["gang_id"] = order.gangId,
                                ["strength"] = block.extortionStrength
                            });
                        }
                        else
                        {
                            block.extortionStrength = Math.Min(100, block.extortionStrength + 10);
                        }
                        CreateCrimeEvent("extort", block, hood, order.gangId);
                    }
                    else if (order.gangId == "player")
                    {
                        eventStream.Add("notification", new Dictionary<string, object>
                        {
                            ["tier"] = "yellow",
                            ["message"] = $"{hoodName} reports: {details}"
                        });
                    }

                    eventStream.Add("order_result", new Dictionary<string, object>
                    {
                        ["hood_name"] = hoodName,
                        ["order_type"] = "extort",
                        ["block_name"] = blockName,
                        ["result"] = success ? "success" : "failure",
                        ["details"] = details
                    }, 0.1f);

                    if (order.gangId != "player")
                    {
                        eventStream.Add("rival_action", new Dictionary<string, object>
                        {
                            ["hood_name"] = hoodName,
                            ["order_type"] = "extort",
                            ["block_name"] = blockName,
                            ["result"] = success ? "success" : "failure"
                        });
                    }
                    break;

                case "collect_protection":
                    if (block.ownerGang == order.gangId && block.extortionStrength > 0)
                    {
                        block.extortionStrength = Math.Min(100, block.extortionStrength + 5);
                        eventStream.Add("order_result", new Dictionary<string, object>
                        {
                            ["hood_name"] = hoodName,
                            ["order_type"] = "collect_protection",
                            ["block_name"] = blockName,
                            ["result"] = "success",
                            ["details"] = $"Collected protection. Strength now {block.extortionStrength}."
                        });
                    }
                    else
                    {
                        eventStream.Add("order_result", new Dictionary<string, object>
                        {
                            ["hood_name"] = hoodName,
                            ["order_type"] = "collect_protection",
                            ["block_name"] = blockName,
                            ["result"] = "failure",
                            ["details"] = "We don't control this block."
                        });
                    }
                    break;

                case "patrol":
                    if (block.ownerGang == order.gangId)
                    {
                        block.extortionStrength = Math.Min(100, block.extortionStrength + 3);
                        eventStream.Add("order_result", new Dictionary<string, object>
                        {
                            ["hood_name"] = hoodName,
                            ["order_type"] = "patrol",
                            ["block_name"] = blockName,
                            ["result"] = "success",
                            ["details"] = $"Patrolled. Strength now {block.extortionStrength}."
                        });
                    }
                    else
                    {
                        eventStream.Add("order_result", new Dictionary<string, object>
                        {
                            ["hood_name"] = hoodName,
                            ["order_type"] = "patrol",
                            ["block_name"] = blockName,
                            ["result"] = "neutral",
                            ["details"] = "Patrolled neutral block."
                        });
                    }
                    break;

                case "intimidate":
                    var (iSuccess, iDetails, _) = CrimeSystem.ResolveIntimidation(hood, block, npcs);
                    CreateCrimeEvent("intimidate", block, hood, order.gangId);
                    eventStream.Add("order_result", new Dictionary<string, object>
                    {
                        ["hood_name"] = hoodName,
                        ["order_type"] = "intimidate",
                        ["block_name"] = blockName,
                        ["result"] = iSuccess ? "success" : "failure",
                        ["details"] = iDetails
                    });
                    break;

                case "lie_low":
                    foreach (var inv in investigations.Values)
                    {
                        if (inv.targetHoods.Contains(hood.id) && inv.status == "active")
                            inv.leads = Math.Max(0, inv.leads - 15);
                    }
                    eventStream.Add("order_result", new Dictionary<string, object>
                    {
                        ["hood_name"] = hoodName,
                        ["order_type"] = "lie_low",
                        ["block_name"] = blockName,
                        ["result"] = "success",
                        ["details"] = "Laying low. Investigation leads reduced."
                    });
                    break;
            }
        }

        private void CreateCrimeEvent(string crimeType, Block block, Hood hood, string gangId)
        {
            var crimeDef = data.crimes.crimes.FirstOrDefault(c => c.id == crimeType);
            if (crimeDef == null) return;

            _crimeCounter++;
            var crime = new CrimeEvent
            {
                id = $"crime_{_crimeCounter:D4}",
                crimeType = crimeType,
                blockId = block.id,
                hoodId = hood.id,
                gangId = gangId,
                suspicion = crimeDef.suspicion,
                sentence = crimeDef.sentence,
                investigationDifficulty = crimeDef.investigation,
                week = week
            };

            // Check if corrupt cop suppresses
            bool suppressed = false;
            foreach (var officer in police)
            {
                if (officer.onPayroll && officer.payrollGang == gangId && officer.beat.Contains(block.id))
                {
                    suppressed = true;
                    break;
                }
            }

            if (!suppressed)
                weekCrimes.Add(crime);
        }

        private void ProcessSqueal()
        {
            foreach (var crime in weekCrimes)
            {
                if (!blocks.TryGetValue(crime.blockId, out var block)) continue;
                var squealers = CrimeSystem.GenerateSqueal(crime, block, npcs, data.constants);

                if (squealers.Count > 0)
                {
                    crime.squealGenerated = true;
                    _investCounter++;
                    var inv = CrimeSystem.CreateInvestigation($"invest_{_investCounter:D4}", crime, squealers, npcs);
                    investigations[inv.id] = inv;

                    if (npcs.TryGetValue(squealers[0], out var squealerNpc))
                    {
                        eventStream.Add("squeal", new Dictionary<string, object>
                        {
                            ["npc_name"] = squealerNpc.name,
                            ["block_name"] = block.name,
                            ["crime_type"] = crime.crimeType
                        });
                        eventStream.Add("investigation", new Dictionary<string, object>
                        {
                            ["block_name"] = block.name,
                            ["leads"] = inv.leads,
                            ["threshold"] = inv.leadsThreshold
                        });

                        // Notification based on info tier
                        var playerBlocks = CityGen.GetBlocksByOwner(blocks, "player");
                        var playerBlockIds = new HashSet<string>(playerBlocks.ConvertAll(b => b.id));
                        var tier = playerBlockIds.Contains(block.id) ? block.InfoTier : InfoTier.Blind;

                        string tierStr = tier switch
                        {
                            InfoTier.Blind => "red",
                            InfoTier.Aware => "yellow",
                            InfoTier.Informed => "yellow",
                            InfoTier.Connected => "yellow",
                            _ => "yellow"
                        };

                        string msg = tier == InfoTier.Blind
                            ? $"Police are investigating activity on {block.name}. You don't know who talked."
                            : tier == InfoTier.Connected
                                ? $"{squealerNpc.name} on {block.name} squealed about the {crime.crimeType}. You know who it was."
                                : $"Someone on {block.name} squealed to the police about {crime.crimeType}.";

                        eventStream.Add("notification", new Dictionary<string, object>
                        {
                            ["tier"] = tierStr,
                            ["message"] = msg
                        });
                    }
                }
            }

            weekCrimes.Clear();
        }

        private void ProcessEconomy()
        {
            foreach (var (gangId, gang) in gangs)
            {
                var (income, expenses, breakdown) = EconomySystem.CalculateGangFinances(
                    gangId, blocks, businesses, npcs, data.businesses, police
                );

                // Hood payroll
                int hoodPayroll = gang.hoods.Count(h => h.status != HoodStatus.Dead) * 50;
                expenses += hoodPayroll;
                breakdown["payroll"] = hoodPayroll;

                int net = income - expenses;
                gang.money += net;

                if (gang.isPlayer)
                {
                    eventStream.Add("economy", new Dictionary<string, object>
                    {
                        ["income"] = income,
                        ["expenses"] = expenses,
                        ["net"] = net,
                        ["balance"] = gang.money,
                        ["breakdown"] = breakdown
                    });
                }
            }
        }

        public Hood FindHood(string hoodId)
        {
            foreach (var gang in gangs.Values)
                foreach (var hood in gang.hoods)
                    if (hood.id == hoodId) return hood;
            return null;
        }
    }
}
