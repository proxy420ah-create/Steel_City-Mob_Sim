using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Read-only cover-metrics overlay — right-center of screen, [F5] toggles.
    /// One cached label rebuilt ~5Hz (the cheap text-tab pattern), drawn over
    /// everything via GUI.depth so it survives HUD minimize for snapshots.
    /// Sections: Vinny pose/combat · threat · sensor measurements + scan ·
    /// possess-drive probe ring · Tan Suit combat state.
    /// Per UI_DEVELOPMENT_PITFALLS: all resolves are cached (≥0.5s cadence),
    /// nothing live-scans per GUI event.
    /// </summary>
    public class CoverMetricsPanel : MonoBehaviour
    {
        public static bool Enabled = true;

        [SerializeField] private float refreshHz = 5f;
        [SerializeField] private float panelWidth = 375f;

        private string cached = "cover metrics — waiting for a character…";
        private readonly GUIContent content = new GUIContent();
        private float cachedH = -1f;
        private float nextBuild;
        private Texture2D bgTex;
        private GUIStyle style;
        private CityMap3D cityMap;
        private float nextResolve;
        private CombatController tanCC;
        private readonly StringBuilder sb = new StringBuilder(1024);
        private static readonly string[] dirs = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

        void Awake() => cityMap = FindFirstObjectByType<CityMap3D>();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame)
            {
                Enabled = !Enabled;
                Debug.Log($"[CoverMetrics] panel {(Enabled ? "ON" : "OFF")} (F5)");
            }
            // Resolve Tan at a slow cadence — never per-GUI-event.
            if (Time.unscaledTime >= nextResolve)
            {
                nextResolve = Time.unscaledTime + 1f;
                if (tanCC == null)
                {
                    var go = GameObject.Find("Civilian_02");
                    if (go != null) tanCC = go.GetComponent<CombatController>();
                }
                else if (tanCC == null || !tanCC) tanCC = null;
            }
            if (Time.unscaledTime < nextBuild) return;
            nextBuild = Time.unscaledTime + 1f / refreshHz;
            cached = BuildText();
        }

        private string BuildText()
        {
            sb.Length = 0;
            var vinny = cityMap != null ? cityMap.SpawnedCharacter : null;
            sb.AppendLine("COVER METRICS · F5 to hide");
            if (vinny == null) { sb.Append("no spawned character"); return sb.ToString(); }

            var anim = vinny.GetComponent<CharacterAnimation>();
            var sensor = vinny.GetComponent<CoverSensor>();
            var cc = vinny.GetComponent<CombatController>();
            Vector3 p = vinny.transform.position;

            sb.AppendLine("── vinny ──────────────");
            bool ducked = anim != null && anim.duck > 0.02f;
            sb.AppendLine($"pos ({p.x:F1}, {p.z:F1}) · pose {(anim != null ? anim.EffectiveState.ToString() : "?")}"
                        + (ducked ? $" · duck {PossessController.CrouchLabel(anim.duck)}" : " · standing (no duck)")
                        + (PossessController.Active ? " · POSSESSED" : "")
                        + (Time.timeScale < 0.999f ? $" · ts {Time.timeScale:F2}" : ""));
            if (cc != null)
                sb.AppendLine($"combat {cc.State}{(cc.enabled ? "" : " · CC DISABLED — state frozen")}"
                            + $" · {cc.LastResult}");
            else sb.AppendLine("combat — (no CombatController)");
            var rigComp = vinny.GetComponent<CharacterRig>();
            if (rigComp != null)
                sb.AppendLine($"rig controllable {(rigComp.Controllable ? "Y" : "n")}"
                            + $" · depth-ease {(rigComp.DepthEaseActive ? $"→ {rigComp.DepthTarget:F2}" : "off")}");

            sb.AppendLine("── sensor ──────────────");
            if (sensor == null) { sb.AppendLine("(no CoverSensor)"); }
            else
            {
                sb.AppendLine(sensor.HasThreat
                    ? $"threat ({sensor.Threat.Value.x:F0},{sensor.Threat.Value.z:F0})"
                      + (PossessController.ThreatPinned ? " · pinned" : "")
                    : "threat — none");
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

            if (tanCC != null)
            {
                sb.AppendLine("── tan suit ────────────");
                sb.AppendLine($"{tanCC.State} · {tanCC.LastResult}");
            }
            return sb.ToString();
        }

        void OnGUI()
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
                    richText = false,
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
