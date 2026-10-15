using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    /// <summary>
    /// Pure-C# voxel damage math (no UnityEngine types) so the offline selftest
    /// (Tools/dda_selftest) compiles the exact code the runtime runs.
    ///
    /// P0 puncture per GLASS_SHATTER_MECHANIC.md: the impact cell is removed and the
    /// ring-1 neighbors in the plane perpendicular to the shot direction are
    /// seeded-removed at ring1RemoveFraction; survivors swap to the cracked material.
    /// Glass cells only — non-glass (the mold's metal core, brick sills) is never
    /// touched, which makes indestructible cores free by construction.
    /// </summary>
    public static class VoxelDamageMath
    {
        public const uint MaterialMask = 0x1FFu;   // VoxelDda.MaterialMask — duplicated so this file stays dependency-free

        // ── Damage tint — bits 16-19 of the voxel word ──
        // Disk is uint16 (bits 0-8 material; 9-15 reserved shape/rotation), but the
        // runtime buffer is uint32 — bits 16+ are unreachable by the format, so the
        // nibble can never collide with authored data AND never saves back into an
        // asset (damage is runtime state by definition).
        // 0 = clean · 1-7 = char (darken) · 8-15 = glint (lighten — crack webbing).
        public const int  DamageShift = 16;
        public const uint DamageMask  = 0xFu << DamageShift;

        public static uint DamageOf(uint v)          => (v & DamageMask) >> DamageShift;
        public static uint WithDamage(uint v, uint d) => (v & ~DamageMask) | ((d & 0xFu) << DamageShift);
        /// <summary>0..1 darken for char levels 1-7 (0 for clean/glint).</summary>
        public static float CharAmount(uint v) { uint d = DamageOf(v); return d < 8u ? d / 7f : 0f; }
        /// <summary>0..1 lighten for glint levels 8-15 (0 for clean/char).</summary>
        public static float GlintAmount(uint v) { uint d = DamageOf(v); return d >= 8u ? (d - 7u) / 8f : 0f; }

        /// <summary>Palette ids that are see-through glass (StAssetReader palette).</summary>
        public static bool IsGlassMaterial(int mat) => mat == 112 || mat == 113 || mat == 114 || mat == 134 || mat == 147;

        /// <summary>
        /// Carve a bullet puncture into data at (hx,hy,hz). Fills dirtyIndices with every
        /// written flat index (x + y·w + z·w·h). Returns the removed-cell count;
        /// cracked = survivor cells swapped to crackedMaterial.
        /// </summary>
        public static int Puncture(uint[] data, int w, int h, int d,
            int hx, int hy, int hz, double dirX, double dirY, double dirZ,
            uint seed, float ring1RemoveFraction, int crackedMaterial,
            List<int> dirtyIndices, out int cracked, bool glintMode = false)
        {
            int removed = TouchGlass(data, w, h, d, hx, hy, hz, seed, force: true,
                ring1RemoveFraction, crackedMaterial, dirtyIndices, glintMode) ? 1 : 0;

            // Ring plane ⊥ shot: drop the shot's dominant axis, jitter the other two.
            double ax = dirX < 0 ? -dirX : dirX, ay = dirY < 0 ? -dirY : dirY, az = dirZ < 0 ? -dirZ : dirZ;
            int drop = ax >= ay ? (ax >= az ? 0 : 2) : (ay >= az ? 1 : 2);
            for (int a = -1; a <= 1; a++)
            for (int b = -1; b <= 1; b++)
            {
                if (a == 0 && b == 0) continue;
                int x = hx, y = hy, z = hz;
                if (drop == 0) { y += a; z += b; }
                else if (drop == 1) { x += a; z += b; }
                else { x += a; y += b; }
                if (TouchGlass(data, w, h, d, x, y, z, seed, force: false,
                        ring1RemoveFraction, crackedMaterial, dirtyIndices, glintMode)) removed++;
            }
            cracked = dirtyIndices.Count - removed;
            return removed;
        }

        /// <summary>
        /// Glass cell → AIR (returns true) when forced or the seeded roll passes;
        /// otherwise the cell survives and swaps to the cracked material. Non-glass,
        /// air, and out-of-bounds cells are untouched. Per-cell seed = shot seed XOR
        /// cell index — same shot reproduces the identical scar.
        /// </summary>
        private static bool TouchGlass(uint[] data, int w, int h, int d, int x, int y, int z,
            uint seed, bool force, float ring1RemoveFraction, int crackedMaterial, List<int> dirty,
            bool glintMode = false)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) return false;
            int idx = x + y * w + z * w * h;
            uint v = data[idx];
            if (!IsGlassMaterial((int)(v & MaterialMask))) return false;

            uint cellSeed = seed ^ (uint)idx * 0x9E3779B9u;
            if (force || Hash01(cellSeed) < ring1RemoveFraction)
            {
                data[idx] = 0u;      // punctured — AIR
                dirty.Add(idx);
                return true;
            }
            // wounded — glint mode keeps the glass material and writes the fracture
            // as a bright damage nibble (frosted webbing, 10-13 hash-varied);
            // classic mode swaps to the dedicated cracked material
            data[idx] = glintMode
                ? WithDamage(v, Math.Max(DamageOf(v), 10u + ((cellSeed >> 4) & 3u)))   // re-shots only brighten
                : (v & ~MaterialMask) | (uint)crackedMaterial;
            dirty.Add(idx);
            return false;
        }

        /// <summary>
        /// Resistance classes — every material id maps to one class so live tuning
        /// is nine sliders instead of ~30 ids.
        /// </summary>
        public enum ResClass : int { Glass = 0, Soft, Wood, Stucco, Brick, Stone, Metal, VehicleMetal, Other, Count }

        public static readonly string[] ResClassNames =
            { "glass", "soft", "wood", "stucco", "brick", "stone", "metal", "veh.metal", "other" };

        /// <summary>Authored FACTORY defaults — the locked baseline. RestoreAuthoredDefaults
        /// escapes back to these; tuning lives in the workshop layer below until promoted.</summary>
        public static readonly float[] BaseResistance =
            { 0.05f, 0.10f, 0.45f, 0.35f, 0.50f, 0.60f, 0.80f, 0.90f, 0.50f };

        /// <summary>
        /// WORKSHOP defaults — the in-between layer: pending tuning values that seed the live
        /// table each domain reload and that ResetResistanceTable restores. NOT the shipped
        /// default — when a workshop value locks, promote it into BaseResistance above.
        /// </summary>
        public static readonly float[][] WorkshopRes = BuildWorkshop();
        private static float[][] BuildWorkshop()
        {
            var w = new float[][]
            {
                (float[])BaseResistance.Clone(),
                (float[])BaseResistance.Clone(),
                (float[])BaseResistance.Clone(),
            };
            // pending tuning overrides go here (graduated: wood 0.45 → authored 10/13)
            return w;
        }

        /// <summary>
        /// Model-type profile — a blast resolves one table from the target volume's id:
        /// veh_* / *_veh_* → Vehicle, item_* / *_item_* → Item, everything else →
        /// Building (chars are `char_*` but aren't damageable volumes). Only buildings
        /// register damageable volumes today; the other tables tune ahead of that.
        /// </summary>
        public enum ResProfile : int { Building = 0, Vehicle = 1, Item = 2, Count }
        public static readonly string[] ResProfileNames = { "building", "vehicle", "item" };

        public static int ResProfileOf(string volumeId)
        {
            if (string.IsNullOrEmpty(volumeId)) return (int)ResProfile.Building;
            if (volumeId.StartsWith("veh") || volumeId.Contains("_veh")) return (int)ResProfile.Vehicle;
            if (volumeId.StartsWith("item") || volumeId.Contains("_item")) return (int)ResProfile.Item;
            return (int)ResProfile.Building;
        }

        /// <summary>
        /// LIVE tables — one per model type. The debug HUD's MatRes sub-tabs edit these
        /// in place and GlobalResistScale multiplies every lookup. Static because
        /// resistance is global tuning state shared by Blast, MarchTransmit and the
        /// HUD; the offline selftest never mutates it.
        /// </summary>
        public static readonly float[][] ResTable =
        {
            (float[])WorkshopRes[0].Clone(),
            (float[])WorkshopRes[1].Clone(),
            (float[])WorkshopRes[2].Clone(),
        };
        public static float GlobalResistScale = 1f;

        /// <summary>profile &lt; 0 restores every table AND the global scale — restores the
        /// WORKSHOP baseline, not the authored factory defaults (see RestoreAuthoredDefaults).</summary>
        public static void ResetResistanceTable(int profile = -1)
        {
            if (profile < 0)
            {
                for (int p = 0; p < ResTable.Length; p++)
                    Array.Copy(WorkshopRes[p], ResTable[p], WorkshopRes[p].Length);
                GlobalResistScale = 1f;
            }
            else Array.Copy(WorkshopRes[profile], ResTable[profile], WorkshopRes[profile].Length);
        }

        /// <summary>Escape hatch: both the workshop baseline AND the live table go back
        /// to the authored factory defaults — wipes pending tuning values.</summary>
        public static void RestoreAuthoredDefaults()
        {
            foreach (var t in WorkshopRes) Array.Copy(BaseResistance, t, BaseResistance.Length);
            ResetResistanceTable();
        }

        /// <summary>Promote the live table into the workshop baseline (session-scoped —
        /// statics reset on domain reload; to make a value permanent, move it into
        /// BuildWorkshop or BaseResistance in code).</summary>
        public static void PromoteToWorkshop(int profile)
            => Array.Copy(ResTable[profile], WorkshopRes[profile], ResTable[profile].Length);

        public static ResClass ClassOf(uint mat)
        {
            switch ((int)mat)
            {
                case 112: case 113: case 114: case 134: case 147:          // glass
                    return ResClass.Glass;
                case 115: case 116: case 117: case 124:                   // neon, lamp glow
                case 125: case 126: case 127: case 128:                   // flesh, fabric, hair
                case 137:                                                // water
                    return ResClass.Soft;
                case 106: case 107: case 108:                            // wood
                case 119:                                                // terracotta
                case 120: case 121: case 122: case 129:                  // painted wood
                case 143:                                                // tire rubber
                    return ResClass.Wood;
                case 103: return ResClass.Stucco;                        // stucco/plaster
                case 100: case 105: case 118: return ResClass.Brick;     // brick, cobblestone, tar roof
                case 101: case 102: case 104: return ResClass.Stone;     // stone, concrete, asphalt
                case 109: case 110: case 142: return ResClass.Metal;     // iron, aged metal, chrome
                case 111: case 140: case 141: return ResClass.VehicleMetal; // painted metal, vehicle body
                default: return ResClass.Other;
            }
        }

        /// <summary>
        /// How strongly a material resists blast removal (0 = dies to any blast,
        /// 1 = indestructible). Bullet puncture stays glass-only — this table
        /// is the bomb's world: glass shatters, masonry craters, metal holds.
        /// Live value = ResTable[profile][class] × GlobalResistScale, clamped to 1.
        /// </summary>
        public static float MaterialResistance(uint mat)
            => MaterialResistance(mat, (int)ResProfile.Building);

        public static float MaterialResistance(uint mat, int profile)
            => Math.Min(1f, ResTable[profile >= 0 && profile < ResTable.Length ? profile : 0]
                [(int)ClassOf(mat)] * GlobalResistScale);

        /// <summary>
        /// Spherical blast removal. Every non-air cell within `radius` cells of
        /// (cx,cy,cz) rolls removal at p = power·(1 − dist/radius)·(1 − resistance)·transmit:
        /// full strength at the center, decaying to zero at the shell, tempered by
        /// material — and attenuated by whatever stands between the bomb and the cell.
        /// With occlusion on, a DDA march from the blast center multiplies transmit
        /// by (1 − resistance) per solid cell crossed: open air is free, a pane
        /// barely shields, a brick wall roughly halves the blast per cell, a metal
        /// beam nearly blocks it. The march reads the PRE-BLAST grid (two-phase:
        /// decisions first, writes after), so a wall removed this blast still
        /// shields what was behind it — blast one weakens the wall, blast two
        /// punches through. Surviving glass swaps to crackedMaterial; surviving
        /// non-glass stays untouched. Deterministic: same seed + same grid
        /// reproduces the identical crater.
        /// </summary>
        public static int Blast(uint[] data, int w, int h, int d,
            float cx, float cy, float cz, float radius, float power,
            uint seed, int crackedMaterial,
            List<int> dirty, List<int> removedIdx, List<uint> removedMats, out int cracked,
            bool occlusion = true, int resProfile = 0)
        {
            removedIdx.Clear(); removedMats.Clear();
            var crackedIdx = new List<int>();
            int removed = 0; cracked = 0;
            int x0 = Math.Max(0, (int)Math.Floor(cx - radius)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + radius));
            int y0 = Math.Max(0, (int)Math.Floor(cy - radius)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + radius));
            int z0 = Math.Max(0, (int)Math.Floor(cz - radius)), z1 = Math.Min(d - 1, (int)Math.Ceiling(cz + radius));
            int wh = w * h;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > radius) continue;
                int idx = x + y * w + z * wh;
                uint v = data[idx];
                uint mat = v & MaterialMask;
                if (mat == 0) continue;
                float p = power * (1f - dist / radius) * (1f - MaterialResistance(mat, resProfile));
                float roll = Hash01(seed ^ (uint)idx * 0x9E3779B9u);
                if (roll < p)
                {
                    // would-be removal — only these cells pay for the occlusion march
                    float t = occlusion
                        ? MarchTransmit(data, w, h, d, cx, cy, cz, x + 0.5f, y + 0.5f, z + 0.5f, resProfile)
                        : 1f;
                    if (roll < p * t)
                    {
                        removedIdx.Add(idx);
                        removedMats.Add(mat);
                        removed++;
                        continue;
                    }
                }
                if (IsGlassMaterial((int)mat))
                {
                    crackedIdx.Add(idx);
                    cracked++;
                }
            }
            // phase 2 — apply every write at once (the march sees the intact grid)
            for (int i = 0; i < removedIdx.Count; i++)
            {
                data[removedIdx[i]] = 0u;
                dirty.Add(removedIdx[i]);
            }
            foreach (int idx in crackedIdx)
            {
                data[idx] = (data[idx] & ~MaterialMask) | (uint)crackedMaterial;
                dirty.Add(idx);
            }
            return removed;
        }

        /// <summary>
        /// Bake a damage gradient into the survivors around a wound. `seeds` are
        /// the just-removed (AIR) cells; a 6-connected BFS walks `depth` rings
        /// outward through solid cells, tinting ring r with level depth-r+1 —
        /// darkest at the wound edge, fading out. `glint` selects the lighten
        /// half of the nibble (8+lvl) instead of char. keepFrac&lt;1 dither-skips
        /// cells by index hash (sparse crack webbing vs a solid char band).
        /// Merges by max — overlapping blasts deepen, never repaint weaker.
        /// Writes go into `dirty` for the usual upload path. `visited` is an
        /// optional w·h·d scratch (allocated if null). Returns cells tinted.
        /// `radial` (0-1) attenuates levels by distance from the blast center —
        /// at 1.0 the char fades progressively to nothing at `radius` cells
        /// out; at 0 the ring level alone decides (uniform wound-adjacency).
        /// </summary>
        public static int ApplyDamageGradient(uint[] data, int w, int h, int d,
            List<int> seeds, int depth, bool glint, float keepFrac, uint seed,
            List<int> dirty, byte[] visited = null,
            float cx = 0f, float cy = 0f, float cz = 0f, float radius = 0f, float radial = 0f)
        {
            if (depth <= 0 || seeds == null || seeds.Count == 0) return 0;
            int n = w * h * d;
            if (visited == null || visited.Length < n) visited = new byte[n];
            else Array.Clear(visited, 0, n);

            // front = the wound's solid skin (ring 1): solid neighbors of seeds
            var front = ListPool.Get(); var next = ListPool.Get();
            for (int i = 0; i < seeds.Count; i++) visited[seeds[i]] = 1;
            for (int i = 0; i < seeds.Count; i++)
            {
                int c = seeds[i];
                int z = c / (w * h), rem = c - z * w * h, y = rem / w, x = rem - y * w;
                for (int ax = 0; ax < 6; ax++)
                {
                    int nx = x + D6[ax], ny = y + D6[ax + 6], nz = z + D6[ax + 12];
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= w || ny >= h || nz >= d) continue;
                    int ni = nx + ny * w + nz * w * h;
                    if (visited[ni] == 0 && (data[ni] & MaterialMask) != 0) { visited[ni] = 1; front.Add(ni); }
                }
            }
            int tinted = 0;
            for (int ring = 1; ring <= depth && front.Count > 0; ring++)
            {
                uint lvl = (uint)(depth - ring + 1); if (lvl > 7u) lvl = 7u;
                uint dmg = glint ? 8u + lvl : lvl;
                for (int i = 0; i < front.Count; i++)
                {
                    int c = front[i];
                    int z = c / (w * h), rem = c - z * w * h, y = rem / w, x = rem - y * w;
                    uint eff = lvl;
                    if (radial > 0f && radius > 1e-5f)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
                        float dN = Math.Min((float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / radius, 1f);
                        eff = (uint)Math.Round(lvl * (1f - radial * dN));
                        if (eff > 7u) eff = 7u;
                        dmg = glint ? 8u + eff : eff;
                    }
                    bool keep = keepFrac >= 1f || Hash01(seed ^ (uint)c * 0x9E3779B9u) < keepFrac;
                    if (eff > 0u && keep && dmg > DamageOf(data[c])) { data[c] = WithDamage(data[c], dmg); dirty.Add(c); tinted++; }
                    // untinted cells still propagate the front — a dither hole
                    // doesn't stop the ring from reaching deeper cells
                    for (int ax = 0; ax < 6; ax++)
                    {
                        int nx = x + D6[ax], ny = y + D6[ax + 6], nz = z + D6[ax + 12];
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= w || ny >= h || nz >= d) continue;
                        int ni = nx + ny * w + nz * w * h;
                        if (visited[ni] == 0 && (data[ni] & MaterialMask) != 0) { visited[ni] = 1; next.Add(ni); }
                    }
                }
                var tmp = front; front = next; next = tmp; next.Clear();
            }
            ListPool.Release(front); ListPool.Release(next);
            return tinted;
        }
        static readonly int[] D6 = { -1, 1, 0, 0, 0, 0, 0, 0, -1, 1, 0, 0, 0, 0, 0, 0, -1, 1 };
        static class ListPool
        {
            static readonly Stack<List<int>> pool = new Stack<List<int>>();
            public static List<int> Get() => pool.Count > 0 ? pool.Pop() : new List<int>();
            public static void Release(List<int> l) { l.Clear(); pool.Push(l); }
        }

        /// <summary>
        /// Amanatides–Woo march from the blast center to a target cell, attenuating
        /// Beer–Lambert style: transmit *= (1 − resistance)^(path length inside the
        /// cell, in cell units). An axis-aligned crossing still costs exactly one
        /// cell of material (existing wall tuning unchanged — brick = ×0.5 per
        /// cell), but oblique rays pay for the distance they actually travel
        /// instead of a full cell per face clipped, so equal-shielding contours
        /// are spheres rather than taxicab diamonds. The target cell itself is
        /// excluded (its own resistance already factors the roll). Early-outs at
        /// 2% transmission — deeper shielding is darkness.
        /// </summary>
        public static float MarchTransmit(uint[] data, int w, int h, int d,
            float cx, float cy, float cz, float tx, float ty, float tz, int resProfile = 0)
        {
            int x = (int)Math.Floor(cx), y = (int)Math.Floor(cy), z = (int)Math.Floor(cz);
            float dx = tx - cx, dy = ty - cy, dz = tz - cz;
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            int sz = dz > 0 ? 1 : (dz < 0 ? -1 : 0);
            float tdx = sx != 0 ? Math.Abs(1f / dx) : float.MaxValue;
            float tdy = sy != 0 ? Math.Abs(1f / dy) : float.MaxValue;
            float tdz = sz != 0 ? Math.Abs(1f / dz) : float.MaxValue;
            float tmx = BoundT(cx, dx, sx), tmy = BoundT(cy, dy, sy), tmz = BoundT(cz, dz, sz);
            float transmit = 1f;
            int wh = w * h, guard = w + h + d + 8;
            while (guard-- > 0)
            {
                float tEnter = Math.Min(tmx, Math.Min(tmy, tmz));   // this cell's exit boundary
                if (tEnter >= 1f) break;                            // the target lies inside this cell
                if (tmx <= tmy && tmx <= tmz) { x += sx; tmx += tdx; }
                else if (tmy <= tmz)          { y += sy; tmy += tdy; }
                else                          { z += sz; tmz += tdz; }
                if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) break;
                float tNext = Math.Min(tmx, Math.Min(tmy, tmz));    // the entered cell's exit
                if (tNext >= 1f) break;                             // entered the target cell — excluded
                uint m = data[x + y * w + z * wh] & MaterialMask;
                if (m != 0)
                {
                    transmit *= (float)Math.Pow(1f - MaterialResistance(m, resProfile), (tNext - tEnter) * len);
                    if (transmit < 0.02f) break;
                }
            }
            return transmit;
        }

        /// <summary>Parametric t (0..1 along the march) of the first cell-boundary crossing on an axis.</summary>
        private static float BoundT(float c, float dir, int step)
        {
            if (step == 0) return float.MaxValue;
            float next = step > 0 ? (float)Math.Floor(c) + 1f : (float)Math.Ceiling(c) - 1f;
            float t = (next - c) / dir;
            return t < 0f ? 0f : t;
        }

        /// <summary>
        /// Count solid cells the straight blast→target ray passes through before
        /// reaching the target's cell — the exposure "shadow" term. Each
        /// intervening voxel absorbs a fixed share of char (NOT material
        /// resistance — exposure is geometry only). Air transmits fully, so a
        /// blown window opening delivers scorch to whatever it reveals. The
        /// target cell is excluded (its own surface is what receives the char).
        /// Same Amanatides–Woo DDA as MarchTransmit.
        /// </summary>
        public static int MarchSolidCount(uint[] data, int w, int h, int d,
            float cx, float cy, float cz, float tx, float ty, float tz)
        {
            int x = (int)Math.Floor(cx), y = (int)Math.Floor(cy), z = (int)Math.Floor(cz);
            int gx = (int)Math.Floor(tx), gy = (int)Math.Floor(ty), gz = (int)Math.Floor(tz);
            float dx = tx - cx, dy = ty - cy, dz = tz - cz;
            int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            int sz = dz > 0 ? 1 : (dz < 0 ? -1 : 0);
            float tdx = sx != 0 ? Math.Abs(1f / dx) : float.MaxValue;
            float tdy = sy != 0 ? Math.Abs(1f / dy) : float.MaxValue;
            float tdz = sz != 0 ? Math.Abs(1f / dz) : float.MaxValue;
            float tmx = BoundT(cx, dx, sx), tmy = BoundT(cy, dy, sy), tmz = BoundT(cz, dz, sz);
            int wh = w * h, solids = 0, guard = w + h + d + 8;
            while (guard-- > 0)
            {
                float tEnter = Math.Min(tmx, Math.Min(tmy, tmz));
                if (tEnter >= 1f) break;                            // the target lies inside this cell
                if (tmx <= tmy && tmx <= tmz) { x += sx; tmx += tdx; }
                else if (tmy <= tmz)          { y += sy; tmy += tdy; }
                else                          { z += sz; tmz += tdz; }
                if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) break;
                if (x == gx && y == gy && z == gz) break;           // entered the target cell — don't count it
                if ((data[x + y * w + z * wh] & MaterialMask) != 0) solids++;
            }
            return solids;
        }

        /// <summary>
        /// Bake blast char as a radial EXPOSURE field: every solid cell inside
        /// `radius + reach` evaluates its own line of sight to the blast —
        ///   level = round(7 · distFactor · absorb^solids)
        /// where distFactor = 1 − radial·smoothstep(dist/(radius+reach)) holds
        /// near-max through the doom zone then S-tapers sharply at the rim
        /// (`radial` 0-1 blends the distance term in/out), and each solid cell
        /// the ray crosses transmits `absorb` (the char "shadow"). Material
        /// resistance plays no part — a wood wall and a steel wall shadow the
        /// same; only distance + intervening voxels decide. Doom-adjacency is
        /// implied, not required: the cavity is air, so the wound's rim cells
        /// see 0 intervening solids and char hardest, while a survivor across
        /// an opening still gets painted (windows transmit to sills). Merges
        /// by max; writes go into `dirty`. Returns cells tinted.
        /// </summary>
        public static int ApplyDamageExposure(uint[] data, int w, int h, int d,
            float cx, float cy, float cz, float radiusCells, float reachCells,
            float radial, float absorb, List<int> dirty)
        {
            float range = radiusCells + reachCells;
            if (range <= 0f || absorb <= 0f) return 0;
            int x0 = Math.Max(0, (int)Math.Floor(cx - range)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + range));
            int y0 = Math.Max(0, (int)Math.Floor(cy - range)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + range));
            int z0 = Math.Max(0, (int)Math.Floor(cz - range)), z1 = Math.Min(d - 1, (int)Math.Ceiling(cz + range));
            int wh = w * h, tinted = 0;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int idx = x + y * w + z * wh;
                uint v = data[idx];
                if ((v & MaterialMask) == 0) continue;               // cavity — no char on air
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > range) continue;
                float t = dist / range;
                float ss = t * t * (3f - 2f * t);                     // smoothstep — S-curve tail-off
                float distFactor = 1f - radial * ss;
                if (distFactor <= 0f) continue;
                float exposure = distFactor;
                int solids = MarchSolidCount(data, w, h, d, cx, cy, cz, x + 0.5f, y + 0.5f, z + 0.5f);
                if (solids > 0) exposure *= (float)Math.Pow(absorb, solids);
                int lvl = (int)Math.Round(7f * exposure);
                if (lvl <= 0) continue;
                if (lvl > 7) lvl = 7;
                if ((uint)lvl > DamageOf(v)) { data[idx] = WithDamage(v, (uint)lvl); dirty.Add(idx); tinted++; }
            }
            return tinted;
        }

        /// <summary>
        /// Structural connectivity: BFS-flood from every solid cell in the y==0 anchor
        /// layer. A cell survives iff the flood reaches it — structural (non-glass)
        /// cells anchor ALL solid neighbors, glass anchors only glass (a pane hangs
        /// off its frame, but a brick can't rest on a pane). Unreached solid cells
        /// are detached: grouped into 6-connected islands (all materials connect for
        /// grouping — a pane embedded in a falling wall rides with it) and appended
        /// to detachedIdx/detachedMats, with islandEnds marking each island's
        /// exclusive end index. `visited` is an optional w·h·d scratch
        /// (bit1 = anchored, bit2 = islanded). Returns the detached cell count.
        /// </summary>
        public static int FindDetached(uint[] data, int w, int h, int d,
            List<int> detachedIdx, List<uint> detachedMats, List<int> islandEnds,
            byte[] visited = null, System.Func<int, bool> treatAsAir = null)
        {
            detachedIdx.Clear(); detachedMats.Clear(); islandEnds.Clear();
            int n = w * h * d;
            if (visited == null || visited.Length < n) visited = new byte[n];
            else Array.Clear(visited, 0, n);
            var queue = new Queue<int>();
            int wh = w * h;

            // anchor pass — seed every solid cell on the ground layer
            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int idx = x + z * wh;
                if ((data[idx] & MaterialMask) != 0 && !(treatAsAir != null && treatAsAir(idx))) { visited[idx] = 1; queue.Enqueue(idx); }
            }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int cz = c / wh, crem = c - cz * wh, cy = crem / w, cx = crem - cy * w;
                bool cStruct = !IsGlassMaterial((int)(data[c] & MaterialMask));
                for (int a = 0; a < 6; a++)
                {
                    int nx = cx, ny = cy, nz = cz;
                    if (a == 0) nx++; else if (a == 1) nx--;
                    else if (a == 2) ny++; else if (a == 3) ny--;
                    else if (a == 4) nz++; else nz--;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= w || ny >= h || nz >= d) continue;
                    int ni = nx + ny * w + nz * wh;
                    if (visited[ni] != 0) continue;
                    uint nm = data[ni] & MaterialMask;
                    if (nm == 0 || (treatAsAir != null && treatAsAir(ni))) continue;
                    if (!cStruct && !IsGlassMaterial((int)nm)) continue;   // glass can't anchor structure
                    visited[ni] = 1;
                    queue.Enqueue(ni);
                }
            }

            // island pass — group everything the anchor flood never reached
            for (int idx = 0; idx < n; idx++)
            {
                if (visited[idx] != 0 || (data[idx] & MaterialMask) == 0) continue;
                visited[idx] = 2; queue.Enqueue(idx);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    detachedIdx.Add(c); detachedMats.Add(data[c] & MaterialMask);
                    int cz = c / wh, crem = c - cz * wh, cy = crem / w, cx = crem - cy * w;
                    for (int a = 0; a < 6; a++)
                    {
                        int nx = cx, ny = cy, nz = cz;
                        if (a == 0) nx++; else if (a == 1) nx--;
                        else if (a == 2) ny++; else if (a == 3) ny--;
                        else if (a == 4) nz++; else nz--;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= w || ny >= h || nz >= d) continue;
                        int ni = nx + ny * w + nz * wh;
                        if (visited[ni] != 0 || (data[ni] & MaterialMask) == 0) continue;
                        visited[ni] = 2;
                        queue.Enqueue(ni);
                    }
                }
                islandEnds.Add(detachedIdx.Count);
            }
            return detachedIdx.Count;
        }

        /// <summary>
        /// A pre-computed explosion: every cell the blast will write, grouped by
        /// wavefront pass, with the detached islands appended as the final front.
        /// The plan IS the delta — applying writeIdx/writeVal to the source grid
        /// in order reproduces the simulated end state exactly, so detonation
        /// playback is pure scheduling (writes per frame, no hitches).
        /// </summary>
        public sealed class BlastPlan
        {
            public int passes;
            public readonly List<int> writeIdx = new();     // every written cell, grouped by pass
            public readonly List<uint> writeVal = new();    // final packed value (0 = AIR)
            public readonly List<uint> writeMat = new();    // ORIGINAL material (shard tint; AIR entries only)
            public readonly List<int> passEnds = new();     // writeIdx end index per pass
            public readonly List<int> islandEnds = new();   // absolute end idx per detached island
            public int detachedStart;                       // writeIdx[detachedStart..] = collapse writes
            public int totalRemoved;
        }

        /// <summary>
        /// Multi-pass expanding shock front on a SCRATCH clone — pass i detonates at
        /// radius·(i+1)/K so interior cells re-roll every pass (near-certain kill)
        /// while fringe cells see the front once. Because each pass mutates the
        /// scratch, occlusion frees as inner structure dies: the wave accelerates
        /// through cleared space. Connectivity (optional) runs on the final state —
        /// islands append as the last wavefront. Deterministic per baseSeed.
        /// </summary>
        public static BlastPlan SimulateBlast(uint[] scratch, int w, int h, int d,
            float cx, float cy, float cz, float radius, float power,
            uint baseSeed, int passes, int crackedMaterial,
            bool occlusion, bool dropUnsupported, byte[] visitedScratch = null, int resProfile = 0)
        {
            var plan = new BlastPlan { passes = passes };
            var dirty = new List<int>(); var ri = new List<int>(); var rm = new List<uint>();
            var matOf = new Dictionary<int, uint>();
            for (int i = 0; i < passes; i++)
            {
                float passR = radius * (i + 1f) / passes;
                dirty.Clear(); ri.Clear(); rm.Clear();
                Blast(scratch, w, h, d, cx, cy, cz, passR, power, baseSeed + (uint)i,
                    crackedMaterial, dirty, ri, rm, out _, occlusion, resProfile);
                matOf.Clear();
                for (int j = 0; j < ri.Count; j++) matOf[ri[j]] = rm[j];
                var removedSet = new HashSet<int>(ri);
                for (int j = 0; j < dirty.Count; j++)
                {
                    int idx = dirty[j];
                    plan.writeIdx.Add(idx);
                    if (removedSet.Contains(idx))
                    {
                        plan.writeVal.Add(0u);
                        plan.writeMat.Add(matOf[idx]);
                        plan.totalRemoved++;
                    }
                    else
                    {
                        plan.writeVal.Add(scratch[idx]);   // Blast already wrote the cracked value
                        plan.writeMat.Add(0u);
                    }
                }
                plan.passEnds.Add(plan.writeIdx.Count);
            }
            plan.detachedStart = plan.writeIdx.Count;
            if (dropUnsupported)
            {
                var detI = new List<int>(); var detM = new List<uint>(); var detE = new List<int>();
                FindDetached(scratch, w, h, d, detI, detM, detE, visitedScratch);
                for (int j = 0; j < detI.Count; j++)
                {
                    plan.writeIdx.Add(detI[j]);
                    plan.writeVal.Add(0u);
                    plan.writeMat.Add(detM[j]);
                }
                foreach (int e in detE) plan.islandEnds.Add(plan.detachedStart + e);
                plan.totalRemoved += detI.Count;
            }
            return plan;
        }

        /// <summary>lowbias32 integer hash → [0,1). Same constants as ProjectileSystem.Hash01.</summary>
        public static float Hash01(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352dU;
                x ^= x >> 15; x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777216f;
            }
        }
    }
}
