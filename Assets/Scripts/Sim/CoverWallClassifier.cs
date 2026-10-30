using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>Where voxel edits landed — WalkLayer implements it; consumers ask "did anything change in MY window".</summary>
    public interface IEditLog
    {
        int Version { get; }
        bool ChangedSince(int sinceVersion, Bounds window);
    }

    /// <summary>Builds the output mark for one classified wall column (CoverSensor supplies WallMark).</summary>
    public interface IMarkFactory<TMark>
    {
        TMark Make(in CoverWall w, byte cls, float reqDepth, float support, float facing);
    }

    /// <summary>
    /// World-fixed wall-column classification. A wall column's class / required duck / support depend on the
    /// column's geometry, the ground at its stand-spot and the BODY's measured silhouette heights — never on where
    /// the owner stands or who the threat is. So they are classified once per (volume, body spec, world edit
    /// stamp) and reused by every scan; a scan only copies the cached entries in range into marks and adds the
    /// two things that really are per-scan: the threat-facing value and (for the rare column with no ground)
    /// the owner's support fallback.
    ///
    /// Equality contract: the produced marks, per-volume aggregates and class counts are bit-identical to the
    /// legacy per-scan loop (CoverSensor.ClassifyLegacy) — Tools/cover_selftest proves it.
    /// Cache lifetime hangs off SolidVolumeSet.VolumeWalls.consumerData: a re-registered/damaged volume or a
    /// changed (minSpan, openCell) emission key makes a NEW set, so a stale classification cannot be served.
    /// </summary>
    public sealed class CoverWallClassifier
    {
        public const byte Low = 0, Crouch = 1, Full = 2;   // == CoverSensor.WallClass

        /// <summary>Everything the classification reads besides the column and the ground.</summary>
        public struct Params : IEquatable<Params>
        {
            public float hStand, hShallow, hDeep, standoff, wallFloor, depthMargin;
            public bool Equals(Params o) => hStand == o.hStand && hShallow == o.hShallow && hDeep == o.hDeep
                && standoff == o.standoff && wallFloor == o.wallFloor && depthMargin == o.depthMargin;
        }

        public delegate bool GroundProbe(Vector3 spot, out float y);

        public struct VolAgg { public string volId; public float depth; public byte cls; }

        sealed class Entry
        {
            public Params key; public int ver;
            public byte[] flag;     // 0 dropped · 1 classified · 2 no ground (classified live against the owner's fallback)
            public byte[] cls; public float[] reqD, sy;
            public int fallbackCount, nFull, nCrouch, nLo;
            public bool hasAgg; public float aggDepth; public byte aggCls;
        }

        // Diagnostics — HUD / tests.
        public int LastHits, LastBuilds;

        static bool Covers(in CoverWall w, float eye) => w.firstSolidY <= eye && eye < w.spanTop;

        /// <summary>Piecewise duck solve — identical to CoverSensor.DuckForTop (clamped 0..2).</summary>
        public static float DuckForTop(in Params p, float top, float baseY)
        {
            float clear = top - p.depthMargin - baseY;
            float raw;
            if (clear >= p.hStand) raw = 0f;
            else
            {
                float spanMorph = p.hStand - p.hShallow, spanDeep = p.hShallow - p.hDeep;
                if (clear >= p.hShallow && spanMorph > 1e-4f) raw = (p.hStand - clear) / spanMorph;
                else raw = spanDeep > 1e-4f ? 1f + (p.hShallow - clear) / spanDeep : 3f;
            }
            return Mathf.Clamp(raw, 0f, 2f);
        }

        static Bounds Pad(Bounds b)
        {
            // Ground probes sit up to ~0.35 m outside the AABB and cast ~2 m vertically.
            b.Expand(new Vector3(1f, 6f, 1f));
            return b;
        }

        /// <summary>Classify every set in range. Marks are appended to `outMarks` in the legacy order;
        /// `aggs` receives one (volume, deepest non-Low requirement, its class) per volume that has any.</summary>
        public void Classify<TMark, TFactory>(List<SolidVolumeSet.VolumeWalls> sets, in Params p, float supportY,
            GroundProbe ground, IEditLog edits, bool hasT, Vector3 tp, TFactory make,
            List<TMark> outMarks, List<VolAgg> aggs, ref int nFull, ref int nCrouch, ref int nLo)
            where TFactory : struct, IMarkFactory<TMark>
        {
            LastHits = LastBuilds = 0;
            for (int si = 0; si < sets.Count; si++)
            {
                var set = sets[si];
                int n = set.walls.Count;
                if (n == 0) continue;
                var e = set.consumerData as Entry;
                bool valid = edits != null && e != null && e.key.Equals(p) && e.flag.Length == n
                          && !edits.ChangedSince(e.ver, Pad(set.bounds));
                if (valid) LastHits++;
                else { e = Build(set, e, p, ground, edits); LastBuilds++; }

                bool aggLive = e.fallbackCount > 0;      // a no-ground column makes the aggregate scan-dependent
                bool has = false; float aggDepth = 0f; byte aggCls = 0;
                int fFull = 0, fCrouch = 0, fLo = 0;
                var walls = set.walls;
                for (int i = 0; i < n; i++)
                {
                    byte f = e.flag[i];
                    if (f == 0) continue;
                    CoverWall w = walls[i];
                    byte cls; float reqD, sy;
                    if (f == 1) { cls = e.cls[i]; reqD = e.reqD[i]; sy = e.sy[i]; }
                    else
                    {
                        sy = supportY;                   // legacy: no ground ⇒ the owner's support stands in
                        if (w.spanTop - sy < p.wallFloor) continue;
                        cls = Covers(in w, sy + p.hStand) ? Full : Covers(in w, sy + p.hDeep) ? Crouch : Low;
                        reqD = cls == Low ? 0f : DuckForTop(in p, w.spanTop, sy);
                        if (cls == Full) fFull++; else if (cls == Crouch) fCrouch++; else fLo++;
                    }
                    if (aggLive && cls != Low && w.volumeId != null && (!has || reqD > aggDepth))
                    { aggDepth = reqD; aggCls = cls; has = true; }
                    float facing = 0f;
                    if (hasT)
                    {
                        Vector3 dt = tp - w.facePos; dt.y = 0f;
                        facing = dt.sqrMagnitude > 1e-6f ? Vector3.Dot(w.outDir, dt.normalized) : 0f;
                    }
                    outMarks.Add(make.Make(in w, cls, reqD, sy, facing));
                }
                nFull += e.nFull + fFull; nCrouch += e.nCrouch + fCrouch; nLo += e.nLo + fLo;
                if (aggLive)
                {
                    // fold in the cached (ground-known) columns' aggregate in column order — the cached
                    // aggregate alone would skip the live ones, so recompute purely from the live loop above
                    // for the whole volume (it saw every kept column).
                    if (has) aggs.Add(new VolAgg { volId = set.id, depth = aggDepth, cls = aggCls });
                }
                else if (e.hasAgg) aggs.Add(new VolAgg { volId = set.id, depth = e.aggDepth, cls = e.aggCls });
            }
        }

        Entry Build(SolidVolumeSet.VolumeWalls set, Entry reuse, in Params p, GroundProbe ground, IEditLog edits)
        {
            int n = set.walls.Count;
            var e = reuse ?? new Entry();
            if (e.flag == null || e.flag.Length != n)
            { e.flag = new byte[n]; e.cls = new byte[n]; e.reqD = new float[n]; e.sy = new float[n]; }
            e.key = p; e.ver = edits != null ? edits.Version : 0;
            e.fallbackCount = e.nFull = e.nCrouch = e.nLo = 0; e.hasAgg = false; e.aggDepth = 0f; e.aggCls = 0;
            var walls = set.walls;
            for (int i = 0; i < n; i++)
            {
                CoverWall w = walls[i];
                Vector3 spot = w.facePos + w.outDir * (p.standoff + w.voxelSize * 0.25f);
                if (!ground(spot, out float sy)) { e.flag[i] = 2; e.fallbackCount++; continue; }
                if (w.spanTop - sy < p.wallFloor) { e.flag[i] = 0; continue; }
                byte cls = Covers(in w, sy + p.hStand) ? Full : Covers(in w, sy + p.hDeep) ? Crouch : Low;
                float reqD = cls == Low ? 0f : DuckForTop(in p, w.spanTop, sy);
                e.flag[i] = 1; e.cls[i] = cls; e.reqD[i] = reqD; e.sy[i] = sy;
                if (cls == Full) e.nFull++; else if (cls == Crouch) e.nCrouch++; else e.nLo++;
                if (cls != Low && w.volumeId != null && (!e.hasAgg || reqD > e.aggDepth))
                { e.aggDepth = reqD; e.aggCls = cls; e.hasAgg = true; }
            }
            set.consumerData = e;
            return e;
        }
    }
}
