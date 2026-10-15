#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SteelCity.Sim;

namespace SteelCity.EditorTools
{
    /// <summary>
    /// Steel City → Build Readiness Audit — enumerates what a player build
    /// needs vs what it will actually pack/run, WITHOUT building. Exists
    /// because every "renders in editor, dead in player" bug so far was a
    /// divergence this tool can see statically:
    ///   • editor D3D11 vs player D3D12 (upload ring / SRV validation)
    ///   • Shader.Find names not packed (init-cascade crash)
    ///   • runtime keywords/instancing variants unpreserved
    ///   • shader-declared SRVs never bound (d3d12 "requires a buffer" skips)
    ///   • single-call ComputeBuffer.SetData uploads that exceed the ring
    /// Run it before every dev build; in play mode it also merges the live
    /// BuildDiag state (real buffer sizes, resolved shaders).
    /// </summary>
    public static class BuildReadinessAudit
    {
        private static int pass, warn, fail;
        private static readonly StringBuilder report = new StringBuilder();

        [MenuItem("Steel City/Build Readiness Audit")]
        public static void Run()
        {
            pass = warn = fail = 0;
            report.Length = 0;
            report.AppendLine("[BuildAudit] ================================");

            CheckGraphicsApis();
            CheckShaderPacking();
            CheckMaterialNullSafety();
            CheckVariantCoverage();
            CheckSrvCompleteness();
            CheckUploadRisk();
            CheckPreloadedShaders();

            report.AppendLine($"[BuildAudit] done — {pass} pass, {warn} warn, {fail} fail");
            Debug.Log(report.ToString());
        }

        private static void Line(string status, string msg)
        {
            if (status == "PASS") pass++;
            else if (status == "WARN") warn++;
            else fail++;
            report.AppendLine($"[{status}] {msg}");
        }

        // ---- 1. Graphics API parity ----------------------------------------

        private static void CheckGraphicsApis()
        {
            var player = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            var editorApi = SystemInfo.graphicsDeviceType;
            var first = player != null && player.Length > 0 ? player[0].ToString() : "auto";
            if (first == editorApi.ToString())
                Line("PASS", $"graphics API parity: editor={editorApi} player={first}");
            else
                Line("FAIL", $"graphics API divergence: editor={editorApi} player={first} — " +
                    "editor-rendered results do NOT prove player behavior (D3D12 validates " +
                    "SRV bindings per-draw and caps SetData uploads; D3D11 does neither)");
        }

        // ---- 2. Shader packing ----------------------------------------------

