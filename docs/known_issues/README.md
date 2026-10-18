# Known Issues — Steel City: Mob Sim

Bug tracker per Development Guidelines Rule 13. Bugs are cataloged by category in subfolders.

| Bug | Category | Status | Severity | Date |
|-----|----------|--------|----------|------|
| [Reference Preview Scale Ratio Stale](editor/REFERENCE_PREVIEW_SCALE.md) | editor | 🟢 FIXED | 🟡 HIGH | 2026-10-02 |
| [Walk Preview bobFn Crash + Formula Drift](editor/POSE_ENGINE_BOBFN_CRASH.md) | editor | 🟢 FIXED | 🔴 CRITICAL | 2026-10-02 |
| [Export Drops Animation Metadata](editor/EXPORT_DROPS_ANIM_METADATA.md) | editor | 🟢 FIXED | 🔴 CRITICAL | 2026-10-02 |
| [dummyMatrix Scale Leak](editor/DUMMY_MATRIX_SCALE_LEAK.md) | editor | 🟢 FIXED | 🟡 HIGH | 2026-10-03 |
| [Angle-Dependent Building Culling](rendering/ANGLE_DEPENDENT_BUILDING_CULLING.md) | rendering | 🟢 FIXED | 🔴 CRITICAL | 2026-10-08 |
| [Ctrl+Z After Tag Painting Removes Model](editor/UNDO_DESTROYS_MODEL_ON_TAG_STROKES.md) | editor | 🟢 FIXED | 🔴 CRITICAL | 2026-10-04 |
| [Reference Preview Survives Asset-Type Switch](editor/REFERENCE_PREVIEW_SURVIVES_MODE_SWITCH.md) | editor | 🟢 FIXED | 🟢 MEDIUM | 2026-10-05 |
| [Transforms Orphan Tag Layers](editor/TRANSFORMS_ORPHAN_TAG_LAYERS.md) | editor | 🟢 FIXED | 🟡 HIGH | 2026-10-06 |
| [Character Lifecycle Races — Weld/Pose Divergence](runtime/CHARACTER_LIFECYCLE_RACES.md) | runtime | 🟢 FIXED | 🔴 CRITICAL | 2026-10-05 |
| [Terrain Sector Overflow at 32x32](rendering/TERRAIN_SECTOR_OVERFLOW_AT_SCALE.md) | rendering | 🟢 FIXED | 🟡 HIGH | 2026-10-05 |
| [RoadGraph Links Across Open Water](runtime/ROADGRAPH_IGNORES_TERRAIN.md) | runtime | 🟢 FIXED | 🟡 HIGH | 2026-10-07 |
| [Sector Frustum Cull Drops Visible Terrain](rendering/SECTOR_FRUSTUM_CULL.md) | rendering | 🟡 MITIGATED | 🟡 HIGH | 2026-10-08 |
| [Static Instanced Asset OOB — 2nd Vehicle Invisible](rendering/STATIC_INSTANCED_OOB.md) | rendering | 🟢 FIXED | 🟡 HIGH | 2026-10-05 |
| [Instanced Proxy Yaw Clip — Elongated Volumes](rendering/INSTANCED_PROXY_YAW_CLIP.md) | rendering | 🟢 FIXED | 🟡 HIGH | 2026-10-06 |
| [Promoted-File Round Trip — Double Flip](editor/PROMOTED_FILE_ROUNDTRIP_DOUBLE_FLIP.md) | editor | 🟢 FIXED | 🟡 HIGH | 2026-10-06 |
| [Hover Highlight Rebuilt ~2xN Instances Per Mousemove](editor/HIGHLIGHT_INSTANCED_REBUILD_LAG.md) | editor | 🟢 FIXED | 🟡 HIGH | 2026-10-09 |
| [IMGUI Debug HUD Frame Tax (120→40 FPS)](rendering/IMGUI_HUD_FRAME_TAX.md) | rendering | 🟢 FIXED | 🟡 HIGH | 2026-10-11 |
| [Mid-Render ComputeBuffer Alloc Drops Sector SRV (`_BuildingMeta`)](rendering/MID_RENDER_BUFFER_ALLOC_SRV_DROP.md) | rendering | 🟢 FIXED | 🟡 HIGH | 2026-10-11 |
| [Shared `proxyMaterial` DrawMesh/DrawMeshInstanced — Instanced Models Displaced/Invisible](rendering/SHARED_MATERIAL_INSTANCED_DRAWS.md) | rendering | 🟢 FIXED | 🔴 CRITICAL | 2026-10-11 |
| [Sector Mode Leaves Building Address Registry Empty — Range Points Missing](runtime/SECTOR_MODE_ADDRESS_REGISTRY_EMPTY.md) | runtime | 🟢 FIXED | 🟡 HIGH | 2026-10-11 |
| [Single-Keyword `multi_compile` Always On — Instanced Models Invisible](rendering/MULTI_COMPILE_SINGLE_KEYWORD_ALWAYS_ON.md) | rendering | 🟡 FIX APPLIED (pending test) | 🔴 CRITICAL | 2026-10-11 |
| [SmoothNormal NaN - Thin Voxel Faces Render Near-Black](rendering/SMOOTH_NORMAL_NAN_THIN_VOXELS.md) | rendering | 🟢 FIXED | 🟢 MEDIUM | 2026-10-12 |
| [Domino Orphan — Floating Voxel After Settling](runtime/DOMINO_ORPHAN_FLOATING_VOXEL.md) | runtime | 🟢 FIXED | 🟢 MEDIUM | 2026-10-13 |
| [Player Build — Stripped Shader.Find Names Abort Scene Init](build/PLAYER_SHADER_STRIPPING_INIT_CASCADE.md) | build | 🟢 FIXED | 🔴 CRITICAL | 2026-10-13 |
| [Player Build — Proxy Raymarch Draws Emit But Produce Nothing](build/PLAYER_PROXY_RENDER_EMPTY.md) | build | � FIXED | 🔴 CRITICAL | 2026-10-13 |
| [Possess Shots Deal No Damage — Lazy VDS Spawn](runtime/POSSESS_SHOTS_NO_DAMAGE_LAZY_VDS.md) | runtime | 🟢 FIXED | 🟡 HIGH | 2026-10-11 |
| [IK Torso Override Stomps Crouch Lean](runtime/IK_TORSO_OVERRIDE_STOMPS_CROUCH_LEAN.md) | runtime | 🟢 FIXED | 🟡 HIGH | 2026-10-14 |
| [Cover Pick Reads Wrong Volume in Splat Overlap](runtime/COVER_PICK_SPLAT_OVERLAP_WRONG_VOLUME.md) | runtime | 🟢 FIXED | 🟡 HIGH | 2026-10-14 |
| [Cover Pit Ladder Authored at Wrong Voxel Scale](runtime/COVER_PIT_VOXEL_SCALE_MISMATCH.md) | runtime | 🟡 FIX APPLIED | 🟡 HIGH | 2026-10-15 |
| [Debug Beam Emission Flood — 120→40 FPS](runtime/PERF_DEBUG_BEAM_EMISSION_FLOOD.md) | runtime | 🟢 FIXED | 🔴 CRITICAL | 2026-10-16 |
