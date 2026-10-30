// Offline equivalence test + benchmark for the world-fixed wall-column cache:
//   SolidVolumeSet.QueryCoverWallSets (per-volume emitted columns) + CoverWallClassifier (cached classification)
// vs the LEGACY path (SolidVolumeSet.QueryCoverWalls + the original per-scan classification loop, copied verbatim).
// Randomized volumes / yaw / body specs / owner support / threat, interleaved with voxel edits (ground changes + edit-log
// entries) and damage swaps (volume re-register). Marks, per-volume aggregates and class counts must be bit-identical.
//
// Run: Tools/cover_selftest/run.sh   (bundled Roslyn + .NET runtime, no Unity process)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using SteelCity.Sim;

struct Mark
{
    public Vector3 facePos, outDir; public float baseY, top, facing, vs, reqDepth, support; public byte cls; public string volId;
}
struct MarkFactory : IMarkFactory<Mark>
{
    public Mark Make(in CoverWall w, byte cls, float reqDepth, float support, float facing) => new Mark
    {
        facePos = w.facePos, outDir = w.outDir, baseY = w.firstSolidY, top = w.spanTop, cls = cls,
        facing = facing, volId = w.volumeId, vs = w.voxelSize, reqDepth = reqDepth, support = support,
    };
}

class FakeEdits : IEditLog
{
    public int Version { get; private set; }
    readonly List<Bounds> log = new List<Bounds> { default };   // index == version
    public void Log(Bounds b) { Version++; log.Add(b); }
    public bool ChangedSince(int since, Bounds window)
    {
        for (int v = since + 1; v <= Version; v++) if (log[v].Intersects(window)) return true;
        return false;
    }
}

static class Program
{
    static readonly System.Random rng = new System.Random(2026);
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static int checks, fails;

    // synthetic world ground — deterministic, with "no ground" holes and an editable bump region
    static readonly List<Bounds> bumps = new List<Bounds>();
    static bool Ground(Vector3 s, out float y)
    {
        int ix = Mathf.FloorToInt(s.x * 10f), iz = Mathf.FloorToInt(s.z * 10f);
        if (((ix * 7 + iz * 13) & 63) == 0) { y = 0f; return false; }              // no-ground holes (fallback path)
        y = 0.03f * (((ix >> 3) + (iz >> 3)) & 3);
        float cx = (ix + 0.5f) * 0.1f, cz = (iz + 0.5f) * 0.1f;   // cell-pure, like the real 0.1 m support cache
        foreach (var b in bumps) { var mn = b.min; var mx = b.max; if (cx >= mn.x && cx <= mx.x && cz >= mn.z && cz <= mx.z) y += 0.11f; }
        return true;
    }

    static uint[] MakeVoxels(int w, int h, int d, out string note)
    {
        var v = new uint[w * h * d];
        int boxes = rng.Next(3, 9);
        for (int b = 0; b < boxes; b++)
        {
            int x0 = rng.Next(0, w - 4), z0 = rng.Next(0, d - 4), y0 = rng.Next(0, h / 3);
            int bx = rng.Next(3, Math.Max(4, w / 2)), bz = rng.Next(3, Math.Max(4, d / 2)), by = rng.Next(3, h - y0);
            for (int z = z0; z < Math.Min(d, z0 + bz); z++)
                for (int y = y0; y < Math.Min(h, y0 + by); y++)
                    for (int x = x0; x < Math.Min(w, x0 + bx); x++) v[x + y * w + z * w * h] = 1u + (uint)rng.Next(3);
        }
        note = ""; return v;
    }

    class VolDef { public string id; public int w, h, d; public Vector3 corner; public float vs, yaw; }

    static CoverWallClassifier.Params RandParams() => new CoverWallClassifier.Params
    {
        hStand = rng.Next(2) == 0 ? 0.62f : 0.70f, hShallow = 0.50f, hDeep = rng.Next(2) == 0 ? 0.45f : 0.40f,
        standoff = 0.3f, wallFloor = 0.15f, depthMargin = 0.02f,
    };

