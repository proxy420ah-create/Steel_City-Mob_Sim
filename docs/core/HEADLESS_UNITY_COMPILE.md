# Headless Unity Compile — full Assembly-CSharp check without launching the editor

**Status**: ✅ Proven (used since the debris-architecture sessions)
**Cost**: ~5–10 s vs. a Unity domain reload — safe to run after every code edit
**Scope**: syntax + type + reference errors for **runtime** code (`Assets/Scripts/**`). Editor scripts (`UnityEditor` APIs) will NOT resolve — see Gotchas.

---

## What it is

The Unity editor install bundles everything needed to compile the project's C# without launching Unity:

- `Editor/Data/DotNetSdkRoslyn/csc.dll` — the Roslyn compiler Unity itself invokes
- `Editor/Data/NetCoreRuntime/dotnet` — a private .NET runtime to run csc
- `Editor/Data/Managed/UnityEngine/*.dll` — every engine module (CoreModule, PhysicsModule, IMGUIModule, …)
- `<project>/Library/ScriptAssemblies/*.dll` — compiled **package** assemblies (InputSystem, URP, TMPro…) from the last Unity import
- `Editor/Data/NetCoreRuntime/shared/Microsoft.NETCore.App/<ver>/*.dll` — the BCL (needed because we compile `-nostdlib`)

Same trick `Tools/dda_selftest/run.sh` uses for the `VoxelDda`+`SolidVolumeSet` subset — this generalizes it to the entire assembly.

## Recipe (Git Bash)

```bash
cd "$PROJ"   # project root
DATA="/c/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Data"
WDATA="C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Data"
WPROJ="C:/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim"
DOTNET="$DATA/NetCoreRuntime/dotnet"
CSC="$DATA/DotNetSdkRoslyn/csc.dll"
FW="$(ls -d "$DATA"/NetCoreRuntime/shared/Microsoft.NETCore.App/*/ | head -1)"
WFW="$WDATA/NetCoreRuntime/shared/Microsoft.NETCore.App/$(basename "$FW")"

# sources — ALL runtime scripts, quoted (paths contain spaces)
find Assets/Scripts -name "*.cs" | sed "s|^|\"$WPROJ/|;s|$|\"|" > /tmp/srcs.txt

# references — quoted, one per line:
#   1) .NET BCL   2) every UnityEngine module   3) package DLLs (NOT Assembly-CSharp)
{
  for f in "$WFW"/System.*.dll "$WFW"/netstandard.dll "$WFW"/mscorlib.dll "$WFW"/Microsoft.CSharp.dll; do
    case "$f" in *.Native.dll) continue;; esac
    [ -f "$f" ] && echo "-r:\"$f\""
  done
  for f in "$WDATA"/Managed/UnityEngine/*.dll; do echo "-r:\"$f\""; done
  for f in "$WPROJ"/Library/ScriptAssemblies/*.dll; do
    case "$f" in *Assembly-CSharp*|*CodeGen*) continue;; esac
    echo "-r:\"$f\""
  done
} > /tmp/refs.rsp

"$DOTNET" "$CSC" -nologo -nostdlib -target:library \
  -out:"$TMP/ac_check.dll" @/tmp/refs.rsp @/tmp/srcs.txt 2>&1 | grep -v ": warning" | head -30
```

Silent output = clean compile. Any `error CS####` lines are real compile errors with `file(line,col)` — same as the Unity Console.

## Gotchas (learned the hard way)

- **Quote every path.** The project path contains spaces (`ATSTradingDashboard Project/Cursor Workshop`). Unquoted rsp entries are split at each space → `CS2001` floods. `cygpath -m` is NOT reliably present — hand-write `C:/`-style absolute paths instead.
- **`-nostdlib` means you must supply the BCL yourself** — the `System.*.dll`/`netstandard`/`mscorlib` trio from the shared framework dir. Missing them = `System.Collections does not exist` on every file.
- **Exclude `Assembly-CSharp*.dll` and `*CodeGen*.dll`** from ScriptAssemblies refs — you'd be referencing the assembly being compiled (or its compiler-generated twins).
- **Editor code fails** — `UnityEditor.*` APIs live in `Managed/UnityEngine/UnityEditor*.dll` which we deliberately skip (the check is for runtime code). Files under `Editor/` folders or `#if UNITY_EDITOR` regions that don't compile standalone will error — expected; scope the source list to `Assets/Scripts` or add the UnityEditor DLLs if you ever need to check editor scripts.
- **Find the editor version from the project**, don't hardcode: `sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt` (see `run.sh` — it does this automatically and honors `UNITY_EDITOR_DATA` overrides).
- **`out` path must be Windows-style** for csc (`$TMP/ac_check.dll` works in Git Bash since `$TMP` is already `C:/…`).
- **Warnings are noise** for a check compile — filter with `grep -v ": warning"` but don't drop stderr entirely (errors share the stream).

## When to use vs. the real thing

| Use headless csc when… | Open Unity when… |
|---|---|
| Syntax/type/ref errors after an edit | Serialized-field wiring, prefab refs, .meta changes |
| Selftest-adjacent logic (`ShardPhysics`, `VoxelDamageMath`) | Shader/HLSL, compute kernels, scene changes |
| Fast iteration loops (compile per edit) | Anything that needs the play mode to observe |
| Verifying a refactor across many files | Verifying visuals — compile ≠ behavior |

Pair with `Tools/dda_selftest/run.sh`: csc proves it *builds*, the selftest proves the pure-math pieces *behave* — together they cover most of what a domain reload catches, minus seconds of Unity startup.
