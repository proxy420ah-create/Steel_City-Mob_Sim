using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelCity.Sim
{
    public enum HoodHealth { Healthy, Winded, LightlyWounded, BadlyWounded, Dead }
    public enum HoodStatus { Available, Assigned, Arrested, Dead }

    [Serializable]
    public class Hood
    {
        public string id;
        public string name;
        public int intelligence;
        public Dictionary<string, int> skills = new();
        public int loyalty = 200;
        public HoodHealth health = HoodHealth.Healthy;
        public HoodStatus status = HoodStatus.Available;
        public Order assignedOrder;
        public string gangId = "";

        public bool IsAvailable =>
            status == HoodStatus.Available && health != HoodHealth.BadlyWounded && health != HoodHealth.Dead;

        public string SkillSummary
        {
            get
            {
                var top = skills.OrderByDescending(kv => kv.Value).Take(3);
                return string.Join(", ", top.Select(kv => $"{kv.Key}={kv.Value}"));
            }
        }

        public int GetSkill(string name) => skills.TryGetValue(name, out var v) ? v : 0;
    }

    [Serializable]
    public class Order
    {
        public string hoodId;
        public string blockId;
        public string orderType;
        public string gangId;
        public int week;
    }
}