    // ── LEGACY reference: SolidVolumeSet.QueryCoverWalls + the original ScanCore classification loop, verbatim ──
    static void Legacy(SolidVolumeSet set, Vector3 pos, float radius, CoverWallClassifier.Params p, float supportY, bool hasT, Vector3 tp,
        List<Mark> marks, Dictionary<string, (float d, byte c)> agg, out int nF, out int nC, out int nL, out int cols)
    {
        var buf = new List<CoverWall>();
        set.QueryCoverWalls(pos, radius, p.wallFloor, supportY + p.hDeep - 0.3f, buf);
        cols = buf.Count; nF = nC = nL = 0;
        var cache = new Dictionary<long, float>();
        float Local(Vector3 spot, float fb)
        {
            long key = ((long)Mathf.FloorToInt(spot.x * 10f) << 32) ^ (uint)Mathf.FloorToInt(spot.z * 10f);
            if (cache.TryGetValue(key, out float sy)) return sy;
            sy = Ground(spot, out float gy) ? gy : fb; cache[key] = sy; return sy;
        }
        for (int i = 0; i < buf.Count; i++)
        {
            CoverWall w = buf[i];
            float sy = Local(w.facePos + w.outDir * (p.standoff + w.voxelSize * 0.25f), supportY);
            if (w.spanTop - sy < p.wallFloor) continue;
            bool full = w.firstSolidY <= sy + p.hStand && sy + p.hStand < w.spanTop;
            bool crouch = w.firstSolidY <= sy + p.hDeep && sy + p.hDeep < w.spanTop;
            byte cls = full ? CoverWallClassifier.Full : crouch ? CoverWallClassifier.Crouch : CoverWallClassifier.Low;
            float reqD = cls == CoverWallClassifier.Low ? 0f : CoverWallClassifier.DuckForTop(in p, w.spanTop, sy);
            float facing = 0f;
            if (hasT) { Vector3 dt = tp - w.facePos; dt.y = 0f; facing = dt.sqrMagnitude > 1e-6f ? Vector3.Dot(w.outDir, dt.normalized) : 0f; }
            if (cls == CoverWallClassifier.Full) nF++; else if (cls == CoverWallClassifier.Crouch) nC++; else nL++;
            marks.Add(new MarkFactory().Make(in w, cls, reqD, sy, facing));
            if (cls != CoverWallClassifier.Low && w.volumeId != null)
                if (!agg.TryGetValue(w.volumeId, out var pd) || reqD > pd.d) agg[w.volumeId] = (reqD, cls);
        }
    }

    static bool Same(Mark a, Mark b) =>
        a.facePos == b.facePos && a.outDir == b.outDir && a.baseY == b.baseY && a.top == b.top && a.facing == b.facing &&
        a.vs == b.vs && a.reqDepth == b.reqDepth && a.support == b.support && a.cls == b.cls && (object)a.volId == (object)b.volId;

    static void Check(bool ok, string what) { checks++; if (!ok) { fails++; if (fails <= 12) Console.WriteLine("  FAIL: " + what); } }

