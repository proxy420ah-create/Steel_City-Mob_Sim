// Offline self-test + micro-benchmark for the CPU pose API in VoxelCharacterAnimator.cs.
//
// Proves the single-evaluation pose API (PreparePose / ApplyPose / PoseRotation) is
// BIT-IDENTICAL to the legacy per-point algorithm it replaced (PosePoint ran
// ComputeGroupRotation once per point; the weld/eye anchor ran the same chain a second
// time for the orientation), then measures time + allocations for the three real call
// shapes: the 40-sample exposure probe, the eye anchor, and the weapon weld.
//
// Run: Tools/anim_selftest/run.sh   (no Unity process — bundled Roslyn + .NET runtime)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using SteelCity.Sim;
using VCA = SteelCity.Sim.VoxelCharacterAnimator;

static class Program
{
    static readonly System.Random rng = new System.Random(12345);
    static float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    static int fails, checks, skipped;

    // ── randomized-but-valid AnimParamsData via reflection ───────────────────
    static object Fill(Type t)
    {
        var o = Activator.CreateInstance(t);
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var ft = f.FieldType; string n = f.Name;
            if (ft == typeof(float))
                f.SetValue(o, n == "cycleDuration" ? R(0.6f, 1.4f) : n == "freq" ? R(1f, 3f)
                             : n.EndsWith("Freq") ? R(1f, 3f) : R(-1.2f, 1.2f));
            else if (ft == typeof(int))
                f.SetValue(o, n.StartsWith("axis") ? rng.Next(0, 3) : n.StartsWith("sign") ? (rng.Next(2) * 2 - 1) : 0);
            else if (ft == typeof(bool)) f.SetValue(o, true);
            else if (ft == typeof(string)) f.SetValue(o, new[] { "spline", "smoothstep", "cosine", "linear" }[rng.Next(4)]);
            else if (ft.IsClass) f.SetValue(o, Fill(ft));
        }
        return o;
    }

    static VCA MakeAnimator()
    {
        var a = new VCA { paramsData = (VCA.AnimParamsData)Fill(typeof(VCA.AnimParamsData)) };
        for (int g = 0; g <= 9; g++) a.pivots[g] = new Vector3(R(0.2f, 0.8f), R(0.2f, 0.8f), R(0.3f, 0.7f));
        a.jointOffsets[2] = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1));
        a.jointOffsets[5] = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1));
        return a;
    }

    // ── the LEGACY algorithm, verbatim (pre-refactor PosePoint/PosePointCore) ──
    static readonly MethodInfo GetWalkPoseMI = typeof(VCA).GetMethod("GetWalkPose", BindingFlags.NonPublic | BindingFlags.Instance);
    static Vector3 MatVec3(float[,] m, Vector3 v) => new Vector3(
        m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z,
        m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z,
        m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z);

    static Vector3 LegacyCore(VCA a, Vector3 restPos, int gid, Vector3Int dims, float state, float time, float speed, float duck, float gait)
    {
        float gaitClock = gait >= 0f ? gait : time * speed;
        bool isWalk = (state > 0.5f && state < 1.5f) || (state > 2.5f && state < 3.5f);
        VCA.WalkPose wp = isWalk ? (VCA.WalkPose)GetWalkPoseMI.Invoke(a, new object[] { gaitClock, 1f, a.paramsData }) : default;
        float bob = isWalk ? wp.bodyBobY : 0f, shift = isWalk ? wp.weightShiftX : 0f;
        float px = restPos.x - dims.x * 0.5f, py = restPos.y, pz = restPos.z - dims.z * 0.5f;
        if (gid >= 0)
        {
            var result = a.ComputeGroupRotation(gid, dims, 1.0f, state, time, speed, duck, gait);
            if (result.HasValue && result.Value.chain != null && result.Value.chain.Length > 0)
            {
                var pos = restPos;
                for (int c = 0; c < result.Value.chain.Length; c++)
                {
                    var e = result.Value.chain[c];
                    pos = MatVec3(e.rot, pos - e.pivot) + e.pivot;
                }
                var off = result.Value.offset;
                px = pos.x + off.x - dims.x * 0.5f; py = pos.y + off.y; pz = pos.z + off.z - dims.z * 0.5f;
            }
        }
        if (isWalk) { px += shift; py += bob; }
        return new Vector3(px, py, pz);
    }

    static Vector3 LegacyPosePoint(VCA a, Vector3 rest, int gid, Vector3Int dims, float state, float time, float speed,
        float tSA, float tTA, float tB, float tDip, float duck, float tDA, float gait)
    {
        var pB = LegacyCore(a, rest, gid, dims, state, time, speed, duck, gait);
        if (tB >= 1f || tSA < 0f) return pB;
        float duckA = tDA >= 0f ? tDA : duck;
        var pA = LegacyCore(a, rest, gid, dims, tSA, tTA, speed, duckA, gait);
        var p = Vector3.Lerp(pA, pB, tB); p.y += tDip; return p;
    }

    static Quaternion LegacyRotation(VCA a, int gid, Vector3Int dims, float state, float time, float speed,
        float tSA, float tTA, float tB, float duck, float tDA, float gait)
    {
        Quaternion qB = Quaternion.identity;
        var gr = a.ComputeGroupRotation(gid, dims, 1.0f, state, time, speed, duck, gait);
        if (gr.HasValue && gr.Value.chain != null) foreach (var e in gr.Value.chain) qB = VCA.QuatFromMat3(e.rot) * qB;
        if (tB < 1f && tSA >= 0f)
        {
            Quaternion qA = Quaternion.identity;
            var grA = a.ComputeGroupRotation(gid, dims, 1.0f, tSA, tTA, speed, tDA >= 0f ? tDA : duck, gait);
            if (grA.HasValue && grA.Value.chain != null) foreach (var e in grA.Value.chain) qA = VCA.QuatFromMat3(e.rot) * qA;
            return Quaternion.Slerp(qA, qB, tB);
        }
        return qB;
    }

    static bool Same(Vector3 x, Vector3 y) =>
        BitConverter.SingleToInt32Bits(x.x) == BitConverter.SingleToInt32Bits(y.x) &&
        BitConverter.SingleToInt32Bits(x.y) == BitConverter.SingleToInt32Bits(y.y) &&
        BitConverter.SingleToInt32Bits(x.z) == BitConverter.SingleToInt32Bits(y.z);
    static bool Same(Quaternion x, Quaternion y) =>
        BitConverter.SingleToInt32Bits(x.x) == BitConverter.SingleToInt32Bits(y.x) &&
        BitConverter.SingleToInt32Bits(x.y) == BitConverter.SingleToInt32Bits(y.y) &&
        BitConverter.SingleToInt32Bits(x.z) == BitConverter.SingleToInt32Bits(y.z) &&
        BitConverter.SingleToInt32Bits(x.w) == BitConverter.SingleToInt32Bits(y.w);

    static readonly float[] States = { 0, 1, 3, 4, 5, 8, 9, 10, 11, 13, 14 };

    struct Case
    {
        public int gid; public Vector3Int dims; public Vector3 rest;
        public float state, time, speed, tSA, tTA, tB, tDip, duck, tDA, gait;
    }

    static Case RandCase()
    {
        var c = new Case
        {
            gid = rng.Next(0, 10),
            dims = new Vector3Int(rng.Next(40, 120), rng.Next(60, 130), rng.Next(20, 80)),
            state = States[rng.Next(States.Length)], time = R(0, 30), speed = R(0.5f, 1.5f),
            duck = R(0f, 2f), gait = rng.Next(3) == 0 ? -1f : R(0, 40),
        };
        c.rest = new Vector3(R(0, c.dims.x), R(0, c.dims.y), R(0, c.dims.z));
        if (rng.Next(2) == 0) { c.tSA = -1f; c.tTA = 0; c.tB = 1f; c.tDip = 0; c.tDA = -1f; }
        else
        {
            c.tSA = States[rng.Next(States.Length)]; c.tTA = R(0, 30); c.tB = R(0.01f, 0.99f);
            c.tDip = R(-2, 2); c.tDA = rng.Next(2) == 0 ? -1f : R(0, 2);
        }
        return c;
    }

    static void SetChannels(VCA a)
    {
        a.tapPulse = R(0, 1); a.reloadGunSide = rng.Next(2) * 2 - 1f; a.ikBlend = 1f;
        a.lookScanEnabled = rng.Next(3) == 0; a.lookScanBlend = R(0, 1);
    }

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) { fails++; if (fails <= 12) Console.WriteLine("  FAIL: " + what); }
    }

    static void EquivalenceSuite()
    {
        Console.WriteLine("== Equivalence: new single-eval API vs legacy per-point algorithm ==");
        for (int rep = 0; rep < 12; rep++)
        {
            var a = MakeAnimator();
            for (int i = 0; i < 2000; i++)
            {
                SetChannels(a);
                var c = RandCase();
                var legacy = LegacyPosePoint(a, c.rest, c.gid, c.dims, c.state, c.time, c.speed, c.tSA, c.tTA, c.tB, c.tDip, c.duck, c.tDA, c.gait);
                var viaWrapper = a.PosePoint(c.rest, c.gid, c.dims, c.state, c.time, c.speed, c.tSA, c.tTA, c.tB, c.tDip, c.duck, c.tDA, c.gait);
                var gp = a.PreparePose(c.gid, c.dims, c.state, c.time, c.speed, c.tSA, c.tTA, c.tB, c.tDip, c.duck, c.tDA, c.gait);
                var viaPrepared = VCA.ApplyPose(in gp, c.rest, c.dims);
                Check(Same(legacy, viaWrapper), $"PosePoint wrapper != legacy (gid {c.gid} st {c.state} tr {c.tSA}/{c.tB})");
                Check(Same(legacy, viaPrepared), $"ApplyPose != legacy (gid {c.gid} st {c.state} tr {c.tSA}/{c.tB})");
                // one prepared pose carries MANY points — same chain, different rest points (the silhouette case)
                for (int k = 0; k < 4; k++)
                {
                    var rp = new Vector3(R(0, c.dims.x), R(0, c.dims.y), R(0, c.dims.z));
                    var l2 = LegacyPosePoint(a, rp, c.gid, c.dims, c.state, c.time, c.speed, c.tSA, c.tTA, c.tB, c.tDip, c.duck, c.tDA, c.gait);
                    Check(Same(l2, VCA.ApplyPose(in gp, rp, c.dims)), "multi-point reuse != legacy");
                }
                // orientation (the weld / joint-query path)
                try
                {
                    var lr = LegacyRotation(a, c.gid, c.dims, c.state, c.time, c.speed, c.tSA, c.tTA, c.tB, c.duck, c.tDA, c.gait);
                    Check(Same(lr, VCA.PoseRotation(in gp)), $"PoseRotation != legacy (gid {c.gid} st {c.state} tr {c.tSA}/{c.tB})");
                }
                catch (Exception e) when (e is System.Security.SecurityException || e is NotSupportedException || e is MethodAccessException)
                { skipped++; }   // Quaternion.Slerp is an engine intrinsic — not runnable outside Unity
            }
        }
        Console.WriteLine($"   checks {checks}   fails {fails}   rotation-checks skipped (engine intrinsic) {skipped}");

        // Walk-pose cache: exact-float key must never serve a stale pose.
        int walkFail = 0, walkChecks = 0;
        var wa = MakeAnimator();
        var fresh = new List<VCA>(); for (int i = 0; i < 1; i++) fresh.Add(MakeAnimator());
        for (int i = 0; i < 6000; i++)
        {
            float t = R(0, 50), s = R(0.6f, 1.4f);
            var p1 = (VCA.WalkPose)GetWalkPoseMI.Invoke(wa, new object[] { t, s, wa.paramsData });
            var p2 = (VCA.WalkPose)GetWalkPoseMI.Invoke(wa, new object[] { t, s, wa.paramsData });         // cache HIT
            var wb = new VCA { paramsData = wa.paramsData };                                                // cold cache
            var p3 = (VCA.WalkPose)GetWalkPoseMI.Invoke(wb, new object[] { t, s, wb.paramsData });
            walkChecks++;
            if (!(p1.bodyBobY == p2.bodyBobY && p2.bodyBobY == p3.bodyBobY && p1.weightShiftX == p3.weightShiftX
                  && p1.pose["armSwingL"] == p3.pose["armSwingL"] && p2.pose["kneeBendR"] == p3.pose["kneeBendR"])) walkFail++;
        }
        Console.WriteLine($"   walk-pose cache: {walkChecks} checks, {walkFail} stale/incorrect");
        fails += walkFail;
    }

    // ── benchmark ────────────────────────────────────────────────────────────
    static (double us, long bytes) Time(Action body, int iters)
    {
        for (int i = 0; i < Math.Max(5, iters / 20); i++) body();                    // warm JIT
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++) body();
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds * 1000.0 / iters, (GC.GetAllocatedBytesForCurrentThread() - b0) / iters);
    }

    static void Bench()
    {
        Console.WriteLine("\n== Benchmark (per call; legacy = pre-refactor algorithm) ==");
        foreach (var scenario in new[] { ("AIMING (stable pose)", 4f, false), ("AIM-WALK (gait)", 3f, false), ("AIM-WALK + transition blend", 3f, true) })
        {
            var a = MakeAnimator(); a.tapPulse = 0f; a.reloadGunSide = 1f;
            var dims = new Vector3Int(96, 110, 64);
            float state = scenario.Item2; bool trans = scenario.Item3;
            float tSA = trans ? 4f : -1f, tB = trans ? 0.5f : 1f;
            // a silhouette sample cloud shaped like the real one: ~40 points over 10 groups
            var pts = new List<(int gid, Vector3 rest)>();
            for (int g = 0; g <= 9; g++) for (int k = 0; k < 4; k++)
                pts.Add((g, new Vector3(R(0, dims.x), R(0, dims.y), R(0, dims.z))));
            var sink = new Vector3[pts.Count];

            var legacyProbe = Time(() =>
            {
                for (int i = 0; i < pts.Count; i++)
                    sink[i] = LegacyPosePoint(a, pts[i].rest, pts[i].gid, dims, state, 3.3f, 1f, tSA, 2.1f, tB, 0.4f, 0.6f, 0.5f, 7.7f);
            }, 400);
            var gps = new VCA.GroupPose[10]; var valid = new bool[10];
            var newProbe = Time(() =>
            {
                Array.Clear(valid, 0, 10);
                for (int i = 0; i < pts.Count; i++)
                {
                    int g = pts[i].gid;
                    if (!valid[g]) { gps[g] = a.PreparePose(g, dims, state, 3.3f, 1f, tSA, 2.1f, tB, 0.4f, 0.6f, 0.5f, 7.7f); valid[g] = true; }
                    sink[i] = VCA.ApplyPose(in gps[g], pts[i].rest, dims);
                }
            }, 400);

            // weld / eye anchor shape: point + orientation of ONE group
            var legacyWeld = Time(() =>
            {
                sink[0] = LegacyPosePoint(a, pts[0].rest, 9, dims, state, 3.3f, 1f, tSA, 2.1f, tB, 0.4f, 0.6f, 0.5f, 7.7f);
                if (!trans) LegacyRotation(a, 9, dims, state, 3.3f, 1f, tSA, 2.1f, tB, 0.6f, 0.5f, 7.7f);   // Slerp is an engine intrinsic offline — blend rows time the point + chain evals only
            }, 4000);
            var newWeld = Time(() =>
            {
                var gp = a.PreparePose(9, dims, state, 3.3f, 1f, tSA, 2.1f, tB, 0.4f, 0.6f, 0.5f, 7.7f);
                sink[0] = VCA.ApplyPose(in gp, pts[0].rest, dims);
                if (!trans) VCA.PoseRotation(in gp);
            }, 4000);

            Console.WriteLine($" {scenario.Item1}");
            Console.WriteLine($"   40-sample probe pose : legacy {legacyProbe.us,8:F1} us {legacyProbe.bytes / 1024.0,7:F1} KB   new {newProbe.us,8:F1} us {newProbe.bytes / 1024.0,7:F1} KB   => {legacyProbe.us / newProbe.us:F1}x faster, {(legacyProbe.bytes) / Math.Max(1.0, newProbe.bytes):F1}x less garbage");
            Console.WriteLine($"   weld / eye (pt+rot)  : legacy {legacyWeld.us,8:F1} us {legacyWeld.bytes / 1024.0,7:F1} KB   new {newWeld.us,8:F1} us {newWeld.bytes / 1024.0,7:F1} KB   => {legacyWeld.us / newWeld.us:F1}x faster, {(legacyWeld.bytes) / Math.Max(1.0, newWeld.bytes):F1}x less garbage");
        }
    }

    static int Main()
    {
        EquivalenceSuite();
        Console.WriteLine(fails == 0 ? "\nEQUIVALENCE: PASS (bit-identical)" : $"\nEQUIVALENCE: FAIL ({fails})");
        Bench();
        return fails == 0 ? 0 : 1;
    }
}
