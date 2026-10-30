#!/bin/bash
# Offline equivalence test + micro-benchmark for the CPU pose API in
# Assets/Scripts/Sim/VoxelCharacterAnimator.cs (PreparePose / ApplyPose / PoseRotation vs the
# legacy per-point algorithm). Same bundled-Roslyn trick as Tools/dda_selftest/run.sh — no Unity
# process, nothing written into the repo. Quaternion.Slerp is an engine intrinsic (skipped, counted).
#
# Usage: Tools/anim_selftest/run.sh        Override the editor with UNITY_EDITOR_DATA="<...>/Editor/Data".
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VER="$(sed -n 's/^m_EditorVersion: *//p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
DATA="${UNITY_EDITOR_DATA:-/c/Program Files/Unity/Hub/Editor/$VER/Editor/Data}"
RT="$DATA/NetCoreRuntime"
DOTNET="$RT/dotnet.exe"; [ -f "$DOTNET" ] || DOTNET="$RT/dotnet"
CSC="$DATA/DotNetSdkRoslyn/csc.dll"
FW="$(ls -d "$RT"/shared/Microsoft.NETCore.App/*/ | head -1)"; FW="${FW%/}"
FWVER="$(basename "$FW")"
win() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else echo "$1"; fi; }

OUT="${TMPDIR:-/tmp}/steelcity_anim_selftest"
mkdir -p "$OUT"
RSP="$OUT/build.rsp"
{
  echo "-nologo"; echo "-nostdlib"; echo "-target:exe"; echo "-langversion:9.0"; echo "-optimize+"
  echo "-out:$(win "$OUT")/selftest.dll"
  for f in "$FW"/System.*.dll "$FW"/netstandard.dll "$FW"/mscorlib.dll "$FW"/Microsoft.CSharp.dll; do
    case "$f" in *.Native.dll) continue;; esac
    echo "-r:\"$(win "$f")\""
  done
  echo "-r:\"$(win "$DATA/Managed/UnityEngine/UnityEngine.CoreModule.dll")\""
  echo "-r:\"$(win "$DATA/Managed/UnityEngine/UnityEngine.JSONSerializeModule.dll")\""
  echo "\"$(win "$ROOT/Assets/Scripts/Sim/VoxelCharacterAnimator.cs")\""
  echo "\"$(win "$ROOT/Tools/anim_selftest/Program.cs")\""
} > "$RSP"

"$DOTNET" "$(win "$CSC")" "@$(win "$RSP")"

cp "$DATA/Managed/UnityEngine/UnityEngine.CoreModule.dll" "$OUT/"
cp "$DATA/Managed/UnityEngine/UnityEngine.JSONSerializeModule.dll" "$OUT/"
printf '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' "$FWVER" > "$OUT/selftest.runtimeconfig.json"
"$DOTNET" "$(win "$OUT")/selftest.dll"
