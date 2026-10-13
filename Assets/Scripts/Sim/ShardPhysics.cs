using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>Ray query the debris sweep runs against: VoxelCollisionWorld.Raycast in game, a SolidVolumeSet in the offline self-test. dir is a unit vector.</summary>
    public delegate bool ShardRaycast(Vector3 origin, Vector3 dir, float maxDistance, out VoxelRayHit hit);

    /// <summary>Free = moved the whole step � Contact = stopped on a face and bounced (read hit) � Embedded = the shard's center is already inside a solid cell (not moved).</summary>
    public enum ShardStep { Free, Contact, Embedded }

    /// <summary>
    /// Debris physics against the voxel world � pure functions, no scene access, so Tools/dda_selftest drives
    /// them offline. A shard is a small sphere (radius = half its cube edge) swept along its velocity by ONE ray
    /// from its center: the DDA visits every cell the step crosses, so a fast shard cannot tunnel through a
    /// 1-voxel floor. A hit stops it a radius off the face and reflects its velocity about the face normal �
    /// a floor bounce, a roof bounce and a wall bounce are the same code, only the normal differs.
    ///
    /// A resting shard becomes rubble: <see cref="Deposit"/> writes its material back into the AIR cells it
    /// settled on (never over solid), so debris is the very voxels the blast removed, put back lower down.
    /// </summary>
    public static class ShardPhysics
    {
        /// <summary>Gap kept between a shard and the face it touches (m). Far below a voxel, so the shard center stays in the air cell in front of the face.</summary>
        public const float Skin = 0.0005f;

        /// <summary>
        /// Advance one airborne shard by dt: gravity, then a swept move that stops at the first voxel face.
        /// restitution scales the normal component of the bounce, friction the tangential one.
        /// hit is only meaningful when Contact is returned.
        /// </summary>
        public static ShardStep Step(ref Vector3 pos, ref Vector3 vel, float radius, float dt, float gravity,
            float restitution, float friction, ShardRaycast cast, out VoxelRayHit hit)
        {
            hit = default;
            vel.y -= gravity * dt;
            Vector3 step = vel * dt;
            float len = step.magnitude;
            if (len < 1e-7f) return ShardStep.Free;
            Vector3 dir = step / len;

            // look 2r past the step: an oblique face meets the sphere before the center is within r of the face plane
            if (!cast(pos, dir, len + 2f * radius, out hit)) { pos += step; return ShardStep.Free; }
            if (hit.normal.sqrMagnitude < 0.5f) return ShardStep.Embedded;
            float cosInc = Mathf.Max(Vector3.Dot(-dir, hit.normal), 0.5f);
            if (hit.distance - radius / cosInc > len) { pos += step; return ShardStep.Free; }

            pos = hit.point + hit.normal * (radius + Skin);
            float vn = Vector3.Dot(vel, hit.normal);
            if (vn < 0f) vel = (vel - hit.normal * vn) * friction - hit.normal * (vn * restitution);
            return ShardStep.Contact;
        }

        /// <summary>
        /// Cell box a resting shard fills: centered on the shard horizontally, bottom aligned to the surface it
        /// sits on. cellPos = shard center in volume cell coordinates, sizeCells = its cube edge in cells
        /// (dust = 1 cell, a collapsed-wall chunk = a block of cells).
        /// </summary>
        public static void DepositBox(Vector3 cellPos, float sizeCells, int maxCells,
            out int x0, out int y0, out int z0, out int n)
        {
            n = Mathf.Clamp(Mathf.RoundToInt(sizeCells), 1, Mathf.Max(1, maxCells));
            x0 = Mathf.RoundToInt(cellPos.x - n * 0.5f);
            z0 = Mathf.RoundToInt(cellPos.z - n * 0.5f);
            y0 = Mathf.RoundToInt(cellPos.y - sizeCells * 0.5f);
        }

        /// <summary>
        /// Write mat into the AIR cells of the n� box at (x0,y0,z0), clipped to the grid � solid cells are never
        /// overwritten. Written indices are appended to dirty; returns how many cells were written.
        /// </summary>
        public static int Deposit(uint[] data, int w, int h, int d, int x0, int y0, int z0, int n, uint mat, List<int> dirty)
        {
            int written = 0;
            int zEnd = Mathf.Min(z0 + n, d), yEnd = Mathf.Min(y0 + n, h), xEnd = Mathf.Min(x0 + n, w);
            for (int z = Mathf.Max(z0, 0); z < zEnd; z++)
                for (int y = Mathf.Max(y0, 0); y < yEnd; y++)
                    for (int x = Mathf.Max(x0, 0); x < xEnd; x++)
                    {
                        int idx = x + y * w + z * w * h;
                        if ((data[idx] & VoxelDda.MaterialMask) != 0u) continue;
                        data[idx] = mat;
                        dirty.Add(idx);
                        written++;
                    }
            return written;
        }

        /// <summary>Spatial aggregate for debris emission: a cluster of removed cells becomes ONE shard
        /// that carries the cluster's cell COUNT as mass - it flies as a small capped cube but on rest
        /// spills that many cells back as a heap (render size is not the deposit mass). repIdx is the
        /// removed cell nearest the centroid - the shard spawns THERE, never at the raw centroid which
        /// can sit inside surviving solid and embed. mat2 is the first non-majority material seen with
        /// othCells its true share of the bucket, so a boundary bucket's mound blends honestly instead
        /// of lying 100% the winner's color.</summary>
        public struct DebrisBucket
        {
            public int sx, sy, sz, count, matCount, repIdx, majCells, othCells;
            public float repDist;
            public uint mat, mat2;
        }

        /// <summary>
        /// Hash the cells in idx[start..end) into cs-pitch spatial buckets (x+y*w+z*w*h indexing).
        /// vals (optional) filters to AIR writes - cracked cells stay in place and never spawn debris.
        /// Each bucket accumulates a centroid (sx/count) and a majority material (Boyer-Moore vote),
        /// then a second pass picks the removed cell nearest the centroid as repIdx (a guaranteed-AIR
        /// spawn point for the shard). cs is the chunkiness knob: bucket count scales as cells / (cs^3 * fill).
        /// </summary>
        public static void BuildBuckets(List<int> idx, List<uint> mats, int w, int h,
            int start, int end, int cs, Dictionary<int, DebrisBucket> buckets, List<uint> vals = null)
        {
            buckets.Clear();
            for (int i = start; i < end; i++)
            {
                if (vals != null && vals[i] != 0u) continue;
                int idxx = idx[i];
                int z = idxx / (w * h), rem = idxx - z * w * h, y = rem / w, x = rem - y * w;
                int key = ((x / cs) << 20) | ((y / cs) << 10) | (z / cs);
                if (!buckets.TryGetValue(key, out var b)) b = new DebrisBucket { repIdx = -1, repDist = float.MaxValue };
                b.sx += x; b.sy += y; b.sz += z; b.count++;
                if (b.matCount == 0) { b.mat = mats[i]; b.matCount = 1; }
                else if (mats[i] == b.mat) b.matCount++; else b.matCount--;
                buckets[key] = b;
            }
            for (int i = start; i < end; i++)                     // pass 2: nearest removed cell to each centroid
            {
                if (vals != null && vals[i] != 0u) continue;
                int idxx = idx[i];
                int z = idxx / (w * h), rem = idxx - z * w * h, y = rem / w, x = rem - y * w;
                int key = ((x / cs) << 20) | ((y / cs) << 10) | (z / cs);
                var b = buckets[key];
                float dx = x - b.sx / (float)b.count, dy = y - b.sy / (float)b.count, dz = z - b.sz / (float)b.count;
                float dd = dx * dx + dy * dy + dz * dz;
                if (dd < b.repDist) { b.repDist = dd; b.repIdx = idxx; }
                if (mats[i] == b.mat) b.majCells++;                       // exact majority share (pass-1 count was a vote strength)
                else { b.othCells++; if (b.mat2 == 0u) b.mat2 = mats[i]; } // all non-majority mass wears the runner-up's color
                buckets[key] = b;
            }
        }

        /// <summary>
        /// Write rubble into up to `maxWrite` AIR cells as a mound growing up from base cell (cx,cy,cz):
        /// square layers whose half-extent starts at ceil(cbrt(3*mass/8)) (capped by maxRad) and shrinks
        /// one cell every two layers, so a big spill reads as a pile, not a cube. `mass` sizes the
        /// footprint (pass the shard's ORIGINAL mass so a mid-pour call keeps the same mound);
        /// `maxWrite` caps this call's writes - re-calling with a fresh budget resumes the pile, since
        /// the ring order is deterministic and already-written cells are now solid. Cells blend `mat`
        /// and `mat2` at the bucket's true share via a deterministic hash of the cell index, and the
        /// outermost ring of each layer skips ~1-in-8 cells so the rim reads as scree, not a machined
        /// edge. Each written cell drops down its column to the lowest open cell — rubble fills the
        /// crater the blast just carved before stacking a pile on top. Solid cells are never
        /// overwritten; the mound is clipped to the grid.
        /// </summary>
        public static int DepositHeap(uint[] data, int w, int h, int d,
            int cx, int cy, int cz, int mass, int maxRad, uint mat, uint mat2, float mix2,
            int maxWrite, List<int> dirty)
        {
            int written = 0;
            uint share2 = mix2 <= 0f || mat2 == 0u ? 0u : (uint)(mix2 * 16777216f);
            int r0 = Mathf.Min(maxRad, Mathf.CeilToInt(Mathf.Pow(mass * 0.375f, 1f / 3f)));
            for (int layer = 0; written < maxWrite && cy + layer < h; layer++)
            {
                int r = r0 - layer / 2;
                if (r < 0) break;
                int yy = cy + layer;
                for (int rr = 0; rr <= r && written < maxWrite; rr++)
                    for (int dz = -rr; dz <= rr && written < maxWrite; dz++)
                        for (int dx = -rr; dx <= rr && written < maxWrite; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != rr) continue;
                            int x = cx + dx, z = cz + dz;
                            if (x < 0 || x >= w || z < 0 || z >= d) continue;
                            int idx = x + yy * w + z * w * h;
                            if ((data[idx] & VoxelDda.MaterialMask) != 0u) continue;
                            uint hsh = (uint)idx * 2654435761u;
                            if (rr == r && r > 1 && (hsh & 7u) == 0u) continue;   // ragged rim
                            // form-fit: a cell whose column has open air below drops to the lowest open
                            // cell - rubble fills the crater the blast carved before stacking on top
                            int fy = yy;
                            while (fy > 0 && (data[x + (fy - 1) * w + z * w * h] & VoxelDda.MaterialMask) == 0u) fy--;
                            if (fy != yy) { idx = x + fy * w + z * w * h; hsh = (uint)idx * 2654435761u; }
                            data[idx] = ((hsh >> 8) & 0xFFFFFFu) <= share2 ? mat2 : mat;
                            dirty.Add(idx);
                            written++;
                        }
            }
            return written;
        }

        /// <summary>
        /// Angle-of-repose relaxation for deposited rubble. Only cells in `rubble` (the set of indices
        /// DepositHeap wrote) can move — structural voxels never slide. An unsupported cell (air
        /// directly below) falls straight down one step regardless of what's above it — rubble
        /// tucked under an overhang drips out instead of freezing into stalactites, and rubble
        /// whose support is later destroyed crumbles. A supported rubble cell slides one step
        /// down into the first open lateral-down neighbor, tried in a hash-rotated order so the
        /// spill has no directional bias — max stable slope is therefore 45°. The lateral-down
        /// target test is the only gate: interior cells of a settled mass have no open target so
        /// they stay locked, but a cell under overburden with a void beside it WILL squirm out —
        /// pillar sides shed top-to-bottom instead of freezing floor-to-ceiling.
        /// Moves are single steps and only ever descend, so the pass converges; `queue` holds candidate
        /// indices (written cells + re-triggered cells) and `maxMoves` bounds one call so a big collapse
        /// relaxes visibly over several frames instead of snapping. Vacated positions re-expose the cell
        /// below and the lateral cells one-up that can now slide in — those are re-queued automatically.
        /// Returns moves made; dirty receives both vacated and filled indices for the upload batch.
        /// </summary>
        public static int Slump(uint[] data, int w, int h, int d,
            HashSet<int> rubble, List<int> queue, int maxMoves, List<int> dirty)
        {
            int moves = 0, qi = 0;
            while (qi < queue.Count && moves < maxMoves)
            {
                int idx = queue[qi++];
                if (!rubble.Contains(idx)) continue;                                  // structural or already moved away
                if ((data[idx] & VoxelDda.MaterialMask) == 0u) { rubble.Remove(idx); continue; }  // re-blasted to air
                int z = idx / (w * h), rem = idx - z * w * h, y = rem / w, x = rem - y * w;
                if (y == 0) continue;
                if ((data[idx - w] & VoxelDda.MaterialMask) == 0u)                    // unsupported - falls straight down
                {                                                                     // (a slide can tuck a cell under an
                    uint m = data[idx];                                               // overhang; trapped under a ceiling it
                    data[idx] = 0u; data[idx - w] = m;                                // must drip, not freeze into a stalactite)
                    rubble.Remove(idx); rubble.Add(idx - w);
                    dirty.Add(idx); dirty.Add(idx - w);
                    moves++;
                    queue.Add(idx - w);                                               // keeps falling while unsupported
                    queue.Add(idx + w);                                               // cell above the vacancy may drop too
                    if (x + 1 < w) queue.Add(idx + 1 + w);
                    if (x > 0) queue.Add(idx - 1 + w);
                    if (z + 1 < d) queue.Add(idx + w * h + w);
                    if (z > 0) queue.Add(idx - w * h + w);
                    continue;
                }
                // no buried check: a cell that genuinely has an open lateral-down target slides
                // even under overburden — pillar sides shed top-to-bottom instead of freezing
                uint hsh = (uint)idx * 2654435761u;
                int start = (int)(hsh & 3u);
                for (int k = 0; k < 4; k++)
                {
                    int dir = (start + k) & 3;
                    int nx = x + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                    int nz = z + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                    if (nx < 0 || nx >= w || nz < 0 || nz >= d) continue;
                    int tidx = idx + (dir == 0 ? 1 : dir == 1 ? -1 : dir == 2 ? w * h : -w * h) - w;
                    if ((data[tidx] & VoxelDda.MaterialMask) != 0u) continue;
                    uint m = data[idx];
                    data[idx] = 0u; data[tidx] = m;
                    rubble.Remove(idx); rubble.Add(tidx);
                    dirty.Add(idx); dirty.Add(tidx);
                    moves++;
                    queue.Add(tidx);                                                  // may keep sliding downhill
                    queue.Add(idx - w);                                               // cell under the vacancy is exposed now
                    if (y + 1 < h) queue.Add(idx + w);                                // cell above may have lost support
                    if (x + 1 < w) queue.Add(idx + 1 + w);                            // lateral cells one-up can
                    if (x > 0) queue.Add(idx - 1 + w);                                // now slide into the vacancy
                    if (z + 1 < d) queue.Add(idx + w * h + w);
                    if (z > 0) queue.Add(idx - w * h + w);
                    break;                                                            // one slide per pop
                }
            }
            queue.RemoveRange(0, qi);                                                 // consumed prefix; tail waits for the next tick
            return moves;
        }
    }
}
