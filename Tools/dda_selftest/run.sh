#!/bin/bash
# Offline self-test for the CPU voxel raycaster: Assets/Scripts/Sim/VoxelDda.cs + SolidVolumeSet.cs.
#
# Compiles the two source files together with Program.cs using the Roslyn and .NET runtime bundled with the
# project's Unity editor (no Unity process, no .NET SDK needed) and runs ~150k checks:
# DDA vs exhaustive brute force, axis-aligned / edge cases, volume registry, yawed volumes, and real shots
# through the shooting-range asset. Build output goes to a temp folder; nothing is written into the repo.
#
# Usage: Tools/dda_selftest/run.sh [range.json]   (default: VoxelAssetStudio/JSON Models In Progress/shooting_range.json)
# Override the editor location with UNITY_EDITOR_DATA="<...>/Editor/Data" (default: Hub install matching ProjectVersion.txt).
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

OUT="${TMPDIR:-/tmp}/steelcity_dda_selftest"
mkdir -p "$OUT"
RSP="$OUT/build.rsp"
{
  echo "-nologo"; echo "-nostdlib"; echo "-target:exe"; echo "-langversion:9.0"
  echo "-out:$(win "$OUT")/selftest.dll"
  for f in "$FW"/System.*.dll "$FW"/netstandard.dll "$FW"/mscorlib.dll "$FW"/Microsoft.CSharp.dll; do
    case "$f" in *.Native.dll) continue;; esac
    echo "-r:\"$(win "$f")\""
  done
  echo "-r:\"$(win "$DATA/Managed/UnityEngine/UnityEngine.CoreModule.dll")\""
  echo "\"$(win "$ROOT/Assets/Scripts/Sim/VoxelDda.cs")\""
  echo "\"$(win "$ROOT/Assets/Scripts/Sim/SolidVolumeSet.cs")\""
  echo "\"$(win "$ROOT/Assets/Scripts/Sim/VoxelDamageMath.cs")\""
  echo "\"$(win "$ROOT/Tools/dda_selftest/Program.cs")\""
} > "$RSP"

"$DOTNET" "$(win "$CSC")" "@$(win "$RSP")"
cp "$DATA/Managed/UnityEngine/UnityEngine.CoreModule.dll" "$OUT/"
printf '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' "$FWVER" > "$OUT/selftest.runtimeconfig.json"

RANGE="${1:-$ROOT/VoxelAssetStudio/JSON Models In Progress/shooting_range.json}"
"$DOTNET" "$(win "$OUT")/selftest.dll" "$(win "$RANGE")"
