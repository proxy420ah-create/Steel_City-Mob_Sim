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

        // invalid registrations are ignored
        set.Register(null, new uint[1], 1, 1, 1, Vector3.zero, 1f);
        set.Register("x", null, 1, 1, 1, Vector3.zero, 1f);
        set.Register("x", new uint[1], 2, 2, 2, Vector3.zero, 1f);
        set.Register("x", new uint[8], 2, 2, 2, Vector3.zero, 0f);
        Check(set.Count == 0, "invalid registrations ignored: " + set.Count);
        Console.WriteLine("[4] SolidVolumeSet registry / placement checks done");
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
            for (int y = 8; y <= 12; y++) for (int z = 8; z <= 12; z++) g[I(13, y, z)] = 106;   // patch always at x=13, dist ~3
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
        int thin = EastRemoved(106u, 12, 12);        // 1 brick cell → transmit 0.5
        int thick = EastRemoved(106u, 10, 12);       // 3 brick cells → transmit 0.125
        int glass = EastRemoved(112u, 12, 12);       // 1 glass cell → transmit 0.95
        Check(open > 0, "open patch should take damage");
        Check(thin < open, $"thin brick should attenuate: open {open} vs shielded {thin}");
        Check(thick <= Math.Max(1, open / 6), $"thick brick should nearly shield: open {open} vs shielded {thick}");
        Check(glass >= open - 2, $"glass should barely shield: open {open} vs glass {glass}");
        Console.WriteLine($"    occlusion: open {open} · brick1 {thin} · brick3 {thick} · glass {glass} (patch cells removed)");
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
        Console.WriteLine(fails == 0 ? $"ALL PASS ({checks} checks)" : $"{fails} FAILURES of {checks} checks");
        return fails == 0 ? 0 : 1;
    }
}
