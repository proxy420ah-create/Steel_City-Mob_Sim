using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Lightweight build diagnostics — a timestamped ring-buffer event log plus
    /// an ordered key/value state table, consumed by the HUD "BuildLog" tab.
    /// Designed for player builds where Player.log is the only window into what
    /// the build actually resolved (shader stripping, missing init steps, empty
    /// draw lists). Cheap enough to leave always-on: Event = one string append,
    /// Set = one dictionary write; neither allocates per-frame.
    /// </summary>
    public static class BuildDiag
    {
        const int MaxEvents = 256;
        static readonly List<string> events = new();
        static readonly List<string> stateKeys = new();
        static readonly Dictionary<string, string> state = new();

        /// <summary>Append a timestamped init/milestone line to the ring buffer.</summary>
        public static void Event(string msg)
        {
            events.Add($"[{Time.realtimeSinceStartup,7:F2}] {msg}");
            if (events.Count > MaxEvents)
                events.RemoveRange(0, events.Count - MaxEvents);
        }

        /// <summary>
        /// Capture the whole Unity log stream into the ring buffer — in a player,
        /// warnings/errors that would otherwise need Player.log show up in the
        /// BuildLog tab directly. Warnings/errors are type-tagged.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookUnityLog()
        {
            events.Clear();
            Application.logMessageReceived += OnUnityLog;
        }

        static void OnUnityLog(string msg, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
            {
                events.Add($"[{Time.realtimeSinceStartup,7:F2}] {msg}");
            }
            else
            {
                events.Add($"[{Time.realtimeSinceStartup,7:F2}] <b>{type}</b> {msg}");
            }
            if (events.Count > MaxEvents)
                events.RemoveRange(0, events.Count - MaxEvents);
        }

        /// <summary>Set a live state value (refreshed in place; keys keep first-set order).</summary>
        public static void Set(string key, string value)
        {
            if (!state.ContainsKey(key)) stateKeys.Add(key);
            state[key] = value;
        }

        public static void Set(string key, object value) => Set(key, value != null ? value.ToString() : "null");

        public static IReadOnlyList<string> Events => events;
        public static IReadOnlyList<string> StateKeys => stateKeys;
        public static bool TryGet(string key, out string value) => state.TryGetValue(key, out value);
    }
}
