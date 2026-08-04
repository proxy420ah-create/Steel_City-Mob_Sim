using UnityEngine;
using System.IO;

namespace SteelCity.Sim
{
    /// <summary>
    /// Bootstrap MonoBehaviour — initializes the game engine and runs a console test.
    /// Attach to a GameObject in the scene to verify the C# simulation core works.
    /// 
    /// NOTE: For interactive play, use GameManager instead (full UI).
    /// This script is for automated testing only.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private int randomSeed = -1;

        private GameEngine engine;

        void Start()
        {
            // Load data from StreamingAssets
            string dataDir = Path.Combine(Application.streamingAssetsPath);
            Debug.Log($"[GameBootstrap] Loading data from: {dataDir}");

            var gameData = DataLoader.LoadAll(dataDir);

            // Set random seed for reproducibility
            if (randomSeed >= 0)
            {
                var rng = new System.Random(randomSeed);
                CharacterGen.SetSeed(randomSeed);
                CityGen.SetSeed(randomSeed);
                CrimeSystem.SetSeed(randomSeed);
                EconomySystem.SetSeed(randomSeed);
                RivalAI.SetSeed(randomSeed);
            }

            // Create and setup engine
            engine = new GameEngine(gameData);
            engine.Setup();

            Debug.Log("[GameBootstrap] === SETUP COMPLETE ===");
            LogGameState();

            // Run 5-week automated test
            RunAutomatedTest();
        }

        private void RunAutomatedTest()
        {
            var player = engine.gangs["player"];
            var hoods = player.hoods;

            // Bribe first officer
            engine.BribeOfficer("officer_001");
            Debug.Log($"[GameBootstrap] Bribed officer_001. Treasury: ${player.money}");

            for (int weekNum = 1; weekNum <= 5; weekNum++)
            {
                Debug.Log($"[GameBootstrap] ===== WEEK {weekNum} — PLANNING =====");

                // Assign orders
                int assigned = 0;
                foreach (var hood in player.hoods)
                {
                    if (!hood.IsAvailable) continue;

                    string target;
                    string orderType;

                    if (weekNum <= 2)
                    {
                        target = hood == hoods[0] ? "block_4" : (hood == hoods[1] ? "block_8" : "block_7");
                        orderType = "extort";
                    }
                    else if (weekNum <= 3)
                    {
                        if (hood == hoods[0]) { target = "block_7"; orderType = "collect_protection"; }
                        else if (hood == hoods[1]) { target = "block_4"; orderType = "extort"; }
                        else { target = "block_8"; orderType = "extort"; }
                    }
                    else
                    {
                        if (hood == hoods[0]) { target = "block_7"; orderType = "collect_protection"; }
                        else if (hood == hoods[1]) { target = "block_4"; orderType = "patrol"; }
                        else { target = "block_8"; orderType = "collect_protection"; }
                    }

                    if (engine.AssignOrder(hood.id, target, orderType))
                    {
                        assigned++;
                        Debug.Log($"  {hood.name} -> {orderType} on {engine.blocks[target].name}");
                    }
                }

                Debug.Log($"  Orders assigned: {assigned}");

                // Run the week
                Debug.Log($"[GameBootstrap] >>> EXECUTING WEEK {weekNum} <<<");
                var stream = engine.RunWorkingWeek();
                Debug.Log(stream.GetTextReport());

                // Status
                int playerBlocks = 0, rivalBlocks = 0;
                foreach (var b in engine.blocks.Values)
                {
                    if (b.ownerGang == "player") playerBlocks++;
                    else if (b.ownerGang == "rival") rivalBlocks++;
                }

                int activeInv = 0;
                foreach (var inv in engine.investigations.Values)
                    if (inv.status == "active") activeInv++;

                Debug.Log($"[GameBootstrap] --- STATUS AFTER WEEK {weekNum} ---");
                Debug.Log($"  Treasury: ${player.money}");
                Debug.Log($"  Player territory: {playerBlocks} blocks");
                Debug.Log($"  Rival territory: {rivalBlocks} blocks");
                Debug.Log($"  Active investigations: {activeInv}");
            }

            Debug.Log("[GameBootstrap] === TEST COMPLETE ===");
        }

        private void LogGameState()
        {
            var player = engine.gangs["player"];
            var rival = engine.gangs["rival"];

            Debug.Log($"  Player: {player.name} | ${player.money} | {player.hoods.Count} hoods");
            Debug.Log($"  Rival:  {rival.name} | ${rival.money} | {rival.hoods.Count} hoods");
            Debug.Log($"  City: {engine.blocks.Count} blocks, {engine.businesses.Count} businesses, {engine.npcs.Count} NPCs");
            Debug.Log($"  Police: {engine.police.Count} officers");

            foreach (var hood in player.hoods)
                Debug.Log($"  Hood: {hood.name} (INT {hood.intelligence}) — {hood.SkillSummary}");
        }
    }
}