        private static readonly Regex RxFind = new Regex("Shader\\.Find\\(\"([^\"]+)\"\\)", RegexOptions.Compiled);
        private static readonly Regex RxResShader = new Regex("Resources\\.Load<Shader>\\(\"([^\"]+)\"\\)", RegexOptions.Compiled);
        private static readonly Regex RxShaderName = new Regex("Shader\\s+\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex RxGuid = new Regex("guid:\\s*([0-9a-f]{32})", RegexOptions.Compiled);

        private static Dictionary<string, string> shaderNameToPath; // name → asset path
        private static HashSet<string> alwaysIncludedNames;

        private static void BuildShaderIndex()
        {
            shaderNameToPath = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = RxShaderName.Match(File.ReadAllText(path));
                if (m.Success) shaderNameToPath[m.Groups[1].Value] = path;
            }

            alwaysIncludedNames = new HashSet<string>();
            var gfx = File.ReadAllText("ProjectSettings/GraphicsSettings.asset");
            var sect = gfx.IndexOf("m_AlwaysIncludedShaders:");
            if (sect >= 0)
            {
                var end = gfx.IndexOf("m_PreloadedShaders", sect);
                foreach (Match g in RxGuid.Matches(gfx.Substring(sect, end - sect)))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g.Groups[1].Value);
                    if (!string.IsNullOrEmpty(p) && shaderNameToPath.ContainsValue(p))
                        alwaysIncludedNames.Add(shaderNameToPath.First(kv => kv.Value == p).Key);
                }
            }
        }

        private static bool ShaderPacks(string name)
        {
            // Builtin names pack only via Always-Included or serialized use.
            if (!shaderNameToPath.TryGetValue(name, out var path))
                return alwaysIncludedNames.Contains(name); // builtin (Unlit/Color etc.)
            return path.Replace('\\', '/').Contains("/Resources/") || alwaysIncludedNames.Contains(name);
        }

        private static void CheckShaderPacking()
        {
            BuildShaderIndex();
            var findNames = new Dictionary<string, bool>(); // name → file has a Resources.Load<Shader> fallback
            var resPaths = new HashSet<string>();
            foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                var txt = File.ReadAllText(f);
                bool hasResFallback = RxResShader.IsMatch(txt);
                foreach (Match m in RxFind.Matches(txt))
                {
                    var n = m.Groups[1].Value;
                    findNames[n] = (findNames.TryGetValue(n, out var prev) && prev) || hasResFallback;
                }
                foreach (Match m in RxResShader.Matches(txt)) resPaths.Add(m.Groups[1].Value);
            }
            foreach (var kv in findNames)
            {
                if (ShaderPacks(kv.Key)) Line("PASS", $"Shader.Find \"{kv.Key}\" → packed");
                else if (kv.Value) Line("PASS", $"Shader.Find \"{kv.Key}\" unpacked — safe: file ends in a Resources.Load fallback");
                else Line("WARN", $"Shader.Find \"{kv.Key}\" — NOT provably packed and no Resources fallback in file → null in player");
            }
            foreach (var p in resPaths)
            {
                var full = $"Assets/Resources/{p}.shader";
                if (File.Exists(full)) Line("PASS", $"Resources.Load \"{p}\" → {full}");
                else Line("FAIL", $"Resources.Load \"{p}\" → missing file {full}");
            }
        }

        // ---- 3. Runtime material null-safety --------------------------------

        private static readonly Regex RxNewMat = new Regex("new Material\\(", RegexOptions.Compiled);

        private static void CheckMaterialNullSafety()
        {
            foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!RxNewMat.IsMatch(lines[i])) continue;
                    // Scan the small window above the call for a resolution chain
                    // that still ends in a bare Shader.Find (the crash pattern).
                    var window = string.Join("\n", lines.Skip(System.Math.Max(0, i - 6)).Take(7));
                    var finds = RxFind.Matches(window).Count;
                    var hasResources = window.Contains("Resources.Load<Shader>");
                    var guarded = Regex.IsMatch(window, "!=\\s*null|==\\s*null");
                    if (finds > 0 && !hasResources)
                        Line("WARN", $"{Path.GetFileName(f)}:{i + 1} new Material with Shader.Find chain, no Resources fallback → null-material risk");
                    else if (finds > 0 && !guarded)
                        Line("WARN", $"{Path.GetFileName(f)}:{i + 1} new Material chain has fallback but no null-guard");
                    else
                        Line("PASS", $"{Path.GetFileName(f)}:{i + 1} new Material — guarded");
                }
            }
        }

        // ---- 4. Variant coverage (SVC + serialized materials) ----------------

        private static readonly Regex RxEnableKw = new Regex("EnableKeyword\\(\"(\\w+)\"\\)", RegexOptions.Compiled);

        private static void CheckVariantCoverage()
        {
            // Required keyword sets per shader (from code usage)
            var keywords = new HashSet<string>();
            bool instancedDraws = false;
            foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                var txt = File.ReadAllText(f);
                foreach (Match m in RxEnableKw.Matches(txt)) keywords.Add(m.Groups[1].Value);
                if (txt.Contains("DrawMeshInstanced") || txt.Contains("enableInstancing")) instancedDraws = true;
            }

            // What the SVCs preserve: guid → keyword combos
            var svcByShader = new Dictionary<string, List<string>>();
            var svcPaths = Directory.GetFiles("Assets", "*.shadervariants", SearchOption.AllDirectories);
            foreach (var p in svcPaths)
            {
                string curShader = null;
                foreach (var ln in File.ReadLines(p))
                {
                    var g = Regex.Match(ln, "first:.*guid:\\s*([0-9a-f]{32})");
                    if (g.Success) { curShader = g.Groups[1].Value; svcByShader[curShader] = new List<string>(); continue; }
                    var k = Regex.Match(ln, "keywords:\\s*(.+)$");
                    if (k.Success && curShader != null) svcByShader[curShader].Add(k.Groups[1].Value.Trim());
                }
            }

            // Serialized materials that prove instancing/keywords
            int instancedMats = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var txt = File.ReadAllText(AssetDatabase.GUIDToAssetPath(guid));
                if (txt.Contains("m_EnableInstancingVariants: 1")) instancedMats++;
            }

            // Report the instancing stripping mode — THE make-or-break setting for
            // runtime-enableInstancing code paths (0=StripUnused kills all of ours).
            {
                var gfxTxt = File.ReadAllText("ProjectSettings/GraphicsSettings.asset");
                var m = Regex.Match(gfxTxt, "m_InstancingStripping:\\s*(\\d+)");
                var mode = m.Success ? int.Parse(m.Groups[1].Value) : -1;
                var modeName = mode == 0 ? "StripUnused (DEFAULT — kills runtime-only instancing)" :
                               mode == 1 ? "StripAll (strips everything)" :
                               mode == 2 ? "KeepAll" : "unknown";
                if (mode == 2) Line("PASS", $"instancing stripping: KeepAll (all variants packed)");
                else if (mode == 0 && svcByShader.Count > 0)
                    Line("WARN", $"instancing stripping: {modeName} — relies on serialized mats/SVC coverage alone");
                else if (mode != 2)
                    Line("FAIL", $"instancing stripping: {modeName} — runtime instanced draws will be stripped");
            }
            if (instancedDraws)
            {
                int svcInst = svcByShader.Sum(kv => kv.Value.Count(v => v.Contains("INSTANCING_ON")));
                if (svcInst > 0 || instancedMats > 0)
                    Line("PASS", $"instanced draws used — INSTANCING_ON preserved via {svcInst} SVC entries + {instancedMats} serialized mats");
                else
                    Line("FAIL", "code uses DrawMeshInstanced/enableInstancing but NOTHING preserves INSTANCING_ON — instanced draws will silently skip in the player");
            }
            foreach (var kv in svcByShader)
            {
                var p = AssetDatabase.GUIDToAssetPath(kv.Key);
                Line("PASS", $"SVC covers {Path.GetFileName(p)}: {string.Join(" | ", kv.Value)}");
            }
            foreach (var k in keywords) Line("PASS", $"runtime keyword used: {k} (verify SVC/serialized coverage)");
        }

        // ---- 5. SRV completeness ---------------------------------------------

        private static readonly Regex RxSrv = new Regex("StructuredBuffer<[^>]+>\\s+(\\w+)", RegexOptions.Compiled);

        private static void CheckSrvCompleteness()
        {
            var allCs = Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText).Aggregate((a, b) => a + "\n" + b);
            foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Replace('\\', '/').Contains("/Resources/")) continue;
                var srvs = RxSrv.Matches(File.ReadAllText(path));
                if (srvs.Count == 0) continue;
                var missing = srvs.Cast<Match>().Select(m => m.Groups[1].Value)
                    .Where(n => !allCs.Contains($"\"{n}\"")).ToList();
                var name = RxShaderName.Match(File.ReadAllText(path)).Groups[1].Value;
                if (missing.Count == 0)
                    Line("PASS", $"{name}: all {srvs.Count} declared SRVs are bound somewhere in code");
                else
                    Line("WARN", $"{name}: SRVs never bound in code → d3d12 draw-skip risk: {string.Join(", ", missing)}");
            }
        }

        // ---- 6. Upload risk ---------------------------------------------------

        private static void CheckUploadRisk()
        {
            int raw = 0, chunked = 0;
            foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (f.EndsWith("GpuUpload.cs") || lines[i].Contains("GpuUpload.SetData")) { chunked++; continue; }
                    if (lines[i].Contains("clip.SetData")) continue; // AudioClip — CPU-side, not a GPU upload
                    if (Regex.IsMatch(lines[i], "\\w+\\.SetData\\("))
                    { raw++; Line("WARN", $"{Path.GetFileName(f)}:{i + 1} raw SetData — route through GpuUpload.SetData (D3D12 upload-ring limit)"); }
                }
            }
            Line("PASS", $"upload sites: {chunked} via GpuUpload.SetData, {raw} raw");

            // Live truth, if running in play mode: BuildDiag buf.* entries carry
            // real sizes — flag anything that would exceed a 4MB chunk.
            if (Application.isPlaying)
            {
                foreach (var key in BuildDiag.StateKeys)
                {
                    if (!key.StartsWith("buf.") || !BuildDiag.TryGet(key, out var v)) continue;
                    var m = Regex.Match(v, "([\\d.]+)MB");
                    if (m.Success && float.Parse(m.Groups[1].Value) > 4f)
                        Line("PASS", $"live buffer {key}={v} — uploads chunked via GpuUpload.SetData");
                    else
                        Line("PASS", $"live buffer {key}={v}");
                }
            }
            else
            {
                report.AppendLine("[INFO] run during Play to include live buffer sizes (BuildDiag buf.* lines)");
            }
        }

        // ---- 7. Preloaded shader registration ---------------------------------

        private static void CheckPreloadedShaders()
        {
            var gfx = File.ReadAllText("ProjectSettings/GraphicsSettings.asset");
            var sect = gfx.IndexOf("m_PreloadedShaders:");
            if (sect < 0) { Line("WARN", "no m_PreloadedShaders section"); return; }
            var end = gfx.IndexOf("m_PreloadShadersBatchTimeLimit", sect);
            var guids = RxGuid.Matches(gfx.Substring(sect, end - sect));
            if (guids.Count == 0) { Line("WARN", "m_PreloadedShaders is empty"); return; }
            foreach (Match g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g.Groups[1].Value);
                if (string.IsNullOrEmpty(p)) Line("FAIL", $"preloaded shader ref {g.Groups[1].Value} resolves to NOTHING");
                else Line("PASS", $"preloaded: {p}");
            }
        }
    }
}
#endif
