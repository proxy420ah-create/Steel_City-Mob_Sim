using System;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Dice-combat flight recorder — one flat text file both fighters append
    /// to, readable between test phases without the editor console.
    /// &lt;project&gt;/Logs/dice_trace.log (previous run rotates to
    /// dice_trace_prev.log). Every line starts with the same t{SimClock.Now}
    /// stamp the SEAM panel's dice history uses, so a panel reading can be
    /// found in the file by timestamp.
    /// </summary>
    public static class DiceTrace
    {
        static StreamWriter w;
        public static string Path { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Close(); }

        public static void Write(string line)
        {
            if (w == null && !Open()) return;
            w.WriteLine(line);
        }

        static bool Open()
        {
            try
            {
                string dir = System.IO.Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(dir);
                Path = System.IO.Path.Combine(dir, "dice_trace.log");
                string prev = System.IO.Path.Combine(dir, "dice_trace_prev.log");
                if (File.Exists(Path)) File.Copy(Path, prev, true);
                w = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                { AutoFlush = true };
                w.WriteLine($"# dice trace — {DateTime.Now:s} — t = SimClock.Now (sim seconds, 0.08s fixed step); same clock as the SEAM panel history");
                Application.quitting += Close;
                Debug.Log($"🧾 [DiceTrace] recording → {Path}");
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"🔴 [DiceTrace] cannot open trace file: {e.Message}"); return false; }
        }

        static void Close()
        {
            try { w?.Dispose(); } catch { }
            w = null;
        }
    }
}
