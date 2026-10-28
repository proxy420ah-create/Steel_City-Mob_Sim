using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Read-only cover+combat metrics overlay — right-center of screen, [F5] toggles.
    /// THE centralized readout surface for combat debugging: the debug tab keeps
    /// only controls (buttons, toggles, sliders); all info labels live here.
    /// EVENT-DRIVEN rebuild: sources fire `Changed` on every display-relevant
    /// mutation → dirty → rebuild next Update (events flag dirty, they don't
    /// rebuild inside themselves — mutation sequences finish first). A 2Hz
    /// "analog floor" runs only while something is LIVE (combat, possess,
    /// probe preview) so continuous readouts stay honest; idle = zero rebuilds.
    /// Sections: Vinny pose · combat state + exposure telemetry · engagement
    /// flow chart · threat · sensor measurements + scan · possess-drive probe
    /// ring · Tan Suit combat. Drawn over everything via GUI.depth so it
    /// survives HUD minimize for snapshots.
    /// </summary>
    public class CoverMetricsPanel : MonoBehaviour
    {
        public static bool Enabled = true;

        [SerializeField] private float refreshHz = 5f;
        [SerializeField] private float panelWidth = 375f;

        private string cached = "cover metrics — waiting for a character…";
        private readonly GUIContent content = new GUIContent();
        private float cachedH = -1f;
        private bool dirty = true;           // event-driven rebuild flag
        private float nextContinuous;        // analog floor — only while live
        private Texture2D bgTex;
        private GUIStyle style;
        private CityMap3D cityMap;
        private float nextResolve;
        private CombatController tanCC;
        private CombatController ccHook;     // vinny's controller (subscribed)
        private CoverSensor senHook;         // vinny's sensor (subscribed)
        private readonly StringBuilder sb = new StringBuilder(1024);
        private static readonly string[] dirs = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

        void Awake() => cityMap = FindFirstObjectByType<CityMap3D>();

        void MarkDirty() => dirty = true;

        // Self-profile — same pattern as DebugHUDManager: Update+OnGUI ms
        // summed per frame, shown on the panel title line. Answers "is THIS
        // surface the frame tax?" on the wire, next to hud Xms.
        private int profFrame = -1, profEvents, profEventsShown;
        private float profMs, profMsShown, profPeak, profPeakShown;
        private float Prof(float ms)
        {
            if (profFrame != Time.frameCount)
            { profMsShown = profMs; profEventsShown = profEvents;
              profPeakShown = profPeak; profMs = 0f; profEvents = 0; profPeak = 0f;
              profFrame = Time.frameCount; }
            profMs += ms; profEvents++;
            if (ms > profPeak) profPeak = ms;
            return ms;
        }

        /// <summary>Swap the Changed subscription when a resolved component
        /// changes — events land on the CURRENT body, never a stale one.</summary>
        void Rehook<T>(ref T cur, T next) where T : Component
        {
            if (ReferenceEquals(cur, next)) return;
            if (cur is CombatController c1) c1.Changed -= MarkDirty;
            else if (cur is CoverSensor s1) s1.Changed -= MarkDirty;
            cur = next;
            if (cur is CombatController c2) c2.Changed += MarkDirty;
            else if (cur is CoverSensor s2) s2.Changed += MarkDirty;
            dirty = true;
        }

        void OnDestroy()
        {
            if (ccHook != null) ccHook.Changed -= MarkDirty;
            if (senHook != null) senHook.Changed -= MarkDirty;
            if (tanCC != null) tanCC.Changed -= MarkDirty;
        }

        void Update()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            UpdateInner();
            Prof((float)sw.Elapsed.TotalMilliseconds);
        }

        void UpdateInner()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame)
            {
                Enabled = !Enabled;
                dirty = true;
                Debug.Log($"[CoverMetrics] panel {(Enabled ? "ON" : "OFF")} (F5)");
            }
            // Event-driven rebuild contract: sources fire Changed on every
            // display-relevant mutation → dirty → rebuild next Update. The
            // analog floor (refreshHz) runs ONLY while something is live —
            // continuous readouts (pos, blend, shot countdowns, drive ring)
            // stay honest without pretending they're events. Idle = zero
            // rebuilds.
            var vinny = cityMap != null ? cityMap.SpawnedCharacter : null;
            Rehook(ref ccHook, vinny != null ? vinny.GetComponent<CombatController>() : null);
            Rehook(ref senHook, vinny != null ? vinny.GetComponent<CoverSensor>() : null);
            // Resolve Tan at a slow cadence — never per-GUI-event.
            if (Time.unscaledTime >= nextResolve)
            {
                nextResolve = Time.unscaledTime + 1f;
                if (tanCC == null)
                {
                    var go = GameObject.Find("Civilian_02");
                    if (go != null) Rehook(ref tanCC, go.GetComponent<CombatController>());
                }
                else if (tanCC == null || !tanCC) Rehook(ref tanCC, null);
            }
            bool live = (ccHook != null && ccHook.State != CombatController.CombatState.Idle)
                     || (tanCC != null && tanCC.State != CombatController.CombatState.Idle)
                     || PossessController.DriveActive || PossessController.Active
                     || (senHook != null && senHook.showTargetViz);
            if (!dirty && !(live && Time.unscaledTime >= nextContinuous)) return;
            nextContinuous = Time.unscaledTime + 1f / refreshHz;
            dirty = false;
            cached = BuildText();
        }

        private string BuildText()
        {
            sb.Length = 0;
            var vinny = cityMap != null ? cityMap.SpawnedCharacter : null;
            sb.AppendLine($"COVER + COMBAT · F5 to hide · {profMsShown:F2}ms/{profEventsShown}ev");
            if (vinny != null) AppendSubject("vinny", vinny, true);
            else sb.AppendLine("no spawned character");

            sb.AppendLine("── drive (possess) ──────");
            if (PossessController.DriveActive)
            {
                int b = PossessController.DriveBestDir;
                sb.AppendLine($"supportY {PossessController.DriveSupport:F2} · best duck {PossessController.DriveBestD:F2}"
                            + $" → req {PossessController.DriveReq:F2}"
                            + (b >= 0 ? $" · win {dirs[b]} {PossessController.ProbeTop[b]:F2} [{PossessController.ProbeVol[b]}]" : "")
                            + (PossessController.ZoneCls != CoverSensor.WallClass.Low
                                ? $" · zone {PossessController.ZoneCls} duck{PossessController.ZoneD:F2}" : ""));
                for (int i = 0; i < 8; i += 4)
                {
                    for (int j = i; j < i + 4; j++)
                    {
                        float d = PossessController.ProbeD[j];
                        if (float.IsNaN(d)) { sb.Append($"{dirs[j]} —     ".PadRight(13)); continue; }
                        char cls = PossessController.ProbeFull[j] ? 'F' : d <= 2f ? 'C' : 'L';
                        string vol = PossessController.ProbeVol[j];
                        string tag = vol == null ? "·" : vol == "terrain" ? "t" : "v";
                        sb.Append($"{dirs[j]} {PossessController.ProbeTop[j]:F2}{cls}{tag}".PadRight(13));
                    }
                    sb.AppendLine();
                }
            }
            else sb.AppendLine(PossessController.HoldActive
                ? "combat hold — inheriting combat pose until he leaves the spot"
                : PossessController.Active
                ? "idle — no threat context (combat-gated)"
                : "idle — not possessed");

            var tan = tanCC != null ? tanCC.GetComponent<VoxelCharacter>() : null;
            if (tan != null) AppendSubject("tan suit", tan, false);
            return sb.ToString();
        }

        /// <summary>One fighter's full readout — pose · combat telemetry ·
        /// beat bar · engagement flow chart · sensor measurements. Rendered
        /// for BOTH duel participants so cover picks, beat rhythms, reloads
        /// and orbit progress compare side-by-side. `isVinny` gates the
        /// possess-only annotations (POSSESSED tag, threat pin, possess
        /// reload hints).</summary>
        void AppendSubject(string label, VoxelCharacter ch, bool isVinny)
        {
            var anim = ch.GetComponent<CharacterAnimation>();
            var sensor = ch.GetComponent<CoverSensor>();
            var cc = ch.GetComponent<CombatController>();
            Vector3 p = ch.transform.position;

            sb.AppendLine($"── {label} ──────────────");
            bool ducked = anim != null && anim.duck > 0.02f;
            sb.AppendLine($"pos ({p.x:F1}, {p.z:F1}) · pose {(anim != null ? anim.EffectiveState.ToString() : "?")}"
                        + (ducked ? $" · duck {PossessController.CrouchLabel(anim.duck)}" : " · standing (no duck)")
                        + (isVinny && PossessController.Active ? " · POSSESSED" : "")
                        + (Time.timeScale < 0.999f ? $" · ts {Time.timeScale:F2}" : ""));
            var rigComp = ch.GetComponent<CharacterRig>();
            if (rigComp != null)
                sb.AppendLine($"rig controllable {(rigComp.Controllable ? "Y" : "n")}"
                            + $" · depth-ease {(rigComp.DepthEaseActive ? $"→ {rigComp.DepthTarget:F2}" : "off")}");

            // ── Combat telemetry: state, destination cover, exposure probe,
            // solver convergence, cylinder, last-seen. Exposure line shows at
            // setup too (tested>0 means a manual setup-phase probe ran).
            var solver = ch.GetComponent<ArmAimSolver>();
            sb.AppendLine("── combat ──────────────");
            if (cc == null) { sb.AppendLine("(no CombatController)"); }
            else
            {
                sb.AppendLine($"{cc.State}{(cc.enabled ? "" : " · CC DISABLED")} · {cc.LastResult}"
                            + (cc.Searching ? $" · SEARCH {cc.SearchProgress:F0}°" : ""));
                if (cc.State != CombatController.CombatState.Idle)
                {
                    sb.AppendLine($"cover {cc.CoverObstacleId ?? "—"} · {cc.CoverWallClass}"
                                + $" · reqDuck {cc.TargetDuck:F2}");
                    {
                        // XZ gap between the anchor (grid center = Feet) and
                        // the committed destination — discriminates "never
                        // arrived" from "arrived but pose folds mass off it".
                        var vd = ch.Dims;
                        Vector3 fc = p + new Vector3(vd.x * ch.voxelSize * 0.5f,
                                                     0f, vd.z * ch.voxelSize * 0.5f);
                        float off = Vector2.Distance(new Vector2(fc.x, fc.z),
                                                     new Vector2(cc.CoverPoint.x, cc.CoverPoint.z));
                        sb.AppendLine($"off {off:F2}m (anchor→cover) {(off > cc.arriveEps ? "⚠ NOT ON POINT" : "on point")}");
                    }
                    sb.AppendLine($"cadence {cc.fireInterval:F1}s · next shot {cc.NextFireIn:F1}s"
                                + $" · shots {cc.ShotsFired}"
                                + (cc.Reloading ? $" · +1 in {cc.NextRoundIn:F1}s · reload {cc.ReloadLeft:F1}s left" : ""));
                    if (cc.Beat != CombatController.EngagementBeat.None)
                        sb.AppendLine($"beat {cc.Beat}"
                                    + (cc.BeatLeft > 0f ? $" · {cc.BeatLeft:F1}s left" : "")
                                    + (cc.Beat == CombatController.EngagementBeat.Rise
                                       || cc.Beat == CombatController.EngagementBeat.Engaged
                                       ? $" · riseDuck {cc.RiseDuck:F2}" : ""));
                    if (cc.LastRiseDiag != null)
                        sb.AppendLine($"rise {cc.LastRiseDiag}");
                    if (cc.LastRelocateDiag != null && cc.LastRelocateDiag != "—")
                        sb.AppendLine($"relocate {cc.LastRelocateDiag}");
                }
                // Possess shares the same wheel — surface reload state outside
                // combat too (LMB spends rounds, R seats them back).
                else if (cc.Reloading)
                    sb.AppendLine($"reloading — +1 in {cc.NextRoundIn:F1}s · full in {cc.ReloadLeft:F1}s");
                else if (isVinny && PossessController.Active && cc.Cylinder <= 0)
                    sb.AppendLine("cylinder empty — press R to reload");
                // Cover-pick scan verdict — same numbers the tab used to show.
                if (cc.lastStats.tested > 0)
                {
                    var st = cc.lastStats;
                    sb.Append($"scan {st.obstacles} obs → {st.tested} pts · {st.valid} valid");
                    if (st.reasons != null && st.reasons.Count > 0)
                    {
                        int shown = 0;
                        foreach (var kv in st.reasons)
                        {
                            if (shown++ == 3) { sb.Append(" …"); break; }
                            sb.Append($" {kv.Key}×{kv.Value}");
                        }
                    }
                    sb.AppendLine();
                }
                var v = cc.Visibility;
                if (v.tested > 0)
                    sb.AppendLine($"exposure {v.visible}/{v.tested} ({v.fraction:P0})"
                                + $" · armed {(cc.ExposureArmed ? "Y" : "n")}"
                                + $" · cyl {cc.Cylinder}/{cc.cylinderCapacity}");
                if (solver != null && (solver.IsAiming || solver.Blend > 0.02f))
                    sb.AppendLine($"aim err {solver.LastAngularErrorDeg:F1}° · blend {solver.Blend:F2}"
                                + $" · dist {solver.LastTargetDistanceM:F1}m");
                if (cc.LastSeenTarget.HasValue)
                    sb.AppendLine($"lastSeen ({cc.LastSeenTarget.Value.x:F0},{cc.LastSeenTarget.Value.z:F0})"
                                + $" · {Time.unscaledTime - cc.LastSeenAt:F1}s ago");
            }

            // ── DICE readout — when the dice frame owns the fighter the
            // authored beat bar + engagement-flow chart are inert (TickBeat
            // early-returns); surface the turn engine + per-line roll log.
            if (cc != null && cc.diceArbiter)
            {
                sb.AppendLine("── dice ────────────────");
                sb.AppendLine(
                    (cc.diceManual ? (cc.diceMimic ? "MANUAL·mimic" : "MANUAL") : "auto")
                    + $" · quantum #{cc.DiceCycle}"
                    + $" · face {cc.DiceFace} ({CombatController.DiceFaceNames[cc.DiceFace]})"
                    + (cc.DiceSubRoll > 0 ? $" · sub {cc.DiceSubRoll}/6" : "")
                    + $" · {(cc.DiceAwaiting ? $"await {cc.DiceAwaitText}" + (cc.DiceRollReadyIn > 0f ? $" ({cc.DiceRollReadyIn:F1}s)" : " · READY") : "executing…")}");
                sb.AppendLine($"verdict {cc.DiceVerdict}");
                // Rolling log — ROLLED (die) / outcome (plain english) /
                // next (what the next roll decides), colored by kind.
                string[] kindCol = { "#aeb8c4", "#ffb35c", "#9fd89f", "#ff8f7a" };
                int dFrom = Mathf.Max(0, cc.DiceLog.Count - 6);
                for (int di = cc.DiceLog.Count - 1; di >= dFrom; di--)
                {
                    var st = cc.DiceLog[di];
                    string kc = kindCol[Mathf.Clamp(st.kind, 0, 3)];
                    sb.AppendLine($"<color=#8fb8d8>t{st.t:F1}</color> <color={kc}><b>ROLLED {st.rolled}</b></color>");
                    sb.AppendLine($"   → {st.outcome}");
                    sb.AppendLine($"   <color=#8fb8d8>next: {st.next}</color>");
                }
            }
            else
            {
            // ── The active beat's dwell, as its own section: a draining
            // countdown so every beat is watchable at the debug tempo.
            sb.AppendLine("── beat ────────────────");
            if (cc == null) { sb.AppendLine("(no CombatController)"); }
            else if (cc.Beat != CombatController.EngagementBeat.None)
            {
                float frac = cc.BeatDwellNow > 0f ? Mathf.Clamp01(cc.BeatLeft / cc.BeatDwellNow) : 0f;
                int cells = Mathf.RoundToInt(frac * 10f);
                sb.AppendLine($"<color=#ffb35c>{cc.Beat.ToString().ToUpperInvariant()}</color>"
                            + $" {new string('▓', cells)}{new string('░', 10 - cells)}"
                            + $" {cc.BeatLeft:F1}s / {cc.BeatDwellNow:F1}s"
                            + (cc.Beat == CombatController.EngagementBeat.Settle
                               ? $" → next: <color=#9ff2a0>{cc.NextAfterSettle}</color>" : ""));
            }
            else
                sb.AppendLine($"parked · {cc.State}");
            // ── Engagement flow: the combat pipeline as a state chart — a
            // node lights when that stage is live, greys when the chain has
            // stalled there. Movement lane and fire lane run in parallel;
            // the numbers above explain WHY a greyed node isn't lit.
            sb.AppendLine("── engagement flow ──────");
            if (cc == null) { sb.AppendLine("(no CombatController)"); }
            else
            {
                // Local: a stage node, lit green when live (FIRE/RELOAD amber).
                string N(string s, bool on, bool hot = false)
                    => on ? (hot ? $"<color=#ffb35c>{s}</color>" : $"<color=#9ff2a0>{s}</color>")
                          : $"<color=#596069>{s}</color>";
                const string A = "<color=#3f464d>▸</color>";
                var cs8 = cc.State;
                // The seek slot relabels RELOCATE while a relocation drive is
                // live — same phase (SeekingCover), different provenance.
                sb.AppendLine($"{N("IDLE", cs8 == CombatController.CombatState.Idle)} {A} "
                            + $"{N(cc.Relocating ? "RELOCATE" : "SEEK", cs8 == CombatController.CombatState.SeekingCover)} {A} "
                            + $"{N("COVER", cs8 == CombatController.CombatState.InCover)}"
                            + $"   │   {N("HOLD", cs8 == CombatController.CombatState.HoldPosition)} (no cover)");
                // The beat lane — the authored rhythm inside COVER (spec:
                // ENGAGEMENT_BEATS.md). SETTLE appears twice = the seam on
                // both sides of the rise-check; both light during any settle.
                var bt = cc.Beat;
                sb.AppendLine($"{N("SETTLE", bt == CombatController.EngagementBeat.Settle, true)} {A} "
                            + $"{N("TOPUP", bt == CombatController.EngagementBeat.TopUp)} {A} "
                            + $"{N("SETTLE", bt == CombatController.EngagementBeat.Settle, true)} {A} "
                            + $"{N("RISE", bt == CombatController.EngagementBeat.Rise)} {A} "
                            + $"{N("ENGAGED", bt == CombatController.EngagementBeat.Engaged, true)} {A} "
                            + $"{N("DUCK", bt == CombatController.EngagementBeat.Duck)}");
                // The orbit-search lane — SEARCH lights the moment a miss
                // arms the driver and stays lit until a reacquire or
                // ExitCombat. ORBIT shows accumulated bearing around the
                // anchor; FLANK lights once the 90° offset is satisfied
                // (the peeks between hops still light the beat lane above —
                // the rhythm is unchanged, the orbit is just its
                // relocation source).
                sb.AppendLine($"{N("SEARCH", cc.Searching, true)} {A} "
                            + $"ORBIT {(cc.Searching ? cc.SearchProgress.ToString("F0") : "0")}°"
                            + (cc.SearchLegOut ? (cc.OrbitLegRecon ? " ·recon" : " ·transit") : "")
                            + $" {A} {N("FLANK 90°", cc.Searching && cc.SearchProgress >= 90f)}");
                bool inCombat = cs8 != CombatController.CombatState.Idle;
                // PROBE lights whenever a probe is actively producing results:
                // the combat tick, or the setup-phase manual preview (probe-rays
                // toggle on). ARMED onward stays combat-gated — at setup the
                // chain honestly stops at EXPOSED: "can see him, gun not out".
                bool probing = cc.Visibility.tested > 0
                            && (inCombat || (sensor != null && sensor.showTargetViz));
                // Held through the same disarm-hold window — a single dropped
                // probe mustn't flicker the node.
                bool exposed = probing && (cc.Visibility.visible > 0
                            || Time.unscaledTime - cc.LastSeenAt <= cc.disarmHoldSec);
                bool converged = cc.ExposureArmed && solver != null && solver.IsAiming
                              && solver.Blend >= 0.7f && solver.LastAngularErrorDeg <= cc.aimGateDeg;
                bool justFired = cc.LastShotAt >= 0f && Time.unscaledTime - cc.LastShotAt < 0.4f;
                // The wheel: ● = chambered, ○ = spent — drains per FIRE,
                // seats back one-at-a-time during RELOAD (amber while loading).
                bool reloading = cc.Reloading;
                string dots = reloading
                    ? $"<color=#ffb35c>{new string('●', cc.Cylinder)}{new string('○', cc.cylinderCapacity - cc.Cylinder)}</color>"
                    : $"<color=#9ff2a0>{new string('●', cc.Cylinder)}</color><color=#596069>{new string('○', cc.cylinderCapacity - cc.Cylinder)}</color>";
                sb.AppendLine($"{N("PROBE", probing)} {A} {N("EXPOSED", exposed)} {A} "
                            + $"{N("ARMED", cc.ExposureArmed)} {A} {N("CONVERGED", converged)} {A} "
                            + $"{N("FIRE", justFired, true)}{(!justFired && cc.FireHold != null ? $"·{cc.FireHold}" : "")} {A} {N("RELOAD", reloading, true)} {dots}");
            }
            }   // end authored beat/flow — skipped entirely under dice

            sb.AppendLine("── sensor ──────────────");
            if (sensor == null) { sb.AppendLine("(no CoverSensor)"); }
            else
            {
                sb.AppendLine(sensor.HasThreat
                    ? $"threat ({sensor.Threat.Value.x:F0},{sensor.Threat.Value.z:F0})"
                      + (isVinny && PossessController.ThreatPinned ? " · pinned" : "")
                    : "threat — none");
                if (cc != null && cc.diceArbiter)
                {   // Dice-relevant sensor surface — the field the sub-rolls
                        // pick from; heights/wall classes/reject reasons are
                        // authored-chain tuning noise here.
                        sb.AppendLine($"field r{sensor.radius:F0}m · {sensor.Field.N}² · cell {sensor.Field.CellSize:F2}m");
                        sb.AppendLine($"cells tested {sensor.stats.tested} · valid {sensor.stats.valid}"
                                    + $" · pick {(sensor.chosenIdx >= 0 ? "#" + sensor.chosenIdx : "—")}");
                }
                else
                {
                sb.AppendLine($"heights stand {sensor.HStand:F2} · crouch {sensor.HShallow:F2}→{sensor.HDeep:F2}"
                            + (sensor.HeightsMeasured ? "" : " · FALLBACK"));
                sb.AppendLine($"support y{sensor.SupportY:F2} · walls {sensor.stats.obstacles}→"
                            + $"{sensor.walls.Count} ({sensor.stats.wallsFull}F {sensor.stats.wallsCrouch}C {sensor.stats.wallsLo}L)");
                if (!string.IsNullOrEmpty(sensor.PickInfo)) sb.AppendLine("pick: " + sensor.PickInfo);
                sb.AppendLine($"field r{sensor.radius:F0}m · {sensor.Field.N}² · cell {sensor.Field.CellSize:F2}m"
                            + (sensor.Field.CellSize > 0.5f ? " · COARSE (no halo)" : ""));
                sb.AppendLine($"cells tested {sensor.stats.tested} · valid {sensor.stats.valid}"
                            + $" · pick {(sensor.chosenIdx >= 0 ? "#" + sensor.chosenIdx : "—")}");
                if (sensor.stats.reasons != null && sensor.stats.reasons.Count > 0)
                {
                    sb.Append("rejects:");
                    int shown = 0;
                    foreach (var kv in sensor.stats.reasons)
                    {
                        if (shown++ == 4) { sb.Append(" …"); break; }
                        sb.Append($" {kv.Key}×{kv.Value}");
                    }
                    sb.AppendLine();
                }
                }
            }
        }

        void OnGUI()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            OnGUIInner();
            Prof((float)sw.Elapsed.TotalMilliseconds);
        }

        void OnGUIInner()
        {
            if (!Enabled || Event.current.type != EventType.Repaint) return;
            if (bgTex == null)
            {
                bgTex = new Texture2D(1, 1);
                bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                bgTex.Apply();
            }
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    richText = true,   // flow-chart nodes: lit vs greyed stages
                    padding = new RectOffset(8, 8, 6, 6),
                    normal = { textColor = new Color(0.85f, 0.95f, 1f, 1f) },
                };
            }
            GUI.depth = -100;   // above the debug window — snapshot surface
            // Content + height are cached per text rebuild (5Hz) — allocating a
            // GUIContent and re-measuring on every Repaint showed up in the
            // GC-alloc profile.
            if (content.text != cached) { content.text = cached; cachedH = -1f; }
            if (cachedH < 0f) cachedH = style.CalcHeight(content, panelWidth) + 12f;
            float h = cachedH;
            Rect r = new Rect(Screen.width - panelWidth - 36f,
                              Screen.height * 0.5f - h * 0.5f, panelWidth, h);
            GUI.DrawTexture(r, bgTex);
            GUI.Label(r, content, style);
        }
    }
}
