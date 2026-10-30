using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// The world's persistent walkability layer — ONE owner of the declared
    /// spatial facts (ground height, floor material, body-column fit) for the
    /// whole loaded map. Cells are world-fixed at the canonical tile size;
    /// every verdict is derived ONCE from VoxelCollisionWorld and lives until
    /// a voxel write dirties it (damage, volume/terrain registration, removal).
    ///
    /// CoverField windows bulk-copy from this cache at Build instead of
    /// re-probing the world per fighter per scan — the event-driven answer to
    /// "why are we probing at all": the voxel edits themselves dirty exactly
    /// the cells they touched; everything else is served from cache forever.
    /// Actors still differ only by floor mask and flood seed, which stay
    /// actor-relative on top of the shared verdicts.
    ///
    /// Layout: lazily-allocated 64×64-cell blocks (16m × 16m), so the layer
    /// pays only for regions someone actually queries — never a full-map pass.
    /// </summary>
    public sealed class WalkLayer : IEditLog
    {
        /// <summary>Canonical tile — a field window is servable iff its
        /// CellSize equals this. 0.25m = fieldRes 64 @ radius 8m (pit config).</summary>
        public const float CellSize = 0.25f;
        const int BlockCells = 64;                      // 64² cells = 16m × 16m
        const float NoGround = -9999f;
        const byte Probed = 1, Grounded = 2, FitOk = 4, FitBad = 8, SqOk = 16, SqBad = 32;
        /// <summary>A cached ground verdict is reused while a consumer's probe
        /// base stays in this band — same cell, same deck assumption. A cell
        /// asked from a different deck (roof vs street) re-probes.</summary>
        const float ProbeYTolerance = 0.75f;

        sealed class Block
        {
            public readonly float[] g = new float[BlockCells * BlockCells];     // NaN unprobed
            public readonly int[] mat = new int[BlockCells * BlockCells];
            public readonly byte[] fl = new byte[BlockCells * BlockCells];
            public readonly float[] fitH = new float[BlockCells * BlockCells];  // bodyH the fit verdict was probed at
            public readonly float[] probeY = new float[BlockCells * BlockCells];// ray origin Y the ground verdict was probed at
            public readonly int[] sqKey = new int[BlockCells * BlockCells];     // spec key the square verdict was probed at
            public readonly int[] egKey = new int[BlockCells * BlockCells];     // spec key the edge row was probed at
            public readonly ushort[] eg = new ushort[BlockCells * BlockCells];  // bits 0-7 edge tested · 8-15 edge open
            public Block() { for (int i = 0; i < g.Length; i++) g[i] = float.NaN; }
        }

        /// <summary>Field→world dir table — CoverField's
        /// (dz+1)*3+(dx+1)−skip-center ordering, identical on both sides.</summary>
        static readonly int[] EDX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        static readonly int[] EDZ = { -1, -1, -1, 0, 0, 1, 1, 1 };

        /// <summary>The physical spec a square/edge verdict was probed at —
        /// quantized so functionally-identical specs share the cache. A spec
        /// mismatch poisons the whole row (re-probe under the new spec).</summary>
        static int SpecKey(float half, float bodyH, float stepUp)
            => (Mathf.RoundToInt(half * 100f) << 16)
             | (Mathf.RoundToInt(stepUp * 100f) << 8)
             | Mathf.RoundToInt(bodyH * 4f);

        readonly VoxelCollisionWorld world;
        readonly Dictionary<long, Block> blocks = new();

        public WalkLayer(VoxelCollisionWorld w) { world = w; }

        // ─── Diagnostics (HUD readout) ────────────────────────────────────
        public int BlocksAllocated => blocks.Count;
        public int CellsProbed { get; private set; }
        /// <summary>Bumps on EVERY voxel-write invalidation (the contract is "call on any voxel write") —
        /// the world's edit stamp. Consumers that cache derived products (cover scans) key on it to know
        /// the geometry is unchanged without re-deriving it.</summary>
        public int Version { get; private set; }

        // Change log — WHERE each versioned edit landed, so consumers can ask "did anything change in MY
        // window?" instead of "did anything change anywhere?" (a bullet hole across the map must not flush
        // a fighter's cover scan). Ring of the last LogCap edit bounds; older = treated as "changed".
        public const int LogCap = 4096;   // power of two
        private readonly Bounds[] changeLog = new Bounds[LogCap];
        private void LogChange(Bounds b) { Version++; changeLog[Version & (LogCap - 1)] = b; }
        /// <summary>Bounds of the edit that produced `version` (valid for Version-LogCap &lt; version ≤ Version).</summary>
        public Bounds ChangeAt(int version) => changeLog[version & (LogCap - 1)];
        /// <summary>True if any edit after `sinceVersion` touched `window` — or the log wrapped (unknown ⇒ yes).</summary>
        public bool ChangedSince(int sinceVersion, Bounds window)
        {
            int n = Version - sinceVersion;
            if (n <= 0) return false;
            if (n > LogCap) return true;
            for (int v = sinceVersion + 1; v <= Version; v++)
                if (changeLog[v & (LogCap - 1)].Intersects(window)) return true;
            return false;
        }
        public int CellsInvalidated { get; private set; }

        static int CellCoord(float w) => Mathf.FloorToInt(w / CellSize);

        Block BlockAt(int wx, int wz, bool create)
        {
            int bx = Mathf.FloorToInt(wx / (float)BlockCells);
            int bz = Mathf.FloorToInt(wz / (float)BlockCells);
            long key = ((long)bx << 32) | (uint)bz;
            if (!blocks.TryGetValue(key, out Block b) && create)
                blocks[key] = b = new Block();
            return b;
        }

        static int LocalIdx(int wx, int wz)
        {
            int lx = wx - Mathf.FloorToInt(wx / (float)BlockCells) * BlockCells;
            int lz = wz - Mathf.FloorToInt(wz / (float)BlockCells) * BlockCells;
            return lz * BlockCells + lx;
        }

        /// <summary>
        /// Bulk-fill a field window (n×n cells at cell-aligned `origin`) from
        /// the shared cache — each covered world cell probed ONCE ever, then
        /// served verbatim to every consumer until invalidated. `baseY` is the
        /// field's deck height (the ground probe band is deck-relative);
        /// `bodyH` is the fit column height. Returns false when the window's
        /// cell size isn't the canonical tile — caller keeps its lazy path.
        /// Writes the field's own flag vocabulary so filled channels are
        /// indistinguishable from locally-probed ones.
        /// </summary>
        public bool FillWindow(Vector3 origin, int n, float cellSize, float baseY, float bodyH,
                               float[] groundY, int[] floorMat, CoverField.CellFlags[] flags,
                               float bodyHalf = -1f, float stepUp = 0f)
        {
            if (Mathf.Abs(cellSize - CellSize) > 1e-4f) return false;
            int ox = CellCoord(origin.x), oz = CellCoord(origin.z);
            float probeY = baseY + 1.6f;   // same ray band GroundYAt used (deck-relative)
            for (int cz = 0; cz < n; cz++)
            {
                int wz = oz + cz;
                for (int cx = 0; cx < n; cx++)
                {
                    int wx = ox + cx;
                    Block b = BlockAt(wx, wz, true);
                    int bi = LocalIdx(wx, wz);
                    // Ground — cached unless the consumer's deck moved bands.
                    if ((b.fl[bi] & Probed) == 0 || Mathf.Abs(b.probeY[bi] - probeY) > ProbeYTolerance)
                        ProbeGround(b, bi, wx, wz, probeY);
                    int i = cz * n + cx;
                    if ((b.fl[bi] & Grounded) == 0) { groundY[i] = NoGround; continue; }
                    groundY[i] = b.g[bi];
                    floorMat[i] = b.mat[bi];
                    flags[i] |= CoverField.CellFlags.Grounded;
                    // Fit — the column verdict cached at THIS body height.
                    if ((b.fl[bi] & (FitOk | FitBad)) == 0 || Mathf.Abs(b.fitH[bi] - bodyH) > 0.05f)
                        ProbeFit(b, bi, wx, wz, bodyH);
                    flags[i] |= (b.fl[bi] & FitOk) != 0 ? CoverField.CellFlags.FitOk
                                                        : CoverField.CellFlags.FitBad;
                    // Footprint square — the mover's own TryBodySquare verdict,
                    // keyed by spec so different bodies never share a wrong answer.
                    if (bodyHalf > 0f)
                    {
                        int key = SpecKey(bodyHalf, bodyH, stepUp);
                        if ((b.fl[bi] & (SqOk | SqBad)) == 0 || b.sqKey[bi] != key)
                            ProbeSquare(b, bi, wx, wz, bodyHalf, bodyH, stepUp, key);
                        flags[i] |= (b.fl[bi] & SqOk) != 0 ? CoverField.CellFlags.SqOk
                                                            : CoverField.CellFlags.SqBad;
                    }
                }
            }
            return true;
        }

        /// <summary>The support-column probe: ray down the cell center through
        /// the consumer's deck band — same law the field's GroundYAt used
        /// (deck-relative so curb tops, aprons, interior floors all read).</summary>
        void ProbeGround(Block b, int bi, int wx, int wz, float probeY)
        {
            // Fit dies with the ground it stood on — never outlive a re-probe.
            b.fl[bi] = (byte)((b.fl[bi] & ~(Grounded | FitOk | FitBad)) | Probed);
            b.probeY[bi] = probeY;
            CellsProbed++;
            Vector3 c = new Vector3((wx + 0.5f) * CellSize, probeY, (wz + 0.5f) * CellSize);
            if (world != null && world.Raycast(c, Vector3.down, 3.2f, out VoxelRayHit gh))
            {
                b.g[bi] = gh.point.y; b.mat[bi] = gh.material; b.fl[bi] |= Grounded;
            }
            else { b.g[bi] = NoGround; b.mat[bi] = 0; }
        }

        /// <summary>The body column at the probed ground — delegates to the
        /// shared VoxelCollisionWorld.ColumnFree primitive (feet / waist /
        /// chest at 0.06 / 0.35h / 0.6h). The cache stores the SHARED law's
        /// answer, not a re-derivation — bulk-fill and lazy paths can't
        /// diverge.</summary>
        void ProbeFit(Block b, int bi, int wx, int wz, float bodyH)
        {
            b.fitH[bi] = bodyH;
            b.fl[bi] = (byte)(b.fl[bi] & ~(FitOk | FitBad));
            if (world == null) { b.fl[bi] |= FitBad; return; }
            Vector3 p = new Vector3((wx + 0.5f) * CellSize, b.g[bi], (wz + 0.5f) * CellSize);
            b.fl[bi] |= world.ColumnFree(p, Mathf.Max(0.2f, bodyH)) ? FitOk : FitBad;
        }

        /// <summary>The body-square verdict at the probed ground — delegates
        /// to the mover's own TryBodySquare (square free, else step-up offered
        /// + raised square free). One primitive serves map and mover; the
        /// cache stores THAT answer so certified ≡ executable stays literal.</summary>
        void ProbeSquare(Block b, int bi, int wx, int wz, float bodyHalf, float bodyH, float stepUp, int key)
        {
            b.sqKey[bi] = key;
            b.fl[bi] = (byte)(b.fl[bi] & ~(SqOk | SqBad));
            if (world == null || (b.fl[bi] & Grounded) == 0) { b.fl[bi] |= SqBad; return; }
            Vector3 p = new Vector3((wx + 0.5f) * CellSize, b.g[bi], (wz + 0.5f) * CellSize);
            b.fl[bi] |= world.TryBodySquare(p, bodyHalf, Mathf.Max(0.2f, bodyH),
                                            stepUp, out _) ? SqOk : SqBad;
        }

        /// <summary>A world-cached edge verdict: cell (wx,wz), dir d — null
        /// when unprobed or spec-stale, else the stored open/closed answer.</summary>
        public bool? TryEdge(int wx, int wz, int d, float bodyHalf, float bodyH, float stepUp)
        {
            Block b = BlockAt(wx, wz, false);
            if (b == null) return null;
            int bi = LocalIdx(wx, wz);
            if (b.egKey[bi] != SpecKey(bodyHalf, bodyH, stepUp)) return null;
            return (b.eg[bi] & (1 << d)) != 0
                ? (b.eg[bi] & (0x100 << d)) != 0
                : (bool?)null;
        }

        /// <summary>Store an edge verdict — mirrored onto the neighbor under
        /// the opposite dir (the edge test is symmetric), spec-keyed on both.</summary>
        public void SetEdge(int wx, int wz, int d, bool open, float bodyHalf, float bodyH, float stepUp)
        {
            int key = SpecKey(bodyHalf, bodyH, stepUp);
            Block b = BlockAt(wx, wz, true);
            int bi = LocalIdx(wx, wz);
            if (b.egKey[bi] != key) { b.egKey[bi] = key; b.eg[bi] = 0; }
            b.eg[bi] |= (ushort)(1 << d);
            if (open) b.eg[bi] |= (ushort)(0x100 << d);
            int nx = wx + EDX[d], nz = wz + EDZ[d];
            Block nb = BlockAt(nx, nz, true);
            int ni = LocalIdx(nx, nz);
            if (nb.egKey[ni] != key) { nb.egKey[ni] = key; nb.eg[ni] = 0; }
            int o = 7 - d;
            nb.eg[ni] |= (ushort)(1 << o);
            if (open) nb.eg[ni] |= (ushort)(0x100 << o);
        }

        /// <summary>Drop every verdict whose cell rect touches `b` — the
        /// event-driven half of the contract. Call on ANY voxel write; the
        /// cells re-derive lazily on the next window fill.</summary>
        public void Invalidate(Bounds b)
        {
            LogChange(b);   // unconditional — a write to an UNPROBED cell still changes what a cover scan would see
            int x0 = CellCoord(b.min.x), x1 = CellCoord(b.max.x);
            int z0 = CellCoord(b.min.z), z1 = CellCoord(b.max.z);
            for (int wz = z0; wz <= z1; wz++)
                for (int wx = x0; wx <= x1; wx++)
                {
                    Block blk = BlockAt(wx, wz, false);
                    if (blk == null) continue;
                    int bi = LocalIdx(wx, wz);
                    if (blk.fl[bi] == 0) continue;
                    blk.fl[bi] = 0; blk.g[bi] = float.NaN;
                    // Edges that CROSS this cell die with it — a dirtied cell
                    // can poison a neighbor's midpoint verdict even when that
                    // neighbor's own ground is untouched.
                    blk.eg[bi] = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        Block nb = BlockAt(wx + EDX[d], wz + EDZ[d], false);
                        if (nb == null) continue;
                        int ni = LocalIdx(wx + EDX[d], wz + EDZ[d]);
                        int back = 7 - d;
                        nb.eg[ni] &= (ushort)~((1 << back) | (0x100 << back));
                    }
                    CellsInvalidated++;
                }
        }

        /// <summary>Wholesale rewrite (terrain re-registration, volume clear)
        /// — drops every cached verdict; blocks stay allocated.</summary>
        public void InvalidateAll()
        {
            LogChange(new Bounds(Vector3.zero, new Vector3(1e9f, 1e9f, 1e9f)));   // intersects every window
            foreach (var b in blocks.Values)
            {
                for (int i = 0; i < b.fl.Length; i++)
                    if (b.fl[i] != 0) { b.fl[i] = 0; b.g[i] = float.NaN; b.eg[i] = 0; CellsInvalidated++; }
            }
        }
    }
}
