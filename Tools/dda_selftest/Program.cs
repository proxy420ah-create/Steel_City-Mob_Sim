using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using UnityEngine;
using SteelCity.Sim;

// Offline self-test for VoxelDda + SolidVolumeSet (no Unity runtime needed: only Vector3/Mathf from UnityEngine.CoreModule).
static class Program
{
    static int fails, checks;
    static readonly System.Random rng = new System.Random(20261011);
    static double R(double a, double b) => a + rng.NextDouble() * (b - a);
    static void Check(bool ok, string msg)
    {
        checks++;
        if (!ok) { fails++; if (fails <= 30) Console.WriteLine("  FAIL: " + msg); }
    }
    static string V(double[] a) => $"({a[0]:G6},{a[1]:G6},{a[2]:G6})";

    static uint[] RandomGrid(int w, int h, int d, double density)
    {
        var g = new uint[w * h * d];
        for (int i = 0; i < g.Length; i++)
        {
            double r = rng.NextDouble();
            if (r < density) g[i] = (uint)rng.Next(1, 148) | ((uint)rng.Next(0, 16) << 12);   // material + shape bits (shape must not matter)
            else if (r < density + 0.05) g[i] = (uint)rng.Next(1, 16) << 12;                  // shape bits but material 0 => AIR
        }
        return g;
    }

    // Exhaustive reference: slab-test every solid cell, keep the earliest entry.
    static bool Brute(uint[] g, int w, int h, int d, double[] o, double[] dir, double tMax,
        out int bx, out int by, out int bz, out double bt, out int nAxis, out int nSign, out int ties)
    {
        bx = by = bz = -1; bt = double.PositiveInfinity; nAxis = -1; nSign = 0; ties = 0;
        for (int z = 0; z < d; z++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if ((g[x + y * w + z * w * h] & 0x1FFu) == 0) continue;
                    double t0 = 0, t1 = tMax; bool ok = true; int ax = -1, sg = 0;
                    int[] c = { x, y, z };
                    for (int a = 0; a < 3 && ok; a++)
                    {
                        if (dir[a] == 0) { if (!(o[a] >= c[a] && o[a] < c[a] + 1)) ok = false; continue; }
                        double ta = (c[a] - o[a]) / dir[a], tb = (c[a] + 1 - o[a]) / dir[a];
                        if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                        if (ta > t0) { t0 = ta; ax = a; sg = dir[a] > 0 ? -1 : 1; }
                        if (tb < t1) t1 = tb;
                        if (t0 > t1) ok = false;
                    }
                    if (!ok) continue;
                    if (t0 < bt - 1e-9) { bt = t0; bx = x; by = y; bz = z; nAxis = ax; nSign = sg; ties = 1; }
                    else if (Math.Abs(t0 - bt) <= 1e-9) ties++;
                }
        return bx >= 0;
    }

