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

        /// <summary>An in-grid ballistic cell: it never left the voxel array — it IS the
        /// particle. vel/acc live in CELL units (cells/sec, cells); vox is the packed voxel
        /// payload, doubling as the staleness guard (a re-blast or a pour into its slot
        /// makes data[idx] != vox and the entry drops out).</summary>
        public sealed class FallCell
        {
            public Vector3 vel;
            public Vector3 acc;
            public uint vox;
            // domino members: partner >= 0 makes this cell half of a 2-cell piece —
            // BOTH dict entries (head + member) point at this ONE instance, so the
            // pair shares vel/acc by reference (a real entity, not correlated cells).
            // vox2/headIdx are only meaningful while partner >= 0.
            public int partner = -1;
            public uint vox2;
            public int headIdx = -1;
        }

        /// <summary>A cell that stepped out of the volume bounds — idx is its last in-grid
        /// index (already cleared), vel its cell-units velocity for the shard handoff.</summary>
        public struct FallEscape { public int idx; public Vector3 vel; public uint vox; }

        /// <summary>
        /// One tick of cellular collapse. `queue` holds the live cells' current indices
        /// (-1 tombstones — compacted when they dominate); a moved cell keeps its slot,
        /// so iteration stays allocation-free. Per tick each cell integrates gravity +
        /// drag, spends per-axis accumulators on single-cell steps into OPEN cells only
        /// (max one step per axis — at 120 fps real blast speeds never exceed ~1
        /// voxel/frame, so there is no tunneling to guard against). Axis order rotates by
        /// index hash to avoid a directional bias. Collision semantics: an open target
        /// moves the cell; a target occupied by another falling cell just waits (retry
        /// next tick — stacked cells rain down as a train); static solid absorbs that
        /// axis's momentum. A cell that can no longer move AND has static support below
        /// AND is slower than landSpeed converts to rubble + joins the slump queue.
        /// A step that would leave the volume bounds (laterally or through the roof)
        /// escapes: the grid cell is cleared, the falling entry is removed, and a
        /// FallEscape (last index + velocity + voxel) is reported via `escaped` —
        /// the caller converts it to a flying voxel, the one way debris legitimately
        /// leaves the grid. Stepping below the floor row is not an escape — it is the
        /// ground plane. Returns moves made; dirty receives vacated/filled indices.
        /// </summary>
        public static int StepFalling(uint[] data, int w, int h, int d,
            Dictionary<int, FallCell> falling, List<int> queue,
            HashSet<int> rubble, List<int> slumpQueue, List<int> dirty, List<FallEscape> escaped,
            float dt, float gravCells, float drag, float landSpeed, int maxMoves,
            bool deposit = true)
        {
            int moves = 0, tombs = 0, wh = w * h;
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int idx = queue[qi];
                if (idx < 0) { tombs++; continue; }
                if (!falling.TryGetValue(idx, out var fc) || data[idx] != fc.vox)
                {                                                          // stale: landed already, re-blasted, or poured over
                    falling.Remove(idx);
                    if (fc != null && fc.partner >= 0 && fc.headIdx == idx)
                    {   // a domino HEAD died externally — don't orphan the member (it owns no
                        // queue slot and would freeze as a floating voxel): adopt it as a
                        // single with a momentum snapshot, or drop it if its cell died too
                        int m = fc.partner;
                        if (m >= 0 && m < data.Length && data[m] == fc.vox2)
                        {
                            falling[m] = new FallCell { vel = fc.vel, acc = fc.acc, vox = fc.vox2 };
                            queue.Add(m);
                        }
                        else falling.Remove(m);
                    }
                    queue[qi] = -1; tombs++; continue;
                }
                fc.vel.y -= gravCells * dt;
                fc.vel *= Mathf.Max(0f, 1f - drag * dt);
                fc.acc += fc.vel * dt;

                if (fc.partner >= 0)
                {
                    // ── domino: a real 2-cell entity ─────────────────────────
                    // Only the head's queue slot steps the pair (the member owns
                    // no slot until it splits). Per axis, both members' targets
                    // are evaluated together: both free → move as a unit; a
                    // falling sibling blocking either → wait; static blocking
                    // BOTH → absorb the axis; static blocking ONE → the piece
                    // fractures there — the stuck member keeps its support, the
                    // free member keeps the momentum and flies on as a single.
                    if (idx != fc.headIdx) { queue[qi] = -1; tombs++; continue; }
                    int m = fc.partner;
                    if (m < 0 || m >= data.Length || data[m] != fc.vox2)
                    {   // member's cell was destroyed (re-blast / pour) — head goes single
                        falling.Remove(m); fc.partner = -1; fc.vox2 = 0; fc.headIdx = -1;
                        continue;
                    }
                    int dhx = 0, dhy = 0, dhz = 0, dmx = 0, dmy = 0, dmz = 0;
                    {
                        int zz = idx / wh, rr = idx - zz * wh, yy = rr / w, xx = rr - yy * w;
                        dhx = xx; dhy = yy; dhz = zz;
                        zz = m / wh; rr = m - zz * wh; yy = rr / w; xx = rr - yy * w;
                        dmx = xx; dmy = yy; dmz = zz;
                    }
                    uint dhsh = (uint)idx * 2654435761u;
                    int da0 = (int)((dhsh >> 12) % 3u);
                    bool dmoved = false, dsplit = false;
                    for (int k = 0; k < 3 && moves < maxMoves; k++)
                    {
                        int axis = (da0 + k) % 3;
                        float acc = axis == 0 ? fc.acc.x : axis == 1 ? fc.acc.y : fc.acc.z;
                        if (Mathf.Abs(acc) < 1f) continue;
                        int step = acc > 0f ? 1 : -1;
                        int ox = axis == 0 ? step : 0, oy = axis == 1 ? step : 0, oz = axis == 2 ? step : 0;
                        int hx2 = dhx + ox, hy2 = dhy + oy, hz2 = dhz + oz;
                        int mx2 = dmx + ox, my2 = dmy + oy, mz2 = dmz + oz;
                        bool hEsc = hx2 < 0 || hx2 >= w || hz2 < 0 || hz2 >= d || hy2 >= h;   // lateral/roof = escape
                        bool mEsc = mx2 < 0 || mx2 >= w || mz2 < 0 || mz2 >= d || my2 >= h;
                        if (hEsc || mEsc)
                        {   // a member left the bounds → it escapes alone; survivor goes single
                            int escIdx = hEsc ? idx : m;
                            uint escVox = hEsc ? fc.vox : fc.vox2;
                            data[escIdx] = 0u; dirty.Add(escIdx);
                            escaped.Add(new FallEscape { idx = escIdx, vel = fc.vel, vox = escVox });
                            falling.Remove(escIdx);
                            fc.vox = hEsc ? fc.vox2 : fc.vox;   // survivor's material
                            fc.vox2 = 0; fc.partner = -1; fc.headIdx = -1;
                            if (hEsc) { queue.Add(m); queue[qi] = -1; tombs++; }   // member survivor needs a slot; member-escape leaves head's intact
                            dsplit = true;
                            break;
                        }
                        bool hFl = hy2 < 0, mFl = my2 < 0;       // below row 0 = the floor — a static contact, never an escape, never a valid index
                        int tH = hFl ? idx : hx2 + hy2 * w + hz2 * wh;
                        int tM = mFl ? m : mx2 + my2 * w + mz2 * wh;
                        bool freeH = !hFl && ((data[tH] & VoxelDda.MaterialMask) == 0u || tH == m);
                        bool freeM = !mFl && ((data[tM] & VoxelDda.MaterialMask) == 0u || tM == idx);
                        if (freeH && freeM)
                        {
                            data[idx] = 0u; data[m] = 0u;
                            data[tH] = fc.vox; data[tM] = fc.vox2;
                            dirty.Add(idx); dirty.Add(m); dirty.Add(tH); dirty.Add(tM);
                            falling.Remove(idx); falling.Remove(m);
                            falling[tH] = fc; falling[tM] = fc;
                            fc.partner = tM; fc.headIdx = tH;
                            queue[qi] = tH; idx = tH;
                            dhx = hx2; dhy = hy2; dhz = hz2;
                            dmx = mx2; dmy = my2; dmz = mz2; m = tM;
                            if (axis == 0) fc.acc.x -= step; else if (axis == 1) fc.acc.y -= step; else fc.acc.z -= step;
                            dmoved = true; moves++;
                            continue;
                        }
                        bool blockH = hFl || (tH != m && (data[tH] & VoxelDda.MaterialMask) != 0u);
                        bool blockM = mFl || (tM != idx && (data[tM] & VoxelDda.MaterialMask) != 0u);
                        if ((!hFl && blockH && falling.ContainsKey(tH)) || (!mFl && blockM && falling.ContainsKey(tM)))
                            continue;                                    // a falling sibling blocks one member — wait it out
                        if (blockH && blockM)
                        {                                                // symmetric obstruction — shared momentum dies on this axis
                            if (axis == 0) { fc.acc.x = 0f; fc.vel.x = 0f; }
                            else if (axis == 1) { fc.acc.y = 0f; fc.vel.y = 0f; }
                            else { fc.acc.z = 0f; fc.vel.z = 0f; }
                            continue;
                        }
                        // asymmetric obstruction → the piece fractures: stuck member
                        // keeps its support, free member keeps the shared momentum
                        {
                            uint voxH = fc.vox, voxM = fc.vox2;
                            int surv = blockH ? m : idx;
                            var lone = new FallCell { vel = fc.vel, acc = fc.acc, vox = blockH ? voxM : voxH };
                            falling[surv] = lone;
                            queue.Add(m);                                 // whichever side is the member, it needs its own slot now
                            fc.vox = blockH ? voxH : voxM; fc.vox2 = 0; fc.partner = -1; fc.headIdx = -1;
                            if (axis == 0) { fc.acc.x = 0f; fc.vel.x = 0f; }
                            else if (axis == 1) { fc.acc.y = 0f; fc.vel.y = 0f; }
                            else { fc.acc.z = 0f; fc.vel.z = 0f; }
                            dsplit = true;
                            break;
                        }
                    }
                    if (dsplit) continue;
                    // landing: BOTH members grounded + slow → two rubble cells, adjacent — a whole brick
                    m = fc.partner;
                    bool hGround = dhy == 0 || ((data[idx - w] & VoxelDda.MaterialMask) != 0u && !falling.ContainsKey(idx - w));
                    bool mGround = dmy == 0 || ((data[m - w] & VoxelDda.MaterialMask) != 0u && !falling.ContainsKey(m - w));
                    if (!dmoved && hGround && mGround && fc.vel.y <= 0.001f &&
                        Mathf.Abs(fc.vel.x) + Mathf.Abs(fc.vel.z) < landSpeed)
                    {
                        if (deposit) { rubble.Add(idx); slumpQueue.Add(idx); rubble.Add(m); slumpQueue.Add(m); }
                        else { data[idx] = 0u; data[m] = 0u; dirty.Add(idx); dirty.Add(m); }
                        falling.Remove(idx); falling.Remove(m);
                        queue[qi] = -1; tombs++;
                    }
                    continue;
                }

                int z = idx / wh, rem = idx - z * wh, y = rem / w, x = rem - y * w;
                uint hsh = (uint)idx * 2654435761u;
                int a0 = (int)((hsh >> 12) % 3u);
                bool moved = false, esc = false;
                for (int k = 0; k < 3 && moves < maxMoves; k++)
                {
                    int axis = (a0 + k) % 3;
                    float acc = axis == 0 ? fc.acc.x : axis == 1 ? fc.acc.y : fc.acc.z;
                    if (Mathf.Abs(acc) < 1f) continue;
                    int step = acc > 0f ? 1 : -1;
                    int nx = x + (axis == 0 ? step : 0), ny = y + (axis == 1 ? step : 0), nz = z + (axis == 2 ? step : 0);
                    if (nx < 0 || nx >= w || nz < 0 || nz >= d || ny >= h || ny < 0)
                    {
                        if (ny < 0) { fc.acc.y = 0f; if (fc.vel.y < 0f) fc.vel.y = 0f; continue; }   // the floor row
                        data[idx] = 0u; dirty.Add(idx);
                        escaped.Add(new FallEscape { idx = idx, vel = fc.vel, vox = fc.vox });       // out of the volume -> shard
                        falling.Remove(idx);
                        queue[qi] = -1; tombs++; esc = true;
                        break;
                    }
                    int t = nx + ny * w + nz * wh;
                    if ((data[t] & VoxelDda.MaterialMask) == 0u)
                    {
                        data[t] = fc.vox; data[idx] = 0u;
                        dirty.Add(idx); dirty.Add(t);
                        falling.Remove(idx); falling[t] = fc;
                        queue[qi] = t; idx = t; x = nx; y = ny; z = nz;
                        if (axis == 0) fc.acc.x -= step; else if (axis == 1) fc.acc.y -= step; else fc.acc.z -= step;
                        moved = true; moves++;
                    }
                    else if (falling.ContainsKey(t)) { /* a falling sibling blocks the cell — wait for it next tick */ }
                    else if (axis == 0) { fc.acc.x = 0f; fc.vel.x = 0f; }
                    else if (axis == 1) { fc.acc.y = 0f; fc.vel.y = 0f; }
                    else                { fc.acc.z = 0f; fc.vel.z = 0f; }
                }
                if (esc) continue;
                bool belowStatic = y == 0 ||
                    ((data[idx - w] & VoxelDda.MaterialMask) != 0u && !falling.ContainsKey(idx - w));
                if (!moved && belowStatic && fc.vel.y <= 0.001f &&
                    Mathf.Abs(fc.vel.x) + Mathf.Abs(fc.vel.z) < landSpeed)
                {
                    if (deposit) { rubble.Add(idx); slumpQueue.Add(idx); }
                    else { data[idx] = 0u; dirty.Add(idx); }
                    falling.Remove(idx); queue[qi] = -1; tombs++;
                }
            }
            if (tombs * 2 > queue.Count && queue.Count > 64)
            { queue.Clear(); queue.AddRange(falling.Keys); }
            return moves;
        }

        // ── Flying-voxel transport: a cell in world space between grids ──────────

        public enum FlySampleKind : byte { Free = 0, Open = 1, Solid = 2 }
        /// <summary>Result of one point query: Free = empty space, Open = inside a
        /// volume's AIR cell (handoff target), Solid = inside material (volume or
        /// terrain — the step may not be taken).</summary>
        public struct FlySample { public FlySampleKind kind; public string volumeId; public int cellIdx; }
        public delegate FlySample FlyQuery(Vector3 pos);
        public enum FlyOutcome : byte { Flying = 0, EnteredOpen = 1 }

        /// <summary>
        /// One ballistic tick for a single-cell voxel in open world space — the
        /// transport phase of a doomed cell between grids. vel is m/s; acc is the
        /// meter accumulator spent in whole `stepLen` strides, largest backlog axis
        /// first (the same accumulator contract StepFalling uses inside a grid, so
        /// per-call travel is bounded by maxSteps×stepLen and a POINT query is exact
        /// where a swept ray would be wasted — this is the physics-sweep mitigation
        /// for swarms of thousands). Each candidate position is sampled: Solid
        /// absorbs that axis's momentum without moving (a fast cell stalls against
        /// the surface — no tunneling at any speed); Open accepts the step and
        /// reports the volume + cell for handoff; Free just moves. The accumulator
        /// is NOT drained on an Open step — if the handoff fails the residual keeps
        /// the same step pending for the next tick.
        /// </summary>
        public static FlyOutcome StepFlyer(ref Vector3 pos, ref Vector3 vel, ref Vector3 acc,
            float dt, float gravity, float drag, float stepLen, int maxSteps,
            FlyQuery sample, out FlySample entered)
        {
            entered = default;
            vel.y -= gravity * dt;
            vel *= Mathf.Max(0f, 1f - drag * dt);
            acc += vel * dt;
            for (int g = 0; g < maxSteps; g++)
            {
                float ax = Mathf.Abs(acc.x), ay = Mathf.Abs(acc.y), az = Mathf.Abs(acc.z);
                int axis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
                if ((axis == 0 ? ax : axis == 1 ? ay : az) < stepLen) break;
                int step = axis == 0 ? (acc.x > 0f ? 1 : -1) : axis == 1 ? (acc.y > 0f ? 1 : -1) : (acc.z > 0f ? 1 : -1);
                Vector3 np = pos;
                if (axis == 0) np.x += step * stepLen; else if (axis == 1) np.y += step * stepLen; else np.z += step * stepLen;
                FlySample s = sample(np);
                if (s.kind == FlySampleKind.Open) { pos = np; entered = s; return FlyOutcome.EnteredOpen; }
                if (s.kind == FlySampleKind.Solid)
                {
                    if (axis == 0) { vel.x = 0f; acc.x = 0f; }
                    else if (axis == 1) { vel.y = 0f; acc.y = 0f; }
                    else { vel.z = 0f; acc.z = 0f; }
                    continue;
                }
                pos = np;
                if (axis == 0) acc.x -= step * stepLen; else if (axis == 1) acc.y -= step * stepLen; else acc.z -= step * stepLen;
            }
            return FlyOutcome.Flying;
        }

        /// <summary>
        /// Voxel pairing seed — the "brick domino" trick. The grid partitions into 2×2
        /// XZ tiles; each tile deterministically picks a pair axis (x or z) and rolls
        /// `chance` for whether it pairs at all. A paired cell returns its PARTNER's index —
        /// the one other cell of the same tile the tile's hash selects (x^1 or z^1 neighbor);
        /// -1 means the cell stays single: the tile rolled over chance, the partner is out of
        /// bounds, not doomed, or a different resistance class. The pairing is mutual
        /// (PairPartner(a) == b ⟺ PairPartner(b) == a) and the ANCHOR — the even-indexed cell
        /// along the pair axis — is always the smaller flat index, which is what lets the
        /// seeder build one FallCell entity per pair without a visited set (members skip,
        /// anchors construct). Even anchoring means pieces can never chain past 2 cells:
        /// two dominoes side by side are two independent entities that merely sit adjacent.
        /// </summary>
        public static int PairPartner(int idx, int w, int h, int d, float chance,
            System.Func<int, bool> isDoomed, System.Func<int, int> classOf)
        {
            int wh = w * h;
            int z = idx / wh, rem = idx - z * wh, y = rem / w, x = rem - y * w;
            int bx = x & ~1, bz = z & ~1;
            uint th = (uint)(bx + y * w + bz * wh) * 2654435761u;   // tile key — same for all 4 cells
            if (chance < 1f && ((th >> 24) & 255u) / 255f >= chance) return -1;   // singles ("broken bricks"); 1.0 pairs every eligible tile
            bool xAxis = (th & 0x10000u) != 0;
            int px = xAxis ? x ^ 1 : x, pz = xAxis ? z : z ^ 1;     // the paired cell inside the tile
            if (px >= w || pz >= d) return -1;                      // tile clipped by the grid edge
            int partner = px + y * w + pz * wh;
            if (!isDoomed(partner) || classOf(partner) != classOf(idx)) return -1;
            return partner;
        }
    }
}
