using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    [Serializable]
    public class GameEvent
    {
        public float time;
        public string type;
        public Dictionary<string, object> data = new();

        public GameEvent(float time, string type, Dictionary<string, object> data)
        {
            this.time = time;
            this.type = type;
            this.data = data;
        }
    }

    public class EventStream
    {
        public int week;
        public List<GameEvent> events = new();
        private float _time = 0f;

        public EventStream(int week) => this.week = week;

        public float CurrentTime => _time;

        public void Add(string eventType, Dictionary<string, object> data, float timeOffset = 0f)
        {
            events.Add(new GameEvent(_time + timeOffset, eventType, data));
        }

        public void AdvanceTime(float duration) => _time += duration;

        public string GetTextReport()
        {
            var lines = new List<string> { $"=== Week {week} Event Log ===\n" };

            foreach (var ev in events)
            {
                var d = ev.data;
                switch (ev.type)
                {
                    case "order_result":
                        lines.Add($"  {d["hood_name"]} -> {d["order_type"]} on {d["block_name"]}: {d["result"]}");
                        if (d.TryGetValue("details", out var det) && !string.IsNullOrEmpty(det?.ToString()))
                            lines.Add($"    Details: {det}");
                        break;
                    case "squeal":
                        lines.Add($"  ⚠ SQUEAL: {d["npc_name"]} talked to police about {d["block_name"]}");
                        break;
                    case "investigation":
                        lines.Add($"  🔍 INVESTIGATION: {d["block_name"]} - Leads: {d["leads"]}/{d["threshold"]}");
                        break;
                    case "arrest":
                        lines.Add($"  🚔 ARREST: {d["hood_name"]} arrested!");
                        break;
                    case "rival_action":
                        lines.Add($"  [RIVAL] {d["hood_name"]} -> {d["order_type"]} on {d["block_name"]}: {d["result"]}");
                        break;
                    case "economy":
                        lines.Add($"  💰 Economy: Income ${d["income"]}, Expenses ${d["expenses"]}, Net ${d["net"]}");
                        break;
                    case "territory_change":
                        lines.Add($"  🏴 Territory: {d["block_name"]} now controlled by {d["gang_id"] ?? "nobody"} (strength: {d["strength"]})");
                        break;
                    case "notification":
                        var tier = d.TryGetValue("tier", out var t) ? t?.ToString() : "green";
                        var prefix = tier switch { "green" => "  ℹ", "yellow" => "  ⚠", "red" => "  🚨", _ => "  ℹ" };
                        lines.Add($"{prefix} [{tier.ToUpper()}] {d["message"]}");
                        break;
                }
            }

            return string.Join("\n", lines);
        }
    }
}