    static void TestDdaVsBrute()
    {
        int[][] dims = { new[] { 7, 5, 9 }, new[] { 16, 16, 16 }, new[] { 1, 1, 1 }, new[] { 1, 9, 3 }, new[] { 32, 3, 2 }, new[] { 5, 5, 5 } };
        double[] dens = { 0.02, 0.15, 0.6 };
        int rays = 0, hits = 0;
        foreach (var dm in dims)
            foreach (var den in dens)
            {
                int w = dm[0], h = dm[1], d = dm[2];
                var g = RandomGrid(w, h, d, den);
                for (int n = 0; n < 6000; n++)
                {
                    var o = new[] { R(-3, w + 3), R(-3, h + 3), R(-3, d + 3) };
                    var dir = new[] { R(-1, 1), R(-1, 1), R(-1, 1) };
                    if (rng.NextDouble() < 0.12) dir[rng.Next(3)] = 0;
                    if (dir[0] == 0 && dir[1] == 0 && dir[2] == 0) continue;
                    double len = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]);
                    double sc = R(0.3, 3.0) / len;
                    for (int a = 0; a < 3; a++) dir[a] *= sc;
                    double tMax = R(0.5, 60);

                    bool bf = Brute(g, w, h, d, o, dir, tMax, out int bx, out int by, out int bz, out double bt, out int nAxis, out int nSign, out int ties);
                    bool dd = VoxelDda.Raycast(g, w, h, d, o[0], o[1], o[2], dir[0], dir[1], dir[2], tMax, out DdaHit hit);
                    rays++;
                    string ctx = $"grid {w}x{h}x{d} den {den} o={V(o)} dir={V(dir)} tMax={tMax:G6}";
                    if (bf != dd) { Check(false, $"hit/miss mismatch brute={bf} dda={dd} {ctx}"); continue; }
                    if (!bf) { Check(true, ""); continue; }
                    hits++;
                    Check(Math.Abs(hit.t - bt) < 1e-7, $"t {hit.t:G9} vs brute {bt:G9} {ctx}");
                    if (ties == 1)
                    {
                        Check(hit.x == bx && hit.y == by && hit.z == bz, $"cell ({hit.x},{hit.y},{hit.z}) vs brute ({bx},{by},{bz}) {ctx}");
                        int enx = nAxis == 0 ? nSign : 0, eny = nAxis == 1 ? nSign : 0, enz = nAxis == 2 ? nSign : 0;
                        if (bt > 1e-9) Check(hit.nx == enx && hit.ny == eny && hit.nz == enz, $"normal ({hit.nx},{hit.ny},{hit.nz}) vs ({enx},{eny},{enz}) {ctx}");
                        else Check(hit.nx == 0 && hit.ny == 0 && hit.nz == 0, $"inside-solid normal should be zero, got ({hit.nx},{hit.ny},{hit.nz}) {ctx}");
                    }
                    Check((hit.voxel & 0x1FFu) != 0, "hit voxel has no material " + ctx);
                }
            }
        Console.WriteLine($"[1] DDA vs brute force: {rays} rays, {hits} hits");
    }

    static void TestAxisAligned()
    {
        int w = 8, h = 8, d = 8;
        var g = new uint[w * h * d];
        g[5 + 3 * w + 2 * w * h] = 7;
        void Expect(string name, double[] o, double[] dir, double tMax, bool hit, double t, int nx, int ny, int nz)
        {
            bool r = VoxelDda.Raycast(g, w, h, d, o[0], o[1], o[2], dir[0], dir[1], dir[2], tMax, out DdaHit hh);
            Check(r == hit, $"{name}: hit={r} expected {hit}");
            if (r && hit)
            {
                Check(Math.Abs(hh.t - t) < 1e-9, $"{name}: t={hh.t} expected {t}");
                Check(hh.x == 5 && hh.y == 3 && hh.z == 2 && hh.voxel == 7, $"{name}: cell ({hh.x},{hh.y},{hh.z}) voxel {hh.voxel}");
                Check(hh.nx == nx && hh.ny == ny && hh.nz == nz, $"{name}: normal ({hh.nx},{hh.ny},{hh.nz}) expected ({nx},{ny},{nz})");
            }
        }
        Expect("+x", new[] { -2.0, 3.5, 2.5 }, new[] { 1.0, 0, 0 }, 100, true, 7, -1, 0, 0);
        Expect("-x", new[] { 10.0, 3.5, 2.5 }, new[] { -1.0, 0, 0 }, 100, true, 4, 1, 0, 0);
        Expect("+y", new[] { 5.5, -3.0, 2.5 }, new[] { 0, 1.0, 0 }, 100, true, 6, 0, -1, 0);
        Expect("-y", new[] { 5.5, 12.0, 2.5 }, new[] { 0, -1.0, 0 }, 100, true, 8, 0, 1, 0);
        Expect("+z", new[] { 5.5, 3.5, -4.0 }, new[] { 0, 0, 1.0 }, 100, true, 6, 0, 0, -1);
        Expect("-z", new[] { 5.5, 3.5, 9.0 }, new[] { 0, 0, -1.0 }, 100, true, 6, 0, 0, 1);
        Expect("miss by one cell", new[] { -2.0, 4.5, 2.5 }, new[] { 1.0, 0, 0 }, 100, false, 0, 0, 0, 0);
        Expect("tMax short", new[] { -2.0, 3.5, 2.5 }, new[] { 1.0, 0, 0 }, 6.99, false, 0, 0, 0, 0);
        Expect("tMax exact", new[] { -2.0, 3.5, 2.5 }, new[] { 1.0, 0, 0 }, 7.0, true, 7, -1, 0, 0);
        Expect("scaled dir (cells/unit)", new[] { -2.0, 3.5, 2.5 }, new[] { 20.0, 0, 0 }, 1, true, 0.35, -1, 0, 0);
        Expect("pointing away", new[] { -2.0, 3.5, 2.5 }, new[] { -1.0, 0, 0 }, 100, false, 0, 0, 0, 0);
        Expect("diagonal into cell (x face)", new[] { 2.5, 0.75, 2.5 }, new[] { 3.0, 3.0, 0 }, 100, true, 0.8333333333333334, -1, 0, 0);
        Expect("diagonal into cell (y face)", new[] { 2.75, 0.5, 2.5 }, new[] { 3.0, 3.0, 0 }, 100, true, 0.8333333333333334, 0, -1, 0);
        {   // exact corner/edge graze: both faces enter at the same t, either normal is geometrically valid
            bool r = VoxelDda.Raycast(g, w, h, d, 2.5, 0.5, 2.5, 3.0, 3.0, 0, 100, out DdaHit eh);
            Check(r && Math.Abs(eh.t - 0.8333333333333334) < 1e-9 && eh.x == 5 && eh.y == 3 && eh.z == 2 && eh.nz == 0 && (eh.nx == -1) != (eh.ny == -1) && eh.nx + eh.ny == -1,
                $"corner graze: hit={r} t={eh.t} n=({eh.nx},{eh.ny},{eh.nz})");
        }
        // origin inside the solid cell: t=0, zero normal
        bool inside = VoxelDda.Raycast(g, w, h, d, 5.5, 3.5, 2.5, 0.3, 0.2, 0.1, 10, out DdaHit ih);
        Check(inside && ih.t == 0 && ih.nx == 0 && ih.ny == 0 && ih.nz == 0 && ih.x == 5 && ih.y == 3 && ih.z == 2, "origin inside solid cell");
        // zero direction never hits
        Check(!VoxelDda.Raycast(g, w, h, d, -2, 3.5, 2.5, 0, 0, 0, 100, out _), "zero direction");
        // degenerate inputs
        Check(!VoxelDda.Raycast(g, 0, h, d, 0, 0, 0, 1, 0, 0, 10, out _), "zero width");
        Check(!VoxelDda.Raycast((uint[])null, w, h, d, 0, 0, 0, 1, 0, 0, 10, out _), "null array");
        Check(!VoxelDda.Raycast(new uint[10], w, h, d, 0, 0, 0, 1, 0, 0, 10, out _), "array shorter than dims");
        Console.WriteLine("[2] axis-aligned / edge cases done");
    }

    static void TestByteGrid()
    {
        int w = 9, h = 3, d = 11; int mism = 0;
        var gb = new byte[w * h * d]; var gu = new uint[w * h * d];
        for (int i = 0; i < gb.Length; i++) if (rng.NextDouble() < 0.3) { gb[i] = (byte)rng.Next(1, 256); gu[i] = gb[i]; }
        for (int n = 0; n < 5000; n++)
        {
            double ox = R(-2, w + 2), oy = R(-2, h + 2), oz = R(-2, d + 2), dx = R(-1, 1), dy = R(-1, 1), dz = R(-1, 1);
            bool a = VoxelDda.Raycast(gb, w, h, d, ox, oy, oz, dx, dy, dz, 40, out DdaHit ha);
            bool b = VoxelDda.Raycast(gu, w, h, d, ox, oy, oz, dx, dy, dz, 40, out DdaHit hb);
            if (a != b || (a && (ha.x != hb.x || ha.y != hb.y || ha.z != hb.z || ha.t != hb.t || ha.voxel != hb.voxel))) mism++;
        }
        Check(mism == 0, $"byte vs uint grid mismatches: {mism}");
        Console.WriteLine("[3] byte-grid overload agrees with uint-grid overload");
    }

    static void TestVolumeSet()
    {
        var set = new SolidVolumeSet();
        // volume A: corner (10,0,20), vs 0.5, dims 4x3x5, solid at (2,1,3) => world x[11,11.5] y[0.5,1] z[21.5,22]
        var a = new uint[4 * 3 * 5]; a[2 + 1 * 4 + 3 * 4 * 3] = 100u | (5u << 12);
        set.Register("A", a, 4, 3, 5, new Vector3(10, 0, 20), 0.5f);
        bool hit = set.Raycast(new Vector3(11.25f, 0.75f, 15f), new Vector3(0, 0, 1), 50f, out VoxelRayHit h);
        Check(hit && Math.Abs(h.distance - 6.5f) < 1e-4 && h.volumeId == "A" && h.material == 100 && h.cell.x == 2 && h.cell.y == 1 && h.cell.z == 3, $"volume hit: {hit} d={h.distance} id={h.volumeId} mat={h.material} cell={h.cell}");
        Check(hit && h.normal == new Vector3(0, 0, -1), $"volume normal {h.normal}");
        Check(hit && (h.point - new Vector3(11.25f, 0.75f, 21.5f)).magnitude < 1e-4, $"volume point {h.point}");
        Check(!set.Raycast(new Vector3(11.25f, 0.75f, 15f), new Vector3(0, 0, 1), 6.4f, out _), "volume maxDistance respected");
        Check(!set.Raycast(new Vector3(11.75f, 0.75f, 15f), new Vector3(0, 0, 1), 50f, out _), "volume miss by a cell");

        // nearest-of-two regardless of registration order
        var b = new uint[1]; b[0] = 111;
        set.Register("B", b, 1, 1, 1, new Vector3(11, 0.5f, 18), 1f);   // 1m cube at z[18,19] in front of A along the same ray
        hit = set.Raycast(new Vector3(11.25f, 0.75f, 15f), new Vector3(0, 0, 1), 50f, out h);
        Check(hit && h.volumeId == "B" && Math.Abs(h.distance - 3f) < 1e-4, $"nearest volume should be B at 3, got {h.volumeId} {h.distance}");

        // replace by id, swap-remove keeps the rest intact
        var c = new uint[1]; c[0] = 120;
        set.Register("C", c, 1, 1, 1, new Vector3(11, 0.5f, 30), 1f);
        Check(set.Count == 3, "count after 3 registrations: " + set.Count);
        set.Register("B", b, 1, 1, 1, new Vector3(11, 0.5f, 18), 1f);
        Check(set.Count == 3, "replace-by-id must not grow: " + set.Count);
        Check(set.Unregister("A") && set.Count == 2, "unregister A");
        Check(!set.Unregister("A"), "unregister twice");
        hit = set.Raycast(new Vector3(11.25f, 0.75f, 25f), new Vector3(0, 0, 1), 50f, out h);
        Check(hit && h.volumeId == "C" && Math.Abs(h.distance - 5f) < 1e-4, $"C survives swap-remove: {h.volumeId} {h.distance}");
        hit = set.Raycast(new Vector3(11.25f, 0.75f, 15f), new Vector3(0, 0, 1), 50f, out h);
        Check(hit && h.volumeId == "B", "B survives swap-remove");
        set.Clear();
        Check(set.Count == 0 && !set.Raycast(new Vector3(11.25f, 0.75f, 15f), new Vector3(0, 0, 1), 50f, out _), "clear");

        // broadphase: volumes in distinct XZ cells — the ray must walk cells in order,
        // find hits across empty cells, and stop at the first cell beyond the best hit
        var g1 = new uint[1]; g1[0] = 200;
        set.Register("near", g1, 1, 1, 1, new Vector3(100, 0, 5), 1f);    // cell (3,0)
        set.Register("far",  g1, 1, 1, 1, new Vector3(300, 0, 5), 1f);    // cell (9,0)
        hit = set.Raycast(new Vector3(0, 0.5f, 5.5f), new Vector3(1, 0, 0), 500f, out h);
        Check(hit && h.volumeId == "near" && Math.Abs(h.distance - 100f) < 1e-4, $"broadphase nearest across cells: {h.volumeId} {h.distance}");
        hit = set.Raycast(new Vector3(150, 0.5f, 5.5f), new Vector3(1, 0, 0), 500f, out h);
        Check(hit && h.volumeId == "far" && Math.Abs(h.distance - 150f) < 1e-4, $"broadphase far across empty cells: {h.volumeId} {h.distance}");
        Check(!set.Raycast(new Vector3(0, 0.5f, 60f), new Vector3(1, 0, 0), 500f, out _), "broadphase miss across cells");
        Check(!set.Raycast(new Vector3(150, 0.5f, 5.5f), new Vector3(1, 0, 0), 100f, out _), "broadphase maxDistance across cells");
        set.Register("neg", g1, 1, 1, 1, new Vector3(-100, 0, -100), 1f);
        hit = set.Raycast(new Vector3(-99.5f, 0.5f, -140f), new Vector3(0, 0, 1), 500f, out h);
        Check(hit && h.volumeId == "neg" && Math.Abs(h.distance - 40f) < 1e-4, $"broadphase negative coords: {h.volumeId} {h.distance}");
        set.Register("diag", g1, 1, 1, 1, new Vector3(50, 0, 80), 1f);
        var ddiag = new Vector3(1, 0, 1); ddiag.Normalize();
        hit = set.Raycast(new Vector3(0, 0.5f, 30.5f), ddiag, 500f, out h);
        Check(hit && h.volumeId == "diag" && Math.Abs(h.distance - 70.71f) < 0.05f, $"broadphase diagonal: {h.volumeId} {h.distance}");
        // vertical ray through a column cell that holds no volume → miss; through one that does → hit
        Check(!set.Raycast(new Vector3(200f, 0.5f, 200f), new Vector3(0, 1, 0), 500f, out _), "broadphase vertical miss");
        hit = set.Raycast(new Vector3(50.5f, -5f, 80.5f), new Vector3(0, 1, 0), 500f, out h);
        Check(hit && h.volumeId == "diag" && Math.Abs(h.distance - 5f) < 1e-4, $"broadphase vertical hit: {h.volumeId} {h.distance}");
        set.Clear();

        // invalid registrations are ignored
        set.Register(null, new uint[1], 1, 1, 1, Vector3.zero, 1f);
        set.Register("x", null, 1, 1, 1, Vector3.zero, 1f);
        set.Register("x", new uint[1], 2, 2, 2, Vector3.zero, 1f);
        set.Register("x", new uint[8], 2, 2, 2, Vector3.zero, 0f);
        Check(set.Count == 0, "invalid registrations ignored: " + set.Count);
        Console.WriteLine("[4] SolidVolumeSet registry / placement checks done");
    }

    static void TestFallingCells()
    {
        const int W = 24, H = 20, D = 24;
        var falling = new Dictionary<int, ShardPhysics.FallCell>();
        var fq = new List<int>();
        var rubble = new HashSet<int>();
        var slq = new List<int>();
        var dirty = new List<int>();
        var esc = new List<ShardPhysics.FallEscape>();

        // a floating column of 5 cells over a solid floor rains down and stacks, mass-conserving
        var g = new uint[W * H * D];
        for (int x = 0; x < W; x++) for (int z = 0; z < D; z++) g[x + 0 * W + z * W * H] = 7;   // floor at y=0
        for (int y = 8; y <= 12; y++) g[5 + y * W + 5 * W * H] = 33;
        for (int y = 8; y <= 12; y++) { int ci = 5 + y * W + 5 * W * H; falling[ci] = new ShardPhysics.FallCell { vel = Vector3.zero, acc = Vector3.zero, vox = 33 }; fq.Add(ci); }
        for (int tick = 0; tick < 200 && falling.Count > 0; tick++)
            ShardPhysics.StepFalling(g, W, H, D, falling, fq, rubble, slq, dirty, esc, 1f / 120f, 18f, 1.6f, 0.6f, 100000);
        Check(falling.Count == 0, "column all landed");
        int landed = 0;
        for (int y = 0; y < H; y++) if ((g[5 + y * W + 5 * W * H] & VoxelDda.MaterialMask) == 33) landed++;
        Check(landed == 5, $"column cells conserved: {landed} (stacked 1..5)");
        Check(rubble.Count == 5, $"landed cells joined rubble: {rubble.Count}");
        Check(esc.Count == 0, "no escapes on a straight drop");

        // lateral impulse scatters — a cell thrown +x lands offset from its start column
        falling.Clear(); fq.Clear(); rubble.Clear(); slq.Clear(); esc.Clear();
        int sc = 4 + 8 * W + 4 * W * H;
        g[sc] = 41; falling[sc] = new ShardPhysics.FallCell { vel = new Vector3(6f, 0f, 0f), acc = Vector3.zero, vox = 41 }; fq.Add(sc);
        for (int tick = 0; tick < 200 && falling.Count > 0; tick++)
            ShardPhysics.StepFalling(g, W, H, D, falling, fq, rubble, slq, dirty, esc, 1f / 120f, 18f, 1.6f, 0.6f, 100000);
        Check(falling.Count == 0 && rubble.Count == 1, "thrown cell landed");
        int sx = -1;
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if ((g[x + y * W + 4 * W * H] & VoxelDda.MaterialMask) == 41) sx = x;
        Check(sx > 4, $"thrown cell scattered +x: landed at x={sx} (started 4)");
        Check(esc.Count == 0, "in-bounds throw did not escape");

        // boundary escape — a cell pushed past the x edge leaves the grid via `escaped`
        falling.Clear(); fq.Clear(); rubble.Clear(); slq.Clear(); esc.Clear();
        sc = (W - 1) + 8 * W + 4 * W * H;
        g[sc] = 55; falling[sc] = new ShardPhysics.FallCell { vel = new Vector3(4f, 0f, 0f), acc = Vector3.zero, vox = 55 }; fq.Add(sc);
        for (int tick = 0; tick < 200 && falling.Count > 0; tick++)
            ShardPhysics.StepFalling(g, W, H, D, falling, fq, rubble, slq, dirty, esc, 1f / 120f, 18f, 1.6f, 0.6f, 100000);
        Check(esc.Count == 1 && falling.Count == 0, $"cell escaped the volume bounds: {esc.Count}");
        Check((g[sc] & VoxelDda.MaterialMask) == 0, "escaped cell cleared its grid slot");

        // stale-guard — a falling cell re-blasted to AIR drops out instead of flying a ghost voxel
        falling.Clear(); fq.Clear(); rubble.Clear(); slq.Clear(); esc.Clear();
        sc = 6 + 8 * W + 6 * W * H;
        g[sc] = 66; falling[sc] = new ShardPhysics.FallCell { vel = Vector3.zero, acc = Vector3.zero, vox = 66 }; fq.Add(sc);
        g[sc] = 0u;
        ShardPhysics.StepFalling(g, W, H, D, falling, fq, rubble, slq, dirty, esc, 1f / 120f, 18f, 1.6f, 0.6f, 100000);
        Check(falling.Count == 0, "re-blasted falling cell dropped out");

        // a falling cell blocks the one above it until it moves — the train descends as a unit
        falling.Clear(); fq.Clear(); rubble.Clear(); slq.Clear(); esc.Clear();
        for (int y = 8; y <= 9; y++) { int ci = 8 + y * W + 8 * W * H; g[ci] = 77; falling[ci] = new ShardPhysics.FallCell { vel = Vector3.zero, acc = Vector3.zero, vox = 77 }; fq.Add(ci); }
        ShardPhysics.StepFalling(g, W, H, D, falling, fq, rubble, slq, dirty, esc, 1f / 120f, 18f, 1.6f, 0.6f, 100000);
        int tc = 0;
        for (int y = 0; y < H; y++) if ((g[8 + y * W + 8 * W * H] & VoxelDda.MaterialMask) == 77) tc++;
        Check(tc == 2 && falling.Count == 2, $"train intact after one tick ({tc} cells, {falling.Count} falling)");
    }


    static void TestFlyerTransport()
    {
        // [18] Flying-voxel transport — SolidVolumeSet.SamplePoint + ShardPhysics.StepFlyer:
        //      the world-space phase of a doomed cell between grids. Point queries only,
        //      never a swept ray; solids absorb, open cells hand off into a grid's rain.
        //      (VoxelCollisionWorld.SamplePoint is a MonoBehaviour wrapper around this
        //      same query plus the terrain fallback — the testable layer lives here.)
        Console.WriteLine("[18] flying-voxel transport");
        var set = new SolidVolumeSet();
        var arrA = new uint[8 * 8 * 8];
        var arrB = new uint[8 * 8 * 8];
        for (int i = 0; i < arrA.Length; i++) { arrA[i] = 5u; arrB[i] = 7u; }
        arrA[0] = 0u;                                    // an open pocket on volA's corner cell
        set.Register("volA", arrA, 8, 8, 8, Vector3.zero, 1f);
        set.Register("volB", arrB, 8, 8, 8, new Vector3(20f, 0f, 0f), 1f);
        ShardPhysics.FlyQuery fly = p =>
        {
            if (!set.SamplePoint(p, out string vid, out int idx, out bool sol))
                return new ShardPhysics.FlySample { kind = ShardPhysics.FlySampleKind.Free };
            return new ShardPhysics.FlySample
            {
                kind = sol ? ShardPhysics.FlySampleKind.Solid : ShardPhysics.FlySampleKind.Open,
                volumeId = vid,
                cellIdx = idx,
            };
        };

        Check(set.SamplePoint(new Vector3(2.5f, 0.5f, 0.5f), out string v1, out int i1, out bool s1)
            && s1 && v1 == "volA" && i1 == 2 + 0 * 8 + 0 * 64,
            $"SamplePoint solid cell → solid ({v1},{i1})");
        Check(set.SamplePoint(new Vector3(0.5f, 0.5f, 0.5f), out string v2, out int i2, out bool s2)
            && !s2 && v2 == "volA" && i2 == 0,
            $"SamplePoint air pocket → open ({v2},{i2})");
        Check(!set.SamplePoint(new Vector3(-30f, 60f, 0f), out _, out _, out _),
            "SamplePoint empty space → not contained");
        Check(set.SamplePoint(new Vector3(21.5f, 0.5f, 0.5f), out string v3, out _, out _)
            && v3 == "volB",
            "SamplePoint second volume resolves via broadphase");

        // free-space flight integrates gravity + moves
        Vector3 pos = new Vector3(-5f, 10f, 0.5f), vel = new Vector3(4f, 0f, 0f), acc = Vector3.zero;
        ShardPhysics.StepFlyer(ref pos, ref vel, ref acc, 0.1f, 9f, 0f, 0.2f, 12, fly, out _);
        Check(pos.x > -5f && vel.y < 0f, $"free flight moves + gravity integrates (x={pos.x:F2}, vy={vel.y:F2})");

        // entry: a +x flyer steps into volA's open pocket → EnteredOpen payload
        pos = new Vector3(-0.05f, 0.5f, 0.5f); vel = new Vector3(2f, 0f, 0f); acc = Vector3.zero;
        var oc = ShardPhysics.StepFlyer(ref pos, ref vel, ref acc, 0.15f, 0f, 0f, 0.2f, 12, fly, out var ent);
        Check(oc == ShardPhysics.FlyOutcome.EnteredOpen && ent.volumeId == "volA" && ent.cellIdx == 0,
            $"enter open cell → EnteredOpen ({oc},{ent.volumeId},{ent.cellIdx})");

        // solid absorb: 200 m/s at a wall — momentum dies, NEVER tunnels
        pos = new Vector3(1.5f, 0.5f, 0.5f); vel = new Vector3(200f, 0f, 0f); acc = Vector3.zero;
        oc = ShardPhysics.StepFlyer(ref pos, ref vel, ref acc, 1f / 60f, 0f, 0f, 0.2f, 12, fly, out _);
        Check(oc == ShardPhysics.FlyOutcome.Flying && pos.x < 3.5f && vel.x == 0f,
            $"200m/s into solid absorbs without tunneling (x={pos.x:F2}, vx={vel.x})");

        // end-to-end: the entered cell resumes as a falling cell and lands as its own material
        var falling = new Dictionary<int, ShardPhysics.FallCell>();
        var fq = new List<int>(); var rubble = new HashSet<int>(); var slq = new List<int>();
        var dirty = new List<int>(); var esc = new List<ShardPhysics.FallEscape>();
        arrA[ent.cellIdx] = 9u;   // TryEnterVolume writes the voxel immediately
        falling[ent.cellIdx] = new ShardPhysics.FallCell { vel = new Vector3(2f, 0f, 0f), acc = Vector3.zero, vox = 9u };
        fq.Add(ent.cellIdx);
        for (int t = 0; t < 240 && falling.Count > 0; t++)
            ShardPhysics.StepFalling(arrA, 8, 8, 8, falling, fq, rubble, slq, dirty, esc, 1f / 120f, 18f, 1.6f, 0.6f, 100000);
        Check(falling.Count == 0 && rubble.Count == 1 && (arrA[0] & VoxelDda.MaterialMask) == 9u,
            $"handoff cell lands in target grid as rubble with own material (rubble={rubble.Count}, mat={arrA[0] & VoxelDda.MaterialMask})");

        // lateral entry through a neighbor's window: flyer crosses open space, enters volB
        pos = new Vector3(19.6f, 0.5f, 0.5f); vel = new Vector3(1.5f, 0f, 0f); acc = Vector3.zero;
        arrB[0] = 0u;
        oc = ShardPhysics.StepFlyer(ref pos, ref vel, ref acc, 0.4f, 0f, 0f, 0.2f, 12, fly, out var ent2);
        Check(oc == ShardPhysics.FlyOutcome.EnteredOpen && ent2.volumeId == "volB" && ent2.cellIdx == 0,
            $"cross-volume entry into a neighbor ({oc},{ent2.volumeId},{ent2.cellIdx})");
    }

    static void TestYaw()
    {
        // analytic: dims (4,1,2), vs 1, yaw 90 about the volume center (2,0.5,1); solid cell (3,0,0) lands on world x[1,2] z[-1,0]
        var g = new uint[4 * 1 * 2]; g[3 + 0 * 4 + 0 * 4] = 100;
        var set = new SolidVolumeSet();
        set.Register("Y", g, 4, 1, 2, Vector3.zero, 1f, 90f);
        bool hit = set.Raycast(new Vector3(1.5f, 0.5f, -5f), new Vector3(0, 0, 1), 50f, out VoxelRayHit h);
        Check(hit && Math.Abs(h.distance - 4f) < 1e-4 && h.cell.x == 3 && h.cell.y == 0 && h.cell.z == 0, $"yaw90 hit {hit} d={h.distance} cell={h.cell}");
        Check(hit && (h.normal - new Vector3(0, 0, -1)).magnitude < 1e-4, $"yaw90 normal {h.normal}");
        Check(!set.Raycast(new Vector3(2.5f, 0.5f, -5f), new Vector3(0, 0, 1), 50f, out _), "yaw90 miss beside the rotated cell");

        // randomized: oriented-box reference built from the FORWARD rotation (the implementation uses the inverse)
        int mism = 0, hits = 0, rays = 0;
        for (int trial = 0; trial < 12; trial++)
        {
            int w = rng.Next(2, 9), hh = rng.Next(1, 5), d = rng.Next(2, 9);
            var grid = RandomGrid(w, hh, d, 0.25);
            float vs = (float)R(0.2, 1.5), yaw = (float)R(-180, 180);
            var corner = new Vector3((float)R(-5, 5), (float)R(0, 2), (float)R(-5, 5));
            var s = new SolidVolumeSet();
            s.Register("V", grid, w, hh, d, corner, vs, yaw);
            double rad = yaw * Math.PI / 180.0, c = Math.Cos(rad), sn = Math.Sin(rad);
            double[] U = { c, 0, -sn }, Vv = { 0, 1, 0 }, Wv = { sn, 0, c };   // rotated local X,Y,Z axes in world space
            double[] ctr = { corner.x + w * vs * 0.5, corner.y + hh * vs * 0.5, corner.z + d * vs * 0.5 };
            for (int n = 0; n < 800; n++)
            {
                var o = new[] { R(-12, 12), R(-4, 8), R(-12, 12) };
                var dr = new[] { R(-1, 1), R(-1, 1), R(-1, 1) };
                double len = Math.Sqrt(dr[0] * dr[0] + dr[1] * dr[1] + dr[2] * dr[2]);
                for (int a = 0; a < 3; a++) dr[a] /= len;
                double tMax = R(1, 40);
                // brute force over solid cells: slab test in the cell's own (rotated) frame using forward axes
                double bt = double.PositiveInfinity; int bx = -1, by = -1, bz = -1, ties = 0;
                for (int z = 0; z < d; z++) for (int y = 0; y < hh; y++) for (int x = 0; x < w; x++)
                {
                    if ((grid[x + y * w + z * w * hh] & 0x1FFu) == 0) continue;
                    double[] cc = { (x + 0.5) * vs - w * vs * 0.5, (y + 0.5) * vs - hh * vs * 0.5, (z + 0.5) * vs - d * vs * 0.5 }; // cell center rel. volume center (local frame)
                    double[] cw = { ctr[0] + cc[0] * U[0] + cc[1] * Vv[0] + cc[2] * Wv[0], ctr[1] + cc[0] * U[1] + cc[1] * Vv[1] + cc[2] * Wv[1], ctr[2] + cc[0] * U[2] + cc[1] * Vv[2] + cc[2] * Wv[2] };
                    double t0 = 0, t1 = tMax; bool ok = true;
                    double[][] axes = { U, Vv, Wv };
                    for (int a = 0; a < 3 && ok; a++)
                    {
                        double oc = (o[0] - cw[0]) * axes[a][0] + (o[1] - cw[1]) * axes[a][1] + (o[2] - cw[2]) * axes[a][2];
                        double dd = dr[0] * axes[a][0] + dr[1] * axes[a][1] + dr[2] * axes[a][2];
                        double lo = -vs * 0.5, hi = vs * 0.5;
                        if (Math.Abs(dd) < 1e-14) { if (oc < lo || oc > hi) ok = false; continue; }
                        double ta = (lo - oc) / dd, tb = (hi - oc) / dd;
                        if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                        if (ta > t0) t0 = ta; if (tb < t1) t1 = tb; if (t0 > t1) ok = false;
                    }
                    if (!ok) continue;
                    if (t0 < bt - 1e-7) { bt = t0; bx = x; by = y; bz = z; ties = 1; } else if (Math.Abs(t0 - bt) <= 1e-7) ties++;
                }
                bool bf = bx >= 0;
                bool sh = s.Raycast(new Vector3((float)o[0], (float)o[1], (float)o[2]), new Vector3((float)dr[0], (float)dr[1], (float)dr[2]), (float)tMax, out VoxelRayHit rh);
                rays++;
                if (bf != sh) { // float vs double rounding at grazing contact: tolerate only when the brute-force hit is within 1e-3 of tMax or t=0 edge
                    if (!(bf && Math.Abs(bt - tMax) < 1e-3)) { mism++; if (mism <= 5) Console.WriteLine($"  yaw mismatch brute={bf} sut={sh} yaw={yaw} bt={bt}"); }
                    continue; }
                if (!bf) continue;
                hits++;
                if (Math.Abs(rh.distance - bt) > 2e-3 || (ties == 1 && (rh.cell.x != bx || rh.cell.y != by || rh.cell.z != bz))) { mism++; if (mism <= 5) Console.WriteLine($"  yaw hit differs: sut d={rh.distance} cell={rh.cell} brute d={bt} cell=({bx},{by},{bz}) yaw={yaw}"); }
            }
        }
        Check(mism == 0, $"yawed-volume mismatches vs oriented-box brute force: {mism}");
        Console.WriteLine($"[5] yawed volumes: {rays} rays, {hits} hits, {mism} mismatches");
    }

    static uint[] LoadRange(string path, out int w, out int h, out int d)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var dims = doc.RootElement.GetProperty("dims");
        w = dims[0].GetInt32(); h = dims[1].GetInt32(); d = dims[2].GetInt32();
        var g = new uint[w * h * d];
        foreach (var v in doc.RootElement.GetProperty("voxels").EnumerateArray())
            g[v[0].GetInt32() + v[1].GetInt32() * w + v[2].GetInt32() * w * h] = (uint)v[3].GetInt32();
        return g;
    }

    static void TestRange(string jsonPath)
    {
        if (jsonPath == null || !File.Exists(jsonPath)) { Console.WriteLine("[6] range asset not provided - skipped"); return; }
        var g = LoadRange(jsonPath, out int w, out int h, out int d);
        float vs = 0.05f; var corner = new Vector3(4f, 0f, -7f);
        var set = new SolidVolumeSet();
        set.Register("range", g, w, h, d, corner, vs);
        Vector3 W(double x, double y, double z) => corner + new Vector3((float)x, (float)y, (float)z) * vs;
        string Describe(VoxelRayHit r) => $"vol={r.volumeId} mat={r.material} cell=({r.cell.x},{r.cell.y},{r.cell.z}) dist={r.distance:F3}m normal={r.normal}";

        Vector3 muzzle = W(96.0, 10.0, 14.0);
        void Shot(string name, Vector3 aim, Func<VoxelRayHit, bool> expect)
        {
            Vector3 dir = (aim - muzzle).normalized;
            bool hit = set.Raycast(muzzle, dir, 80f, out VoxelRayHit r);
            Console.WriteLine($"    {name,-34} -> {(hit ? Describe(r) : "no hit")}");
            Check(hit && expect(r), $"range shot '{name}' unexpected: {(hit ? Describe(r) : "no hit")}");
        }
        Shot("bullseye centre (firing_target)", W(96.0, 8.0, 169.5), r => r.material == 100 && r.cell.z == 169 && r.normal == new Vector3(0, 0, -1) && r.cell.x >= 94 && r.cell.x <= 97 && r.cell.y >= 6 && r.cell.y <= 9);
        Shot("paper, left of bullseye", W(88.5, 11.0, 169.5), r => r.material == 127 && r.cell.z == 169);
        Shot("paper, right/low", W(104.0, 5.0, 169.5), r => r.material == 127 && r.cell.z == 169);
        Shot("over the top of the target", W(96.0, 24.0, 169.5), r => r.cell.z > 169);
        Shot("into the floor in front", W(96.0, 2.5, 60.0), r => r.cell.y <= 2);
        Check(!set.Raycast(muzzle, Vector3.up, 80f, out _), "straight up into the sky hits nothing");

        // timing: 200k shots through the real range grid
        var sw = Stopwatch.StartNew(); int hitsN = 0;
        for (int i = 0; i < 200000; i++)
        {
            double j1 = R(-3, 3), j2 = R(-3, 3);
            Vector3 aim = W(96.0 + j1, 8.0 + j2, 169.5);
            if (set.Raycast(muzzle, (aim - muzzle).normalized, 80f, out _)) hitsN++;
        }
        sw.Stop();
        Console.WriteLine($"    200000 shots through 192x120x192 range: {sw.Elapsed.TotalMilliseconds:F0} ms total, {sw.Elapsed.TotalMilliseconds * 1000.0 / 200000:F2} us/shot, {hitsN} hits");
        Console.WriteLine("[6] range asset shots done");

        TestGlassPuncture(w, h, d, corner, vs);
    }

    /// <summary>
    /// P0 glass puncture (GLASS_SHATTER_MECHANIC.md): fire the same ray into the lane-3
    /// ballistics mold, apply VoxelDamageMath.Puncture on every glass hit, and keep
    /// drilling until the ray exits the mold's back face and strikes the lane-3 target
    /// downrange. Verifies: first hit is glass at the front face (z=109), each puncture
    /// removes >0 cells, the hole deepens monotonically, and the Painted Metal retainer
    /// at the cube's base (y4-5) is never touched by the level aim line.
    /// </summary>
    static void TestGlassPuncture(int w, int h, int d, Vector3 corner, float vs)
    {
        // Fresh copy of the asset — this test mutates it.
        var g2 = LoadRange(rangePath, out int w2, out int h2, out int d2);
        Check(w2 == w && h2 == h && d2 == d, "puncture grid dims match");
        var set2 = new SolidVolumeSet();
        set2.Register("range", g2, w, h, d, corner, vs);

        // Level shot down lane 3 into the mold face: glass_target centroid (155.5,7.5,112.5), front face z=109.
        Vector3 muzzle = corner + new Vector3(155.5f, 7.5f, 20f) * vs;
        Vector3 aim = corner + new Vector3(155.5f, 7.5f, 112.5f) * vs;
        Vector3 dir = (aim - muzzle).normalized;
        var dirty = new List<int>();

        int firstZ = -1, lastGlassZ = -1, shots = 0, totalRemoved = 0;
        bool punchedThrough = false, monotonic = true;
        for (; shots < 40; shots++)
        {
            Check(set2.Raycast(muzzle, dir, 80f, out VoxelRayHit r), $"puncture shot {shots}: ray hit nothing");
            if (r.material == 111) { Check(false, $"puncture shot {shots}: hit metal retainer at {Describe2(r)} — it must be below the aim line"); break; }
            if (!VoxelDamageMath.IsGlassMaterial(r.material))
            {
                // Clear through the mold — first non-glass hit must be PAST the cube's
                // back face (z > 116): lane-3 target/backstop downrange.
                punchedThrough = r.cell.z > 116;
                Check(punchedThrough, $"puncture shot {shots}: ray stopped inside the mold at {Describe2(r)}");
                break;
            }
            if (firstZ < 0) Check(r.cell.z == 109 && r.material == 112, $"first glass hit at front face z=109 mat 112, got {Describe2(r)}");
            if (r.cell.z < lastGlassZ) monotonic = false;
            lastGlassZ = r.cell.z;
            if (firstZ < 0) firstZ = r.cell.z;
            int removed = VoxelDamageMath.Puncture(g2, w, h, d, r.cell.x, r.cell.y, r.cell.z,
                dir.x, dir.y, dir.z, 0xC0FFEE00u + (uint)shots, 0.8f, 134, dirty, out int cracked);
            Check(removed > 0, $"puncture shot {shots}: removed 0 cells at {Describe2(r)}");
            totalRemoved += removed;
            dirty.Clear();
            set2.Register("range", g2, w, h, d, corner, vs);   // COW path re-registers by id; same array here
        }
        Check(punchedThrough, $"ray punched through the mold back face after {shots} shots, {totalRemoved} cells removed");
        Check(monotonic, "hole deepened monotonically along the shot line");

        // The metal retainer at the cube's base must be intact — it sits below the
        // y7.5 aim line (cells x155-156, y4-5, z112-113) and is indestructible anyway.
        for (int x = 155; x <= 156; x++)
            for (int y = 4; y <= 5; y++)
                for (int z = 112; z <= 113; z++)
                {
                    int idx = x + y * w + z * w * h;
                    Check((g2[idx] & VoxelDamageMath.MaterialMask) == 111,
                        $"metal retainer cell ({x},{y},{z}) intact — got mat {g2[idx] & VoxelDamageMath.MaterialMask}");
                }
        Console.WriteLine($"    mold drilled: {shots} shots, {totalRemoved} cells removed, clean through-hole");
        Console.WriteLine("[7] glass puncture drill done");
    }

    // [8] Demolition: spherical blast — material resistance + falloff + sphere bounds.
    // Synthetic 20^3 shell: brick (106) shell, glass band (112) at z=9-10, metal core (111).
    static void TestBlast()
    {
        const int w = 20, h = 20, d = 20;
        var g = new uint[w * h * d];
        for (int z = 0; z < d; z++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint mat = 106;                                  // brick everywhere
                    if (x >= 4 && x <= 15 && y >= 6 && y <= 13 && z >= 9 && z <= 10)
                        mat = 112;                                   // glass band across the middle
                    if (x >= 8 && x <= 11 && y >= 8 && y <= 11 && z >= 8 && z <= 11)
                        mat = 111;                                   // metal core
                    g[x + y * w + z * w * h] = mat;
                }
        var dirty = new List<int>(); var ridx = new List<int>(); var rmats = new List<uint>();
        float cx = 10f, cy = 10f, cz = 10f, radius = 8f, power = 1f;
        int removed = VoxelDamageMath.Blast(g, w, h, d, cx, cy, cz, radius, power,
            0xB1A57u, 134, dirty, ridx, rmats, out int cracked, occlusion: false);
        Check(removed > 0, "blast removed nothing");
        Check(ridx.Count == removed && rmats.Count == removed, "removed index/material lists aligned");
        // every removed cell inside the sphere
        foreach (int idx in ridx)
        {
            int z = idx / (w * h), rem = idx - z * w * h, y = rem / w, x = rem - y * w;
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            Check(dist <= radius + 1e-4f, $"removed cell ({x},{y},{z}) outside blast radius ({dist:F2} > {radius})");
        }
        // rates per material inside the sphere — glass must die at a higher rate than brick
        int glassHit = 0, metalHit = 0, brickHit = 0;
        foreach (var m in rmats)
        {   if (m == 112) glassHit++; else if (m == 111) metalHit++; else if (m == 106) brickHit++; }
        int glassIn = 0, metalIn = 0, brickIn = 0;
        for (int z = 0; z < d; z++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
            if (dx * dx + dy * dy + dz * dz > radius * radius) continue;
            uint m = g[x + y * w + z * w * h] & 0x1FFu;   // post-blast value
            // count what WAS there: removed cells are AIR now — count via rmats + survivors
            if (m == 112 || m == 134) glassIn++;
            else if (m == 111) metalIn++;
            else if ((m & 0x1FFu) == 106) brickIn++;
        }
        glassIn += glassHit; metalIn += metalHit; brickIn += brickHit;
        float glassRate = glassIn > 0 ? (float)glassHit / glassIn : 0f;
        float brickRate = brickIn > 0 ? (float)brickHit / brickIn : 0f;
        Check(glassHit > 0, "blast removed no glass");
        float metalRate = metalIn > 0 ? (float)metalHit / metalIn : 0f;
        Check(metalRate < 0.15f, $"metal core lost {metalRate:P0} of its cells at point-blank max power — mat 111 should be near-indestructible");
        Check(brickHit > 0, "blast removed no brick");
        Check(glassRate > brickRate, $"glass removal rate {glassRate:F2} should beat brick {brickRate:F2} (resistance 0.05 vs 0.5)");
        Check(cracked >= 0, "cracked count sane");
        // a ray through the crater should now see AIR at the former glass band
        Console.WriteLine($"    blast: -{removed} cells ({glassHit} glass / {brickHit} brick / {metalHit} metal), {cracked} cracked");
        Console.WriteLine("[8] blast test done");
    }

    // [9] Connectivity: sever a pillar's base → the cap must detach; a brick
    // resting on glass falls; a glass column chained to the slab survives.
    static void TestConnectivity()
    {
        const int w = 20, h = 20, d = 20;
        var g = new uint[w * h * d];
        int I(int x, int y, int z) => x + y * w + z * w * h;
        for (int z = 0; z < d; z++) for (int x = 0; x < w; x++) g[I(x, 0, z)] = 106;   // ground slab = anchor
        for (int y = 1; y <= 8; y++) g[I(5, y, 5)] = 106;                            // pillar
        for (int x = 12; x <= 14; x++) for (int y = 10; y <= 12; y++) for (int z = 12; z <= 14; z++)
            g[I(x, y, z)] = 106;                                                     // floating cube — detached by design
        for (int y = 1; y <= 4; y++) g[I(1, y, 10)] = 112;                           // glass column chained to slab
        g[I(1, 5, 10)] = 106; g[I(1, 5, 11)] = 106;                                  // bricks resting on glass — must fall
        g[I(5, 1, 5)] = 0; g[I(5, 2, 5)] = 0;                                        // sever the pillar (a blast crater by hand)
        var det = new List<int>(); var dm = new List<uint>(); var ends = new List<int>();
        int detached = VoxelDamageMath.FindDetached(g, w, h, d, det, dm, ends);
        var set = new HashSet<int>(det);
        Check(detached == 35, $"expected 35 detached cells (6 pillar cap + 2 bricks + 27 cube), got {detached}");
        Check(ends.Count == 3, $"expected 3 islands, got {ends.Count}");
        Check(set.Contains(I(5, 8, 5)), "pillar cap should detach after sever");
        Check(set.Contains(I(13, 11, 13)), "floating cube should be detached");
        Check(set.Contains(I(1, 5, 10)) && set.Contains(I(1, 5, 11)), "bricks on glass should fall — glass can't anchor structure");
        Check(!set.Contains(I(1, 4, 10)), "glass column chained to slab should survive");
        Check(!set.Contains(I(10, 0, 10)), "ground slab is the anchor");
        Console.WriteLine($"    connectivity: -{detached} cells in {ends.Count} islands");
        Console.WriteLine("[9] connectivity test done");
    }

    // [10] Occlusion: same target patch, same seeds — only the wall between it
    // and the bomb varies. A brick cell roughly halves the blast; 3-thick brick
    // nearly shields; glass barely shields. Isolates occlusion exactly.
    static void TestBlastOcclusion()
    {
        const int w = 20, h = 20, d = 20;
        int I(int x, int y, int z) => x + y * w + z * w * h;
        int EastRemoved(uint wallMat, int wallX0, int wallX1)
        {
            var g = new uint[w * h * d];
            if (wallX0 <= wallX1)
                for (int x = wallX0; x <= wallX1; x++)
                    for (int y = 0; y < h; y++) for (int z = 0; z < d; z++) g[I(x, y, z)] = wallMat;
            for (int y = 8; y <= 12; y++) for (int z = 8; z <= 12; z++) g[I(13, y, z)] = 100;   // patch always at x=13, dist ~3
            var dirty = new List<int>(); var ri = new List<int>(); var rm = new List<uint>();
            VoxelDamageMath.Blast(g, w, h, d, 10f, 10f, 10f, 9f, 1f,
                0x0CC1u, 134, dirty, ri, rm, out _, occlusion: true);
            int east = 0;
            foreach (int idx in ri)
            {
                int z = idx / (w * h), rem = idx - z * w * h, y = rem / w, x = rem - y * w;
                if (x == 13) east++;
            }
            return east;
        }
        int open = EastRemoved(0u, 1, 0);            // no wall — control count
        int thin = EastRemoved(100u, 12, 12);        // 1 brick cell → transmit 0.5
        int thick = EastRemoved(100u, 10, 12);       // 3 brick cells → transmit 0.125
        int wood = EastRemoved(106u, 12, 12);        // 1 wood cell → transmit 0.75 (weak shield)
        int glass = EastRemoved(112u, 12, 12);       // 1 glass cell → transmit 0.95
        Check(open > 0, "open patch should take damage");
        Check(thin < open, $"thin brick should attenuate: open {open} vs shielded {thin}");
        Check(thick < thin, $"thicker should shield more: brick1 {thin} vs brick3 {thick}");
        Check(thick <= Math.Max(2, open / 3), $"thick brick should mostly shield: open {open} vs shielded {thick}");
        Check(wood > thin, $"wood should shield less than brick: wood {wood} vs brick {thin}");
        Check(glass >= open - 2, $"glass should barely shield: open {open} vs glass {glass}");
        Console.WriteLine($"    occlusion: open {open} · brick1 {thin} · brick3 {thick} · wood {wood} · glass {glass} (patch cells removed)");
        Console.WriteLine("[10] occlusion test done");
    }

    // [11] Shock-front sim: the BlastPlan must be a complete delta — replaying
    // its writes reproduces the simulated end state exactly; multi-pass with
    // occlusion frees structure between fronts → strictly more removed than one.
    static void TestSimulate()
    {
        const int w = 20, h = 20, d = 20;
        var g0 = new uint[w * h * d];
        for (int i = 0; i < g0.Length; i++) g0[i] = 106;                       // solid brick cube
        var scratch = (uint[])g0.Clone();
        var plan = VoxelDamageMath.SimulateBlast(scratch, w, h, d, 10f, 10f, 10f,
            7f, 1f, 0x5151u, 4, 134, occlusion: true, dropUnsupported: false);
        Check(plan.totalRemoved > 0, "sim removed nothing");
        Check(plan.passEnds.Count == 4, "4 fronts recorded");
        Check(plan.detachedStart == plan.writeIdx.Count, "no islands without dropUnsupported");
        // plan replay fidelity: apply writes to a pristine copy → identical end state
        var replay = (uint[])g0.Clone();
        for (int i = 0; i < plan.writeIdx.Count; i++) replay[plan.writeIdx[i]] = plan.writeVal[i];
        bool same = true;
        for (int i = 0; i < replay.Length; i++) if (replay[i] != scratch[i]) { same = false; break; }
        Check(same, "plan playback must reproduce the simulated end state exactly");
        // compounding: 4 expanding fronts remove strictly more than a single full blast
        var g2 = (uint[])g0.Clone();
        var dirty = new List<int>(); var ri = new List<int>(); var rm = new List<uint>();
        int single = VoxelDamageMath.Blast(g2, w, h, d, 10f, 10f, 10f, 7f, 1f,
            0x5151u + 3, 134, dirty, ri, rm, out _, occlusion: true);
        Check(plan.totalRemoved > single, $"4-front sim {plan.totalRemoved} should exceed single blast {single}");
        // determinism: same seed → identical plan
        var s3 = (uint[])g0.Clone();
        var plan2 = VoxelDamageMath.SimulateBlast(s3, w, h, d, 10f, 10f, 10f,
            7f, 1f, 0x5151u, 4, 134, occlusion: true, dropUnsupported: false);
        Check(plan2.totalRemoved == plan.totalRemoved && plan2.writeIdx.Count == plan.writeIdx.Count,
            "same seed must reproduce the identical plan");
        // wavefront ordering: cells near center die on early fronts, fringe on late
        int firstEnd = plan.passEnds[0];
        for (int i = 0; i < firstEnd; i++)
        {
            int idx = plan.writeIdx[i];
            int z = idx / (w * h), rem = idx - z * w * h, y = rem / w, x = rem - y * w;
            float dist = (float)Math.Sqrt((x + 0.5f - 10f) * (x + 0.5f - 10f) + (y + 0.5f - 10f) * (y + 0.5f - 10f) + (z + 0.5f - 10f) * (z + 0.5f - 10f));
            Check(dist <= 1.9f, $"first-front cell at dist {dist:F1} exceeds front-1 radius 1.75");
        }
        Console.WriteLine($"    sim: {plan.totalRemoved} doomed vs {single} single-blast · {plan.passEnds.Count} fronts · plan replay exact");
        Console.WriteLine("[11] shock-front sim test done");
    }

    // [12] Path-length march: attenuation is by DISTANCE travelled through
    // material, not cells entered — a 45° ray over the same span must match an
    // axis-aligned ray (the old cell-count march made craters diamond-shaped).
    // Axis crossings keep their old cost exactly (3 brick cells = 0.5³).
    static void TestMarchPathLength()
    {
        const int w = 81, h = 81, d = 81;
        var gb = new uint[w * h * d];
        for (int i = 0; i < gb.Length; i++) gb[i] = 100u;                       // solid brick
        float tAxis3 = VoxelDamageMath.MarchTransmit(gb, w, h, d, 40.5f, 40.5f, 40.5f, 44.5f, 40.5f, 40.5f);
        Check(Math.Abs(tAxis3 - 0.125f) < 0.001f,
            $"3 brick cells axis-aligned must transmit exactly 0.125, got {tAxis3:F4}");
        var gw = new uint[w * h * d];
        for (int i = 0; i < gw.Length; i++) gw[i] = 106u;                       // solid wood
        float tA = VoxelDamageMath.MarchTransmit(gw, w, h, d, 40.5f, 40.5f, 40.5f, 70.5f, 40.5f, 40.5f);
        float tD = VoxelDamageMath.MarchTransmit(gw, w, h, d, 40.5f, 40.5f, 40.5f, 61.5f, 40.5f, 61.5f);
        double lnRatio = Math.Log(tD) / Math.Log(tA);
        Check(Math.Abs(lnRatio - 1.0) < 0.15,
            $"45° ray must match axis ray (isotropic): lnT ratio {lnRatio:F2}");
        Console.WriteLine($"    path-length: 3-brick axis {tAxis3:F4} · wood r≈30 axis {tA:F5} vs 45° {tD:F5} (ln ratio {lnRatio:F2})");
        Console.WriteLine("[12] path-length march test done");
    }

    // [13] Debris vs voxels (ShardPhysics.Step): a swept sphere never tunnels, bounces off floors AND walls
    // (yawed volumes too — floor and wall are the same code, only the normal differs) and reports embedding.
    static ShardRaycast Caster(SolidVolumeSet set) => (Vector3 o, Vector3 dd, float m, out VoxelRayHit hh) => set.Raycast(o, dd, m, out hh);

    static void TestShardCollision()
    {
        const float vs = 0.05f, r = 0.01875f, e = 0.35f, fr = 0.6f, dt60 = 1f / 60f;
        const int w = 40, h = 20, d = 40;
        uint[] Slab(int yCells) { var g = new uint[w * h * d]; for (int z = 0; z < d; z++) for (int y = 0; y < yCells; y++) for (int x = 0; x < w; x++) g[x + y * w + z * w * h] = 100; return g; }
        uint[] Wall(int xFrom) { var g = new uint[w * h * d]; for (int z = 0; z < d; z++) for (int y = 0; y < h; y++) for (int x = xFrom; x < w; x++) g[x + y * w + z * w * h] = 100; return g; }

        // A. a tossed shard settles on a 4-cell floor (top face at 0.2 m): never below it, never embedded, comes to rest
        var set = new SolidVolumeSet(); set.Register("floor", Slab(4), w, h, d, Vector3.zero, vs);
        var cast = Caster(set);
        Vector3 pos = new Vector3(1f, 0.9f, 1f), vel = new Vector3(0.6f, 0f, 0.2f);
        float minY = float.MaxValue; int rest = -1; VoxelRayHit hit = default; bool embedded = false;
        for (int f = 0; f < 600 && rest < 0; f++)
        {
            var res = ShardPhysics.Step(ref pos, ref vel, r, dt60, 9f, e, fr, cast, out hit);
            minY = Mathf.Min(minY, pos.y);
            if (res == ShardStep.Embedded) embedded = true;
            if (res == ShardStep.Contact && hit.normal.y > 0.7f && vel.sqrMagnitude < 0.16f) rest = f;
        }
        Check(!embedded, "tossed shard must never be embedded");
        Check(rest >= 0, "tossed shard must come to rest within 10 s");
        Check(minY >= 0.2f + r - 1e-4f, $"shard sank into the floor: minY {minY:F4} < {0.2f + r:F4}");
        Check(Mathf.Abs(pos.y - (0.2f + r + ShardPhysics.Skin)) < 1e-3f, $"rest height {pos.y:F4} should sit one radius above the floor");
        Check(hit.volumeId == "floor", "contact must report the volume it landed on");

        // B. 30 m/s at dt 1/30 (a 1 m step) onto a ONE-voxel floor (top face 0.05 m): stops on it, no tunnelling
        var setT = new SolidVolumeSet(); setT.Register("thin", Slab(1), w, h, d, Vector3.zero, vs);
        Vector3 pT = new Vector3(1f, 0.9f, 1f), vT = new Vector3(0f, -30f, 0f);
        var rT = ShardPhysics.Step(ref pT, ref vT, r, 1f / 30f, 0f, e, fr, Caster(setT), out _);
        Check(rT == ShardStep.Contact, "a 30 m/s shard must stop on a 1-voxel floor, not tunnel");
        Check(Mathf.Abs(pT.y - (0.05f + r + ShardPhysics.Skin)) < 1e-3f, $"fast-shard rest height {pT.y:F4}");
        Check(vT.y > 10f && vT.y < 11f, $"vertical bounce {vT.y:F2} should be 0.35 x 30");

        // C. wall: a shard flying +x at 6 m/s stops a radius off the face (x = 1.5 m) and reverses vx x restitution
        var setW = new SolidVolumeSet(); setW.Register("wall", Wall(30), w, h, d, Vector3.zero, vs);
        var castW = Caster(setW);
        Vector3 pW = new Vector3(1.0f, 0.5f, 1.0f), vW = new Vector3(6f, 0f, 0f);
        var rW = ShardStep.Free; VoxelRayHit hW = default;
        for (int f = 0; f < 40 && rW != ShardStep.Contact; f++)
            rW = ShardPhysics.Step(ref pW, ref vW, r, dt60, 0f, e, fr, castW, out hW);
        Check(rW == ShardStep.Contact, "a shard flying at a wall must hit it");
        Check(Mathf.Abs(pW.x - (1.5f - r - ShardPhysics.Skin)) < 1e-3f, $"wall stop x {pW.x:F4}");
        Check(Mathf.Abs(vW.x + 6f * e) < 0.01f && Mathf.Abs(vW.y) < 1e-4f && Mathf.Abs(vW.z) < 1e-4f, $"wall bounce should be vx = -{6f * e:F2}, got {vW}");
        Check(hW.normal.x < -0.99f, $"wall normal should face the shard, got {hW.normal}");

        // D. the same wall in a YAWED volume: contact + bounce happen about the rotated normal
        const float yawDeg = 30f;
        float yr = yawDeg * Mathf.Deg2Rad, yc = Mathf.Cos(yr), ys = Mathf.Sin(yr);
        Vector3 Rot(Vector3 p, float sgn) => new Vector3(p.x * yc + p.z * sgn * ys, p.y, -p.x * sgn * ys + p.z * yc);
        Vector3 half = new Vector3(w, h, d) * (vs * 0.5f), corner = new Vector3(3f, 0.2f, 5f), centre = corner + half;
        var setY = new SolidVolumeSet(); setY.Register("wallY", Wall(30), w, h, d, corner, vs, yawDeg);
        var castY = Caster(setY);
        Vector3 pY = centre + Rot(new Vector3(1.0f, 0.5f, 1.0f) - half, 1f), vY = Rot(new Vector3(6f, 0f, 0f), 1f);
        var rY = ShardStep.Free;
        for (int f = 0; f < 40 && rY != ShardStep.Contact; f++)
            rY = ShardPhysics.Step(ref pY, ref vY, r, dt60, 0f, e, fr, castY, out _);
        Vector3 localP = Rot(pY - centre, -1f) + half;
        Check(rY == ShardStep.Contact, "shard must hit the yawed wall");
        Check(Mathf.Abs(localP.x - (1.5f - r - ShardPhysics.Skin)) < 1e-3f, $"yawed wall stop (volume-local x) {localP.x:F4}");
        Check(Vector3.Dot(vY.normalized, Rot(new Vector3(-1f, 0f, 0f), 1f)) > 0.999f && Mathf.Abs(vY.magnitude - 6f * e) < 0.02f,
            $"yawed bounce should leave along the rotated normal at 0.35 x speed, got {vY} ({vY.magnitude:F2})");

        // E. a shard whose center is already inside a solid cell is reported, not moved through the floor
        Vector3 pE = new Vector3(1f, 0.1f, 1f), vE = new Vector3(0f, -1f, 0f);
        Check(ShardPhysics.Step(ref pE, ref vE, r, dt60, 9f, e, fr, cast, out _) == ShardStep.Embedded, "a shard inside a solid cell must report Embedded");
        Check((pE - new Vector3(1f, 0.1f, 1f)).sqrMagnitude < 1e-10f, "an embedded shard must not be moved");

        // F. skimming along a floor at 2 m/s: the center never crosses the floor plane
        Vector3 pS = new Vector3(0.5f, 0.2f + r + ShardPhysics.Skin, 1f), vS = new Vector3(2f, 0f, 0f);
        float minS = float.MaxValue; bool embS = false;
        for (int f = 0; f < 90; f++)
        {
            if (ShardPhysics.Step(ref pS, ref vS, r, dt60, 9f, e, fr, cast, out _) == ShardStep.Embedded) embS = true;
            minS = Mathf.Min(minS, pS.y);
        }
        Check(!embS && minS >= 0.2f, $"skimming shard must stay above the floor plane, minY {minS:F4}");

        // G. fuzz: shards in a sealed box room (floor 4 cells, walls/ceiling 2) with random velocity up to 12 m/s,
        //    random frame times 1/120..1/20 s — no shard ever ends up inside solid, none reports Embedded.
        var room = new uint[w * h * d];
        bool Inner(int x, int y, int z) => x >= 2 && x < w - 2 && y >= 4 && y < h - 2 && z >= 2 && z < d - 2;
        for (int z = 0; z < d; z++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (!Inner(x, y, z)) room[x + y * w + z * w * h] = 100;
        var setR = new SolidVolumeSet(); setR.Register("room", room, w, h, d, Vector3.zero, vs);
        var castR = Caster(setR);
        int bad = 0, contacts = 0, shardsRun = 0;
        for (int s = 0; s < 700; s++)
        {
            float rr = s % 7 == 0 ? 0.1f : 0.01875f;                                   // some chunk-sized shards
            Vector3 p = new Vector3((float)R(0.3, 1.7), (float)R(0.35, 0.8), (float)R(0.3, 1.7));
            Vector3 v = new Vector3((float)R(-12, 12), (float)R(-12, 12), (float)R(-12, 12));
            shardsRun++;
            for (int f = 0; f < 300; f++)
            {
                float fdt = (float)R(1.0 / 120, 1.0 / 20);
                var res = ShardPhysics.Step(ref p, ref v, rr, fdt, 9f, e, fr, castR, out _);
                if (res == ShardStep.Contact) contacts++;
                int cx = (int)Mathf.Floor(p.x / vs), cy = (int)Mathf.Floor(p.y / vs), cz = (int)Mathf.Floor(p.z / vs);
                bool outside = cx < 0 || cx >= w || cy < 0 || cy >= h || cz < 0 || cz >= d;
                if (res == ShardStep.Embedded || outside || room[cx + cy * w + cz * w * h] != 0u) { bad++; break; }
            }
        }
        Check(bad == 0, $"fuzz: {bad} of {shardsRun} shards tunnelled into / through the room walls");
        Check(contacts > 5000, $"fuzz should exercise many bounces, got {contacts}");

        // cost note: one sweep ray per airborne shard against a city's worth of registered volumes
        var big = new SolidVolumeSet();
        var tiny = new uint[8 * 8 * 8]; for (int i = 0; i < tiny.Length; i++) tiny[i] = 100;
        for (int i = 0; i < 800; i++) big.Register("b" + i, tiny, 8, 8, 8, new Vector3((i % 40) * 12f, 0f, (i / 40) * 12f), vs);
        var castBig = Caster(big);
        var sw = Stopwatch.StartNew(); int rays = 192 * 20; int sink = 0;
        for (int i = 0; i < rays; i++) { Vector3 o = new Vector3((i * 7 % 480) + 0.2f, 1f, (i * 13 % 240) + 0.2f); if (castBig(o, Vector3.down, 0.1f, out _)) sink++; }
        sw.Stop();
        Console.WriteLine($"    debris sweep: tossed {contacts} bounces over {shardsRun} fuzz shards · 800-volume broadphase {sw.Elapsed.TotalMilliseconds * 1000.0 / rays:F2} us/ray -> 192 shards = {sw.Elapsed.TotalMilliseconds / 20.0:F2} ms/frame");
        Console.WriteLine("[13] shard collision test done");
    }

    // [14] Rubble: a resting shard writes its material into the AIR cells it settled on — dust = 1 cell on the first
    // air layer above the surface, a chunk = an n^3 block, piles stack, solid is never overwritten, bounds clip.
    static void TestRubble()
    {
        const float vs = 0.05f, e = 0.35f, fr = 0.6f;
        const int w = 40, h = 20, d = 40;
        int I(int x, int y, int z) => x + y * w + z * w * h;
        var data = new uint[w * h * d];
        for (int z = 0; z < d; z++) for (int y = 0; y < 4; y++) for (int x = 0; x < w; x++) data[I(x, y, z)] = 100;   // floor, top face at 0.2 m
        var set = new SolidVolumeSet(); set.Register("pile", data, w, h, d, Vector3.zero, vs);
        var cast = Caster(set);
        var dirty = new List<int>();

        // drop a shard straight down, deposit where it comes to rest; returns the lowest deposited layer
        int DropDeposit(Vector3 start, float size, uint mat, out int count)
        {
            float rr = size * 0.5f; Vector3 p = start, v = Vector3.zero;
            for (int f = 0; f < 600; f++)
            {
                var res = ShardPhysics.Step(ref p, ref v, rr, 1f / 60f, 9f, e, fr, cast, out var hh);
                if (res == ShardStep.Contact && hh.normal.y > 0.7f && v.sqrMagnitude < 0.16f) break;
            }
            ShardPhysics.DepositBox(p / vs, size / vs, 20, out int x0, out int y0, out int z0, out int n);
            dirty.Clear();
            count = ShardPhysics.Deposit(data, w, h, d, x0, y0, z0, n, mat, dirty);
            return y0;
        }

        // dust (0.75 cell) stacks: layer 4 = first air layer above the floor, then 5, 6 — each lands on the last
        int c;
        int y1 = DropDeposit(new Vector3(1.025f, 0.9f, 1.025f), 0.75f * vs, 101, out c);
        Check(y1 == 4 && c == 1 && data[I(20, 4, 20)] == 101u, $"dust 1 should fill (20,4,20), got layer {y1} count {c}");
        int y2 = DropDeposit(new Vector3(1.025f, 0.9f, 1.025f), 0.75f * vs, 101, out c);
        Check(y2 == 5 && c == 1 && data[I(20, 5, 20)] == 101u, $"dust 2 should stack on dust 1 (layer 5), got layer {y2} count {c}");
        int y3 = DropDeposit(new Vector3(1.025f, 0.9f, 1.025f), 0.75f * vs, 101, out c);
        Check(y3 == 6 && data[I(20, 6, 20)] == 101u, $"dust 3 should reach layer 6, got {y3}");

        // chunk (4 cells = 0.2 m): a 4x4x4 block sitting on the floor, centered under where it rested
        int yc = DropDeposit(new Vector3(1.5f, 0.9f, 1.5f), 4f * vs, 102, out c);
        Check(yc == 4 && c == 64, $"4-cell chunk should deposit 64 cells from layer 4, got layer {yc} count {c}");
        bool block = true;
        for (int z = 28; z < 32; z++) for (int y = 4; y < 8; y++) for (int x = 28; x < 32; x++) block &= data[I(x, y, z)] == 102u;
        Check(block, "chunk block should occupy x/z 28..31, y 4..7");
        Check(data[I(27, 4, 29)] == 0u && data[I(32, 4, 29)] == 0u && data[I(29, 8, 29)] == 0u, "chunk deposit must not spill outside its box");

        // never overwrite solid: a blocker inside the box survives, the other 63 cells fill
        var g2 = new uint[w * h * d]; g2[I(5, 5, 5)] = 101u;
        dirty.Clear();
        int cnt = ShardPhysics.Deposit(g2, w, h, d, 4, 4, 4, 4, 102u, dirty);
        Check(cnt == 63 && g2[I(5, 5, 5)] == 101u && dirty.Count == 63, $"solid cell must survive the deposit: wrote {cnt}, blocker {g2[I(5, 5, 5)]}");

        // bounds: a box hanging off the grid corner clips (no wrap into the next row/slab), indices valid + unique
        var g3 = new uint[w * h * d];
        dirty.Clear();
        int clipped = ShardPhysics.Deposit(g3, w, h, d, -2, 4, -2, 4, 102u, dirty);
        var uniq = new HashSet<int>(dirty);
        bool inRange = true; foreach (int i in dirty) inRange &= i >= 0 && i < g3.Length;
        Check(clipped == 16 && uniq.Count == 16 && inRange, $"corner box should clip to 2x4x2 = 16 cells, got {clipped}");
        Check(g3[I(w - 1, 4, 0)] == 0u && g3[I(0, 4, d - 1)] == 0u, "clipped deposit must not wrap to the opposite edge");

        // box math: chunk edge is capped, dust never rounds to zero cells
        ShardPhysics.DepositBox(new Vector3(10f, 10f, 10f), 50f, 20, out _, out _, out _, out int nCap);
        ShardPhysics.DepositBox(new Vector3(10f, 10f, 10f), 0.4f, 20, out _, out _, out _, out int nMin);
        Check(nCap == 20 && nMin == 1, $"edge cap/floor: got {nCap} / {nMin}");
        Console.WriteLine("    rubble: dust stacks 4->5->6 · 4-cell chunk = 64-cell block · solid kept · bounds clipped");
        Console.WriteLine("[14] rubble deposit test done");
    }

    // [15] Mass-conserving debris (ShardPhysics.BuildBuckets + DepositHeap): removed cells group into
    //      buckets that each become ONE shard carrying the bucket's cell count as mass; on rest the
    //      DepositHeap spills that mass into AIR cells as a shrinking-layer mound.
    static void TestMassDebris()
    {
        const int w = 40, h = 24, d = 40;
        int I(int x, int y, int z) => x + y * w + z * w * h;
        var buckets = new Dictionary<int, ShardPhysics.DebrisBucket>();
        var idx = new List<int>(); var mats = new List<uint>(); var vals = new List<uint>();
        var dirty = new List<int>();

        // a dense 10x2x10 slab inside one bucket cell at cs=18 -> ONE bucket of mass 200, majority material, true centroid
        for (int z = 2; z < 12; z++) for (int y = 4; y < 6; y++) for (int x = 5; x < 15; x++) { idx.Add(I(x, y, z)); mats.Add(100u); }
        ShardPhysics.BuildBuckets(idx, mats, w, h, 0, idx.Count, 18, buckets);
        Check(buckets.Count == 1, $"slab should be one bucket, got {buckets.Count}");
        var b = default(ShardPhysics.DebrisBucket); foreach (var v in buckets.Values) b = v;
        Check(b.count == 200 && b.mat == 100u, $"slab bucket mass/mat: {b.count} / {b.mat}");
        Check(Mathf.Abs(b.sx / (float)b.count - 9.5f) < 0.01f && Mathf.Abs(b.sz / (float)b.count - 6.5f) < 0.01f, "slab centroid");

        // two clusters a pitch apart -> 2 buckets; Boyer-Moore vote keeps the majority material
        idx.Clear(); mats.Clear();
        for (int i = 0; i < 30; i++) { idx.Add(I(2 + i % 5, 4 + i / 5 % 6, 2 + i / 5 / 6)); mats.Add(100u); }
        for (int i = 0; i < 30; i++) { idx.Add(I(30 + i % 5, 4 + i / 5 % 6, 30 + i / 5 / 6)); mats.Add(i < 8 ? 106u : 100u); }
        ShardPhysics.BuildBuckets(idx, mats, w, h, 0, idx.Count, 10, buckets);
        Check(buckets.Count == 2, $"two clusters should be 2 buckets, got {buckets.Count}");
        bool sawBrick = false; foreach (var v in buckets.Values) if (v.mat == 100u && v.count == 30) sawBrick = true;
        Check(sawBrick, "majority vote should keep brick for the mixed bucket");
        bool sawBlend = false; foreach (var v in buckets.Values)
            if (v.mat == 100u && v.majCells == 22 && v.othCells == 8 && v.mat2 == 106u) sawBlend = true;
        Check(sawBlend, "mixed bucket should carry exact 22/8 brick/wood share for blending");

        // vals filter: non-AIR writes (cracked cells) never spawn debris
        vals.Clear(); for (int i = 0; i < idx.Count; i++) vals.Add(i % 2 == 0 ? 134u : 0u);
        ShardPhysics.BuildBuckets(idx, mats, w, h, 0, idx.Count, 10, buckets, vals);
        int total = 0; foreach (var v in buckets.Values) total += v.count;
        Check(total == 30, $"half the cells are cracks -> 30 cells bucketed, got {total}");

        // heap: mass 9 on a clear floor -> all 9 land in the base layer (r0=1, 3x3), nothing above
        var data2 = new uint[w * h * d];
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++) data2[I(x, 2, z)] = 100u;  // solid floor
        dirty.Clear();
        int w1 = ShardPhysics.DepositHeap(data2, w, h, d, 15, 3, 15, 9, 20, 106u, 0u, 0f, 9, dirty);
        Check(w1 == 9 && dirty.Count == 9, $"mass-9 heap should write 9 cells, got {w1}");
        Check(data2[I(15, 3, 15)] == 106u, "heap writes at the rest cell");
        Check(data2[I(15, 4, 15)] == 0u, "mass-9 heap should not reach a second layer");
        Check(data2[I(15, 2, 15)] == 100u, "solid floor untouched");

        // mass 500 -> mound grows layers (r0=ceil(cbrt(500*0.375))=6; layer0 13x13=169, layer1 13x13, layer2 11x11...)
        dirty.Clear();
        int w2 = ShardPhysics.DepositHeap(data2, w, h, d, 15, 3, 15, 500, 20, 106u, 0u, 0f, 500, dirty);
        Check(w2 == 500, $"mass-500 heap should write all 500 cells, got {w2}");
        Check(data2[I(15, 4, 15)] == 106u && data2[I(15, 5, 15)] == 106u, "mass-500 heap should stack layers");
        Check(data2[I(15, 2, 15)] == 100u, "solid floor still untouched under the mound");

        // solid cells are never overwritten: wall through the heap -> written < footprint, never on solid
        var data3 = new uint[w * h * d];
        for (int z = 0; z < d; z++) for (int x = 0; x < w; x++) data3[I(x, 1, z)] = 100u;
        for (int y = 2; y < 10; y++) data3[I(15, y, 15)] = 100u;   // solid column through the centre
        dirty.Clear();
        int w3 = ShardPhysics.DepositHeap(data3, w, h, d, 15, 2, 15, 40, 20, 106u, 0u, 0f, 40, dirty);
        Check(w3 == 40, $"heap should still spend all 40 cells around the column, got {w3}");
        Check(data3[I(15, 2, 15)] == 100u && data3[I(15, 3, 15)] == 100u, "never overwrites the solid column");
        Check(data3[I(14, 2, 15)] == 106u, "heap fills beside the column");

        // grid edge: heap at the corner is clipped, not out-of-bounds
        dirty.Clear();
        int w4 = ShardPhysics.DepositHeap(data3, w, h, d, 0, 2, 0, 9, 20, 106u, 0u, 0f, 9, dirty);
        Check(w4 == 9 && dirty.Count == 9, $"corner heap should write all 9 cells in-bounds, got {w4}");

        // pour budget: maxWrite caps a call; re-calling resumes the pile (written cells are solid now)
        var data4 = new uint[w * h * d];
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++) data4[I(x, 2, z)] = 100u;
        dirty.Clear();
        int p1 = ShardPhysics.DepositHeap(data4, w, h, d, 15, 3, 15, 200, 20, 106u, 0u, 0f, 50, dirty);
        int p2 = ShardPhysics.DepositHeap(data4, w, h, d, 15, 3, 15, 200, 20, 106u, 0u, 0f, 50, dirty);
        Check(p1 == 50 && p2 == 50, $"pour ticks should write exactly the budget, got {p1}+{p2}");
        Check(dirty.Count == 100 && new HashSet<int>(dirty).Count == 100, "pour ticks must not double-write cells");

        // material blend: 60/40 bucket writes a genuinely mottled mound
        var data5 = new uint[w * h * d];
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++) data5[I(x, 2, z)] = 100u;
        dirty.Clear();
        int w5 = ShardPhysics.DepositHeap(data5, w, h, d, 15, 3, 15, 300, 20, 100u, 106u, 0.4f, 300, dirty);
        int nWood = 0; foreach (int di in dirty) if (data5[di] == 106u) nWood++;
        Check(w5 == 300, $"blend heap wrote {w5}/300");
        Check(nWood > 80 && nWood < 160, $"40% wood blend should land ~120 of 300 cells, got {nWood}");

        // ragged rim: outermost-ring cells of each layer are probabilistically skipped (deterministic)
        int rimAir = 0;
        for (int x = 9; x <= 21; x++) for (int z = 9; z <= 21; z++)
            if (Mathf.Max(Mathf.Abs(x - 15), Mathf.Abs(z - 15)) == 6 && data2[I(x, 3, z)] == 0u) rimAir++;
        Check(rimAir > 0, "mass-500 mound should leave deterministic gaps on the outer ring");

        // form-fit: a crater under the deposit point swallows the mound's cells first
        var data6 = new uint[w * h * d];
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++)
        { data6[I(x, 0, z)] = 100u; data6[I(x, 1, z)] = 100u; data6[I(x, 2, z)] = 100u; }
        for (int z = 14; z <= 16; z++) for (int x = 14; x <= 16; x++)
        { data6[I(x, 1, z)] = 0u; data6[I(x, 2, z)] = 0u; }    // 3x3 crater punched to the floor
        dirty.Clear();
        int w6 = ShardPhysics.DepositHeap(data6, w, h, d, 15, 3, 15, 9, 20, 106u, 0u, 0f, 9, dirty);
        Check(w6 == 9 && data6[I(15, 1, 15)] == 106u, $"mass-9 over a 3x3 crater should fill the pit, got {w6} w/ floor {data6[I(15, 1, 15)]}");
        Check(data6[I(15, 3, 15)] == 0u, "cells dropped into the crater, not left floating at rest height");
        dirty.Clear();
        int w7 = ShardPhysics.DepositHeap(data6, w, h, d, 15, 3, 15, 18, 20, 106u, 0u, 0f, 18, dirty);
        Check(w7 == 18, $"mass-18 pours over the filled pit then the rim, got {w7}");
        Check(data6[I(15, 2, 15)] == 106u, "once the pit floor is full the next cells stack inside it");

        // slump: a tall rubble column relaxes toward a 45-degree cone — mass conserved, nothing
        // left that can still slide, every rubble cell supported
        var data7 = new uint[w * h * d];
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++) data7[I(x, 1, z)] = 100u;
        var rubble7 = new HashSet<int>(); var slump7 = new List<int>();
        for (int y = 2; y < 10; y++) { data7[I(15, y, 15)] = 106u; rubble7.Add(I(15, y, 15)); slump7.Add(I(15, y, 15)); }
        dirty.Clear();
        int mv1 = ShardPhysics.Slump(data7, w, h, d, rubble7, slump7, 10000, dirty);
        Check(mv1 > 0 && slump7.Count == 0, $"8-tall column should fully relax within budget, {mv1} moves");
        bool supported = true; int canSlide = 0;
        foreach (int ri in rubble7)
        {
            if ((data7[ri] & VoxelDda.MaterialMask) == 0u) { supported = false; continue; }
            int z7 = ri / (w * h), rem7 = ri - z7 * w * h, y7 = rem7 / w, x7 = rem7 - y7 * w;
            if (y7 > 0 && (data7[ri - w] & VoxelDda.MaterialMask) == 0u) supported = false;
            if (y7 == 0) continue;                                                                    // grounded (buried cells CAN slide)
            if (x7 + 1 < w && (data7[ri + 1 - w] & VoxelDda.MaterialMask) == 0u) canSlide++;
            if (x7 > 0 && (data7[ri - 1 - w] & VoxelDda.MaterialMask) == 0u) canSlide++;
            if (z7 + 1 < d && (data7[ri + w * h - w] & VoxelDda.MaterialMask) == 0u) canSlide++;
            if (z7 > 0 && (data7[ri - w * h - w] & VoxelDda.MaterialMask) == 0u) canSlide++;
        }
        Check(rubble7.Count == 8 && supported && canSlide == 0,
            $"relaxed pile: 8 cells, all supported, no slides left (supported={supported} canSlide={canSlide})");
        int spread = (data7[I(14, 2, 15)] == 106u ? 1 : 0) + (data7[I(16, 2, 15)] == 106u ? 1 : 0)
                   + (data7[I(15, 2, 14)] == 106u ? 1 : 0) + (data7[I(15, 2, 16)] == 106u ? 1 : 0);
        Check(spread > 0, "column top should spill onto the floor around the base");

        // slump spills over a ledge edge; structural cells never move; a second run is deterministic
        var data8 = new uint[w * h * d]; var data8b = new uint[w * h * d];
        var rubble8 = new HashSet<int>(); var slump8 = new List<int>();
        var rubble8b = new HashSet<int>(); var slump8b = new List<int>();
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++) { data8[I(x, 1, z)] = 100u; data8b[I(x, 1, z)] = 100u; }
        for (int z = 10; z < 15; z++) for (int x = 10; x < 15; x++) for (int y = 2; y <= 5; y++) { data8[I(x, y, z)] = 100u; data8b[I(x, y, z)] = 100u; }
        for (int z = 12; z <= 14; z++) for (int x = 12; x <= 14; x++)
        { data8[I(x, 6, z)] = 106u; rubble8.Add(I(x, 6, z)); slump8.Add(I(x, 6, z));
          data8b[I(x, 6, z)] = 106u; rubble8b.Add(I(x, 6, z)); slump8b.Add(I(x, 6, z)); }
        dirty.Clear();
        int mv2 = ShardPhysics.Slump(data8, w, h, d, rubble8, slump8, 10000, dirty);
        int off = 0;
        foreach (int ri in rubble8)
        { int z8 = ri / (w * h), rem8 = ri - z8 * w * h, x8 = rem8 % w; if (x8 >= 15 || z8 >= 15) off++; }
        Check(off > 0, $"edge cells should cascade off the ledge, {off} off-footprint");
        bool ledgeOk = data8[I(12, 3, 12)] == 100u && data8[I(14, 5, 14)] == 100u;
        Check(ledgeOk, "structural cells never move under slump");
        dirty.Clear();
        int mv3 = ShardPhysics.Slump(data8b, w, h, d, rubble8b, slump8b, 10000, dirty);
        bool same = mv2 == mv3; if (same) for (int i = 0; i < data8.Length; i++) if (data8[i] != data8b[i]) { same = false; break; }
        Check(same, "slump is deterministic for identical inputs");

        // unsupported rubble drips: a cell tucked under an overhang falls to the ground,
        // not a frozen stalactite (structural ceiling above, open air below)
        var data9 = new uint[w * h * d];
        for (int z = 10; z < 20; z++) for (int x = 10; x < 20; x++) data9[I(x, 1, z)] = 100u;
        for (int z = 14; z <= 16; z++) for (int x = 14; x <= 16; x++) data9[I(x, 6, z)] = 100u;   // ceiling slab
        var rubble9 = new HashSet<int>(); var slump9 = new List<int>();
        data9[I(15, 5, 15)] = 106u; rubble9.Add(I(15, 5, 15)); slump9.Add(I(15, 5, 15));        // hanging cell
        dirty.Clear();
        ShardPhysics.Slump(data9, w, h, d, rubble9, slump9, 10000, dirty);
        Check(data9[I(15, 2, 15)] == 106u && data9[I(15, 5, 15)] == 0u,
            "hanging rubble must drip down through open air to the floor");
        Check(data9[I(15, 6, 15)] == 100u, "the ceiling slab above is untouched");

        Console.WriteLine("    mass debris: slab=1 bucket mass 200 · majority vote+blend share · crack filter · heap 9/500 cells · solid kept · corner clipped · pour 50+50 · blend ~40% · ragged rim");
        Console.WriteLine("[15] debris mass test done");
    }

    // [16] Macro-cell occupancy skip (VoxelProxyRaymarch two-level DDA): a
    // straight port of the shader's skip math. Invariants vs flat DDA on the
    // same grid + ray: every cell the skip march samples is one the flat march
    // visited, and the first solid hit cell is identical. Rays include exact
    // axis/diagonal directions — the worst fp-tie cases for boundary crossing.
    static List<int> FlatMarch(uint[] data, int w, int h, int d,
        float lx, float ly, float lz, float rx, float ry, float rz, out int firstSolid)
    {
        var cells = new List<int>();
        firstSolid = -1;
        int vx = Math.Min(w - 1, Math.Max(0, (int)Math.Floor(lx)));
        int vy = Math.Min(h - 1, Math.Max(0, (int)Math.Floor(ly)));
        int vz = Math.Min(d - 1, Math.Max(0, (int)Math.Floor(lz)));
        int sx = Math.Sign(rx), sy = Math.Sign(ry), sz = Math.Sign(rz);
        float dx = Math.Abs(1f / rx), dy = Math.Abs(1f / ry), dz = Math.Abs(1f / rz);
        float mx = sx < 0 ? (lx - vx) * dx : (vx + 1 - lx) * dx;
        float my = sy < 0 ? (ly - vy) * dy : (vy + 1 - ly) * dy;
        float mz = sz < 0 ? (lz - vz) * dz : (vz + 1 - lz) * dz;
        for (int i = 0; i < 2000; i++)
        {
            if (vx < 0 || vy < 0 || vz < 0 || vx >= w || vy >= h || vz >= d) break;
            int idx = vx + vy * w + vz * w * h;
            cells.Add(idx);
            if (firstSolid < 0 && (data[idx] & 0x1FFu) != 0) firstSolid = idx;
            if (mx < my) { if (mx < mz) { vx += sx; mx += dx; } else { vz += sz; mz += dz; } }
            else { if (my < mz) { vy += sy; my += dy; } else { vz += sz; mz += dz; } }
        }
        return cells;
    }

    static List<int> SkipMarch(uint[] data, uint[] occ, int w, int h, int d, int mdx, int mdy,
        float lx, float ly, float lz, float rx, float ry, float rz, out int firstSolid)
    {
        var cells = new List<int>();
        firstSolid = -1;
        int vx = Math.Min(w - 1, Math.Max(0, (int)Math.Floor(lx)));
        int vy = Math.Min(h - 1, Math.Max(0, (int)Math.Floor(ly)));
        int vz = Math.Min(d - 1, Math.Max(0, (int)Math.Floor(lz)));
        int sx = Math.Sign(rx), sy = Math.Sign(ry), sz = Math.Sign(rz);
        float dx = Math.Abs(1f / rx), dy = Math.Abs(1f / ry), dz = Math.Abs(1f / rz);
        float mx = sx < 0 ? (lx - vx) * dx : (vx + 1 - lx) * dx;
        float my = sy < 0 ? (ly - vy) * dy : (vy + 1 - ly) * dy;
        float mz = sz < 0 ? (lz - vz) * dz : (vz + 1 - lz) * dz;
        for (int i = 0; i < 2000; i++)
        {
            if (vx < 0 || vy < 0 || vz < 0 || vx >= w || vy >= h || vz >= d) break;
            // ---- shader macro-skip port ----
            for (int k = 0; k < 16; k++)
            {
                if (vx < 0 || vy < 0 || vz < 0 || vx >= w || vy >= h || vz >= d) break;
                int mvx = vx >> 2, mvy = vy >> 2, mvz = vz >> 2;
                int mi = mvx + mdx * (mvy + mdy * mvz);
                if (((occ[mi >> 5] >> (mi & 31)) & 1u) != 0) break;
                int m0x = mvx << 2, m0y = mvy << 2, m0z = mvz << 2;
                int tex = sx > 0 ? m0x + 4 - vx : vx - m0x + 1;
                int tey = sy > 0 ? m0y + 4 - vy : vy - m0y + 1;
                int tez = sz > 0 ? m0z + 4 - vz : vz - m0z + 1;
                float ex = tex == 1 ? mx : mx + (tex - 1) * dx;
                float ey = tey == 1 ? my : my + (tey - 1) * dy;
                float ez = tez == 1 ? mz : mz + (tez - 1) * dz;
                float exitT = Math.Min(ex, Math.Min(ey, ez));
                while (mx <= exitT) { vx += sx; mx += dx; }
                while (my <= exitT) { vy += sy; my += dy; }
                while (mz <= exitT) { vz += sz; mz += dz; }
            }
            if (vx < 0 || vy < 0 || vz < 0 || vx >= w || vy >= h || vz >= d) break;
            // ---- end port ----
            int idx = vx + vy * w + vz * w * h;
            cells.Add(idx);
            if (firstSolid < 0 && (data[idx] & 0x1FFu) != 0) firstSolid = idx;
            if (firstSolid >= 0) break;   // flat keeps marching for the set check; skip mirrors the hit break
            if (mx < my) { if (mx < mz) { vx += sx; mx += dx; } else { vz += sz; mz += dz; } }
            else { if (my < mz) { vy += sy; my += dy; } else { vz += sz; mz += dz; } }
        }
        return cells;
    }

    static void TestOccSkip()
    {
        int L = checks;
        var r2 = new System.Random(7919);
        for (int cfg = 0; cfg < 6; cfg++)
        {
            int w = cfg == 4 ? 18 : 16, h = cfg == 4 ? 10 : 16, d = cfg == 4 ? 14 : 16;
            var data = new uint[w * h * d];
            for (int z = 0; z < d; z++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double r = r2.NextDouble();
                bool solid =
                    cfg == 0 ? r < 0.05 :
                    cfg == 1 ? r < 0.30 :
                    cfg == 2 ? r < 0.90 :
                    cfg == 3 ? (x == 0 || y == 0 || z == 0 || x == w - 1 || y == h - 1 || z == d - 1 || (y <= 2 && r < 0.55)) :
                    cfg == 4 ? r < 0.25 :
                               (y % 4 == 0);   // 5: floor slabs, air between
                data[x + y * w + z * w * h] = solid ? (uint)(1 + r2.Next(6)) : 0u;
            }
            int mdx = (w + 3) >> 2, mdy = (h + 3) >> 2, mdz = (d + 3) >> 2;
            var occ = new uint[(mdx * mdy * mdz + 31) >> 5];
            for (int z = 0; z < d; z++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if ((data[x + y * w + z * w * h] & 0x1FFu) != 0)
                {
                    int mi = (x >> 2) + mdx * ((y >> 2) + mdy * (z >> 2));
                    occ[mi >> 5] |= 1u << (mi & 31);
                }
            for (int ray = 0; ray < 400; ray++)
            {
                float rx, ry, rz;
                int mode = ray % 9;
                if (mode == 0) { rx = ry = rz = ray % 18 == 0 ? -1f : 1f; }
                else if (mode == 1) { rx = 0f; ry = 1f; rz = 0f; }
                else if (mode == 2) { rx = ray % 4 == 0 ? -1f : 1f; ry = 0f; rz = 0.35f; }
                else { rx = (float)(r2.NextDouble() * 2 - 1); ry = (float)(r2.NextDouble() * 2 - 1); rz = (float)(r2.NextDouble() * 2 - 1); }
                if (rx == 0 && ry == 0 && rz == 0) rx = 0.4f;
                float lx = (float)(r2.NextDouble() * w), ly = (float)(r2.NextDouble() * h), lz = (float)(r2.NextDouble() * d);
                var flat = FlatMarch(data, w, h, d, lx, ly, lz, rx, ry, rz, out int flatHit);
                var skip = SkipMarch(data, occ, w, h, d, mdx, mdy, lx, ly, lz, rx, ry, rz, out int skipHit);
                Check(flatHit == skipHit,
                    $"cfg{cfg} ray{ray}: first solid hit must match flat DDA (flat={flatHit} skip={skipHit})");
                var flatSet = new HashSet<int>(flat);
                bool sub = true;
                foreach (int c in skip) sub &= flatSet.Contains(c);
                Check(sub, $"cfg{cfg} ray{ray}: skip sampled a cell the flat march never visited");
                Check(skip.Count <= flat.Count, $"cfg{cfg} ray{ray}: skip sampled {skip.Count} cells > flat {flat.Count}");
            }
        }
        Console.WriteLine($"[16] occ-skip DDA equivalence done (+{checks - L} checks)");
        TestFallingCells();
        TestFlyerTransport();
        Console.WriteLine("[17] cellular collapse (in-grid ballistic cells) done");
    }

    static string rangePath;
    static string Describe2(VoxelRayHit r) => $"vol={r.volumeId} mat={r.material} cell=({r.cell.x},{r.cell.y},{r.cell.z}) dist={r.distance:F3}m";

    static int Main(string[] args)
    {
        TestDdaVsBrute();
        TestAxisAligned();
        TestByteGrid();
        TestVolumeSet();
        TestYaw();
        rangePath = args.Length > 0 ? args[0] : null;
        TestRange(rangePath);
        TestBlast();
        TestConnectivity();
        TestBlastOcclusion();
        TestSimulate();
        TestMarchPathLength();
        TestShardCollision();
        TestRubble();
        TestMassDebris();
        TestOccSkip();
        Console.WriteLine(fails == 0 ? $"ALL PASS ({checks} checks)" : $"{fails} FAILURES of {checks} checks");
        return fails == 0 ? 0 : 1;
    }
}