    static int Main()
    {
        var defs = new List<VolDef>();
        var set = new SolidVolumeSet();
        var edits = new FakeEdits();
        var classifier = new CoverWallClassifier();
        CoverWallClassifier.GroundProbe gp = Ground;
        string note;
        for (int k = 0; k < 14; k++)
        {
            var d = new VolDef
            {
                id = "b" + k, w = rng.Next(60, 150), h = rng.Next(30, 70), d = rng.Next(60, 150),
                corner = new Vector3(R(-5, 30), 0f, R(-5, 30)), vs = rng.Next(2) == 0 ? 0.05f : 0.1f, yaw = rng.Next(4) == 0 ? R(-60, 60) : 0f,
            };
            defs.Add(d);
            set.Register(d.id, MakeVoxels(d.w, d.h, d.d, out note), d.w, d.h, d.d, d.corner, d.vs, d.yaw);
        }

        Console.WriteLine("== Equivalence: cached wall sets + classifier vs legacy emit + per-scan loop ==");
        var sets = new List<SolidVolumeSet.VolumeWalls>();
        int hits = 0, builds = 0, edit = 0, swaps = 0;
        float burstSupport = 0f; var burstParams = RandParams();
        for (int it = 0; it < 1500; it++)
        {
            int ev = rng.Next(100);
            if (ev < 12)   // voxel edit: ground bump over a region + edit-log entry
            {
                var b = new Bounds(new Vector3(R(-5, 35), 0f, R(-5, 35)), new Vector3(R(0.3f, 6f), 2f, R(0.3f, 6f)));
                bumps.Add(b); edits.Log(b); edit++;
            }
            else if (ev < 20)   // damage swap: re-register a volume with fresh voxels
            {
                var d = defs[rng.Next(defs.Count)];
                set.Register(d.id, MakeVoxels(d.w, d.h, d.d, out note), d.w, d.h, d.d, d.corner, d.vs, d.yaw);
                edits.Log(new Bounds(d.corner + new Vector3(d.w, d.h, d.d) * d.vs * 0.5f, new Vector3(d.w, d.h, d.d) * d.vs)); swaps++;
            }
            var pos = new Vector3(R(0, 28), 0f, R(0, 28));
            // specs/support held in bursts of 25 so the HIT path (cached classification surviving edits) is exercised hard
            if (it % 25 == 0) { burstSupport = new[] { 0f, 0.03f, 0.06f, 0.11f, 0.3f }[rng.Next(5)]; burstParams = RandParams(); }
            float supportY = burstSupport; var p = burstParams;
            bool hasT = rng.Next(3) != 0; var tp = new Vector3(R(-5, 35), 0f, R(-5, 35));

            var lm = new List<Mark>(); var la = new Dictionary<string, (float d, byte c)>();
            Legacy(set, pos, 8f, p, supportY, hasT, tp, lm, la, out int lF, out int lC, out int lL, out int lCols);

            set.QueryCoverWallSets(pos, 8f, p.wallFloor, supportY + p.hDeep - 0.3f, sets);
            int cols = 0; foreach (var s in sets) cols += s.walls.Count;
            var nm = new List<Mark>(); var na = new List<CoverWallClassifier.VolAgg>();
            int nF = 0, nC = 0, nL = 0;
            classifier.Classify<Mark, MarkFactory>(sets, p, supportY, gp, edits, hasT, tp, default, nm, na, ref nF, ref nC, ref nL);
            hits += classifier.LastHits; builds += classifier.LastBuilds;

            Check(cols == lCols, $"wall column count {cols} != {lCols}");
            Check(nm.Count == lm.Count, $"mark count {nm.Count} != {lm.Count}");
            if (nm.Count == lm.Count) for (int i = 0; i < nm.Count; i++) if (!Same(nm[i], lm[i]))
            {
                var x = nm[i]; var y = lm[i];
                string f = x.facePos != y.facePos ? "facePos" : x.outDir != y.outDir ? "outDir" : x.baseY != y.baseY ? "baseY" : x.top != y.top ? "top"
                    : x.facing != y.facing ? "facing" : x.vs != y.vs ? "vs" : x.reqDepth != y.reqDepth ? $"reqDepth {x.reqDepth} vs {y.reqDepth}"
                    : x.support != y.support ? $"support {x.support} vs {y.support}" : x.cls != y.cls ? $"cls {x.cls} vs {y.cls}" : "volId";
                Check(false, $"mark #{i} differs in {f} (hasT {hasT}, supportY {supportY})"); break;
            }
            Check(nF == lF && nC == lC && nL == lL, $"class counts {nF}/{nC}/{nL} != {lF}/{lC}/{lL}");
            var nad = new Dictionary<string, (float d, byte c)>(); foreach (var a in na) nad[a.volId] = (a.depth, a.cls);
            bool aggOk = nad.Count == la.Count;
            if (aggOk) foreach (var kv in la) if (!nad.TryGetValue(kv.Key, out var v) || v.d != kv.Value.d || v.c != kv.Value.c) { aggOk = false; break; }
            Check(aggOk, "per-volume aggregates differ");
        }
        Console.WriteLine($"   iterations 1500 · edits {edit} · damage swaps {swaps} · classifier cache hits {hits} / rebuilds {builds}");
        Console.WriteLine($"   checks {checks}   fails {fails}   wall-cache columns held {set.WallCacheColumns}");
        Console.WriteLine(fails == 0 ? "\nEQUIVALENCE: PASS (bit-identical)" : $"\nEQUIVALENCE: FAIL ({fails})");

        // ── benchmark: WARM scan (nothing changed) — legacy loop vs cached path, ground already cached for both ──
        Console.WriteLine("\n== Benchmark: warm scan, radius 8 m, fixed position ==");
        bumps.Clear();
        var bp = new Vector3(14f, 0f, 14f); var prm = RandParams();
        var groundCache = new Dictionary<long, float>();
        CoverWallClassifier.GroundProbe cachedGround = (Vector3 s, out float y) =>
        {
            long key = ((long)Mathf.FloorToInt(s.x * 10f) << 32) ^ (uint)Mathf.FloorToInt(s.z * 10f);
            if (groundCache.TryGetValue(key, out float v)) { y = v; return !float.IsNaN(v); }
            bool ok = Ground(s, out float gy); groundCache[key] = ok ? gy : float.NaN; y = ok ? gy : 0f; return ok;
        };
        var wbuf = new List<CoverWall>(); var marks = new List<Mark>();
        // legacy with a persistent ground cache (== the CURRENT shipped state before this change)
        float LocalPersist(Vector3 spot, float fb) => cachedGround(spot, out float y) ? y : fb;
        double Bench(Action a, int n)
        {
            for (int i = 0; i < 20; i++) a();
            GC.Collect(); var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) a(); sw.Stop(); return sw.Elapsed.TotalMilliseconds / n;
        }
        int colsB = 0;
        double legacyMs = Bench(() =>
        {
            marks.Clear(); set.QueryCoverWalls(bp, 8f, prm.wallFloor, 0f + prm.hDeep - 0.3f, wbuf); colsB = wbuf.Count;
            var vd = new Dictionary<string, float>();
            for (int i = 0; i < wbuf.Count; i++)
            {
                CoverWall w = wbuf[i];
                float sy = LocalPersist(w.facePos + w.outDir * (prm.standoff + w.voxelSize * 0.25f), 0f);
                if (w.spanTop - sy < prm.wallFloor) continue;
                bool full = w.firstSolidY <= sy + prm.hStand && sy + prm.hStand < w.spanTop;
                bool crouch = w.firstSolidY <= sy + prm.hDeep && sy + prm.hDeep < w.spanTop;
                byte cls = full ? CoverWallClassifier.Full : crouch ? CoverWallClassifier.Crouch : CoverWallClassifier.Low;
                float reqD = cls == CoverWallClassifier.Low ? 0f : CoverWallClassifier.DuckForTop(in prm, w.spanTop, sy);
                Vector3 dt = new Vector3(5f, 0f, 5f) - w.facePos; dt.y = 0f;
                float facing = dt.sqrMagnitude > 1e-6f ? Vector3.Dot(w.outDir, dt.normalized) : 0f;
                marks.Add(new MarkFactory().Make(in w, cls, reqD, sy, facing));
                if (cls != CoverWallClassifier.Low && w.volumeId != null) if (!vd.TryGetValue(w.volumeId, out float pd) || reqD > pd) vd[w.volumeId] = reqD;
            }
        }, 60);
        int bF = 0, bC = 0, bL = 0; var bAgg = new List<CoverWallClassifier.VolAgg>();
        double cachedMs = Bench(() =>
        {
            marks.Clear(); bAgg.Clear(); bF = bC = bL = 0;
            set.QueryCoverWallSets(bp, 8f, prm.wallFloor, 0f + prm.hDeep - 0.3f, sets);
            classifier.Classify<Mark, MarkFactory>(sets, prm, 0f, cachedGround, edits, true, new Vector3(5f, 0f, 5f), default, marks, bAgg, ref bF, ref bC, ref bL);
        }, 60);
        Console.WriteLine($"   {colsB} wall columns in range");
        Console.WriteLine($"   legacy (emit + classify, ground cached): {legacyMs,7:F3} ms     cached wall sets + classifier: {cachedMs,7:F3} ms     => {legacyMs / cachedMs:F1}x faster");
        return fails == 0 ? 0 : 1;
    }
}
