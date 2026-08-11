# Character Animation & Physics Pipeline

**Purpose**: Unified design for procedural keyframe animation, flinch reactions, and ragdoll physics in Steel City: Mob Sim
**Created**: August 10, 2026
**Status**: DESIGN — implementation in progress

---

## 1. Architecture Overview

Steel City uses a **GPU raymarch shader** (`VoxelProxyRaymarch.shader`) to render voxel characters. All voxels for a character live in a single ComputeBuffer — characters are NOT separate GameObjects per bone. Per-voxel `groupID` tags identify which limb each voxel belongs to (0=body, 1=head, 2=L arm, 3=R arm, 4=L leg, 5=R leg, 6=L shin, 7=R shin, 8=L forearm, 9=R forearm).

The shader applies per-group rotations during the DDA raymarch loop using an **inverse transform** technique: the ray steps through posed space, and at each step it inverse-transforms the sample position back to rest space to read the voxel grid.

### Three animation drivers feed one transform pipeline

```
ALIVE (procedural):     walkKeyframes → shader computes spline interp → per-group rotation
HIT/FLINCH (reactive):  flinch keyframes → shader computes one-shot interp → per-group rotation
DEAD/RAGDOLL (physics): proxy bone Rigidbodies → physics sim → transform buffer → shader applies
```

All three produce **per-group rotation matrices** that the shader consumes. The shader does not care where the matrices came from.

### Current state vs target

| Component | Current | Target |
|-----------|---------|--------|
| Animator (HTML) | Keyframe system complete (spline interp, leg twist, body bob) | Add angular limits, flinch keyframes, export pipeline |
| Shader | Hardcoded `sin()` animation | Keyframe buffer + spline interp in HLSL |
| C# driver | Pushes only `animState/animTime/animSpeed` | Pushes keyframe data, pivot data, per-group transforms |
| Ragdoll | None | Proxy bone physics → transform upload |
| Flinch | None | One-shot keyframe playback on hit event |

---

## 2. Data Flow: Animator → Unity

### 2.1 What the animator exports

The animator's `Save Project` produces a JSON file containing:

```json
{
  "format": "character_animator_project",
  "dims": [W, H, D],
  "voxels": [[x, y, z, materialID], ...],
  "groups": [[x, y, z, groupID], ...],
  "pivots": {
    "0": {"x": 0.5, "y": 0.4, "z": 0.5},   // body/waist
    "1": {"x": 0.5, "y": 0.78, "z": 0.5},  // head/neck
    "2": {"x": 0.25, "y": 0.75, "z": 0.5}, // L shoulder
    "3": {"x": 0.75, "y": 0.75, "z": 0.5}, // R shoulder
    "4": {"x": 0.375, "y": 0.34, "z": 0.5},// L hip
    "5": {"x": 0.625, "y": 0.34, "z": 0.5},// R hip
    "6": {"x": 0.375, "y": 0.20, "z": 0.5},// L knee
    "7": {"x": 0.625, "y": 0.20, "z": 0.5},// R knee
    "8": {"x": 0.25, "y": 0.75, "z": 0.5}, // L elbow
    "9": {"x": 0.75, "y": 0.75, "z": 0.5}  // R elbow
  },
  "animParams": {
    "restPose": {"leftArmZ": -1.5708, "rightArmZ": 1.5708},
    "jointOffset": {"1": {"x":0,"y":0,"z":0}, "2": {"x":N,"y":0,"z":0}, ...},
    "walkKeyframes": {
      "autoMirror": true,
      "cycleDuration": 1.2,
      "interpolation": "spline",
      "kf0": {"armSwingL":0.3, "armSwingR":-0.3, "legStrideL":-0.4, "legStrideR":0.4, ...},
      "kf1": {"armSwingL":0.0, "armSwingR":0.0, "legStrideL":0.0, "legStrideR":0.0, ...},
      "bodyBob": {"enabled": true, "amplitude": 0.6},
      "weightShift": {"enabled": true, "amplitude": 0.3}
    },
    "legTwist": {"leftRest": 0.0, "rightRest": 0.0},
    "armSwing": {"axisL": 0, "axisR": 0, "signL": 1, "signR": 1},
    "legStride": {"axisL": 0, "axisR": 0, "signL": -1, "signR": -1},
    "elbowBend": {"axisL": 0, "axisR": 0, "signL": 1, "signR": 1, "leftRest": 0.2, "rightRest": 0.2, "twistL": 0, "twistR": 0},
    "kneeBend": {"axisL": 0, "axisR": 0, "signL": 1, "signR": 1, "leftRest": 0.1, "rightRest": 0.1},
    "aiming": {...},
    "crouching": {...}
  }
}
```

### 2.2 Data mapping: animator → Unity driver

| Animator field | Unity consumer | Purpose |
|----------------|---------------|---------|
| `pivots` | Shader constant buffer + ragdoll joint positions | Where each group rotates around |
| `jointOffset` | Shader constant buffer | Positional nudge per group (arm offset) |
| `restPose` | Shader (idle/neutral pose) | Arms-down Z rotation |
| `walkKeyframes` | Shader structured buffer | 4 keyframe poses for walk cycle |
| `walkKeyframes.interpolation` | Shader uniform | "spline" / "cosine" / "smoothstep" |
| `walkKeyframes.cycleDuration` | Shader uniform | Seconds per full stride |
| `walkKeyframes.bodyBob` | Shader uniform | Vertical bob amplitude |
| `walkKeyframes.weightShift` | Shader uniform | Lateral shift amplitude |
| `legTwist` | Shader constant | Static Y rotation per leg |
| `armSwing.axisL/signL` | Shader constant | How to apply arm swing angle |
| `legStride.axisL/signL` | Shader constant | How to apply leg stride angle |
| `elbowBend.axisL/signL` | Shader constant | How to apply elbow bend |
| `kneeBend.axisL/signL` | Shader constant | How to apply knee bend |
| `elbowBend.leftRest/rightRest` | Shader (idle pose) | Resting elbow bend |
| `kneeBend.leftRest/rightRest` | Shader (idle pose) | Resting knee bend |
| `aiming.*` | Shader (state 3/4) | Aim pose parameters |
| `crouching.*` | Shader (state 5) | Crouch pose parameters |

### 2.3 What does NOT exist yet (needs to be added)

| Missing data | Why needed | Where to add |
|-------------|-----------|--------------|
| **Angular limits** per joint | Ragdoll ConfigurableJoint constraints | Animator + export JSON |
| **Joint type** (Ball/Hinge/Root) | Ragdoll joint configuration | Animator + export JSON |
| **Flinch keyframes** | Hit reaction animation | Animator + export JSON |
| **Group voxel bounds** | Collider sizing for ragdoll | Computed from voxel data in C# |

---

## 3. Parent-Child Hierarchy (Forward Kinematics)

Both the animator and the shader use the same FK chain:

```
Group 0: Body (root)
├── Group 1: Head
├── Group 2: Left Arm
│   └── Group 8: Left Forearm
├── Group 3: Right Arm
│   └── Group 9: Right Forearm
├── Group 4: Left Leg
│   └── Group 6: Left Shin
└── Group 5: Right Leg
    └── Group 7: Right Shin
```

**PARENT_OF map**: `{8:2, 9:3, 6:4, 7:5}`

Child groups inherit their parent's transform chain. The child's own rotation is prepended to the parent's chain (applied first, then parent cascades on top — standard FK).

This hierarchy is used by:
- The animator's `computeGroupRotation()` (JavaScript)
- The shader's `ComputeGroupRotation()` (HLSL) — needs updating to support chains
- The ragdoll's ConfigurableJoint parent-child connections (C#)

---

## 4. Keyframe Shader Port (Option A)

### 4.1 Goal

Replace the shader's hardcoded `sin(animTime * 6.0 * animSpeed) * 0.3` walk logic with keyframe interpolation that matches the animator.

### 4.2 HLSL functions to implement

```hlsl
// Catmull-Rom spline — flows through keyframes with continuous velocity
float CatmullRom(float p0, float p1, float p2, float p3, float t) {
    float t2 = t * t;
    float t3 = t2 * t;
    return 0.5 * (
        (2 * p1) +
        (-p0 + p2) * t +
        (2*p0 - 5*p1 + 4*p2 - p3) * t2 +
        (-p0 + 3*p1 - 3*p2 + p3) * t3
    );
}

// L↔R mirror
struct WalkPose {
    float armSwingL, armSwingR;
    float legStrideL, legStrideR;
    float elbowBendL, elbowBendR;
    float kneeBendL, kneeBendR;
    float forearmTwistL, forearmTwistR;
};
WalkPose MirrorPose(WalkPose p) {
    WalkPose m;
    m.armSwingL = p.armSwingR;    m.armSwingR = p.armSwingL;
    m.legStrideL = p.legStrideR;  m.legStrideR = p.legStrideL;
    m.elbowBendL = p.elbowBendR;  m.elbowBendR = p.elbowBendL;
    m.kneeBendL = p.kneeBendR;    m.kneeBendR = p.kneeBendL;
    m.forearmTwistL = p.forearmTwistR; m.forearmTwistR = p.forearmTwistL;
    return m;
}

// Get walk pose at cycle phase (0.0-1.0)
WalkPose GetWalkPose(float cyclePhase, WalkPose kf[4], bool autoMirror) {
    // ... segment selection + Catmull-Rom interpolation
}
```

### 4.3 Buffer layout

**Per-character-type constants** (shared by all instances of same character):
- `_WalkKeyframes[4]` — 4 WalkPose structs (40 floats)
- `_WalkConfig` — float4 (cycleDuration, bodyBobAmp, weightShiftAmp, autoMirror)
- `_JointConfig[10]` — per-group axis/sign/rest values

**Per-instance data** (already exists):
- `_InstanceOffsets[i]` — float4 (pos.xyz, yaw)
- `_InstanceOffsets[i + N]` — float4 (animState, animTime, animSpeed, 0)

### 4.4 Shader changes

Replace `ComputeGroupRotation()` walking branch:
- Current: `swing = sin(animTime * 6.0 * animSpeed) * 0.3`
- Target: `swing = signL * GetWalkPose(phase, kf, autoMirror).armSwingL`

Add body bob + weight shift to the raymarch origin or volume offset.

---

## 5. Ragdoll Physics System

### 5.1 Design

The ragdoll uses the **group structure as-is** — no renaming, no extra hierarchy. Each group becomes one proxy bone. The animator's data directly defines the entire ragdoll:

| Animator data | Ragdoll use |
|--------------|-------------|
| Group IDs (0-9) | One proxy bone per group |
| Pivots | ConfigurableJoint anchor positions |
| `PARENT_OF` map | Joint parent-child connections |
| Axis/sign per limb | Joint rotation axis and direction |

```
Group structure = bone structure (no conversion needed):

Group 0: Body          ← root (no parent)
Group 1: Head          ← parent: Body
Group 2: Left Arm      ← parent: Body
Group 8: Left Forearm  ← parent: Left Arm
Group 3: Right Arm     ← parent: Body
Group 9: Right Forearm ← parent: Right Arm
Group 4: Left Leg      ← parent: Body
Group 6: Left Shin     ← parent: Left Leg
Group 5: Right Leg     ← parent: Body
Group 7: Right Shin    ← parent: Right Leg
```

10 proxy bones total. Each is an invisible GameObject with:
- Rigidbody (mass proportional to voxel count)
- Capsule collider (sized from group's voxel bounds)
- ConfigurableJoint (connected to parent, anchor at pivot position)

### 5.2 Per-group colliders

Each proxy bone gets a **capsule collider** sized from that group's voxel bounds:
- Compute min/max XYZ of all voxels with that groupID (C# utility, runs once on ragdoll spawn)
- Create a capsule along the dominant axis
- Radius = half the average of the other two axes

This reuses the existing `VoxelCollisionWorld` for environment collision — each proxy bone probes the voxel world the same way `VoxelCharacter` currently probes for ground.

### 5.3 Joint configuration (ported from Steel Tide)

Steel Tide's `VoxelActor2Joints.cs` provides the joint configuration logic. Mapped to Steel City's groups:

| Group | Joint type | Reason |
|-------|-----------|--------|
| 0 (Body) | Root (locked) | Torso is the root — everything connects to it |
| 1 (Head) | Ball | Neck can yaw/pitch/tilt |
| 2, 3 (Arms) | Ball | Shoulders are 3DOF |
| 4, 5 (Legs) | Ball | Hips are 3DOF |
| 8, 9 (Forearms) | Hinge | Elbows bend on one axis (the animator's `elbowBend.axisL/R`) |
| 6, 7 (Shins) | Hinge | Knees bend on one axis (the animator's `kneeBend.axisL/R`) |

The animator's existing `axisL/axisR` settings map directly to the hinge axis. The `signL/signR` settings map to the hinge direction. Angular limits (min/max angle) are the one piece still missing from the animator — Phase 2 adds them.

### 5.4 What ports from Steel Tide

| Steel Tide file | What it does | Port status |
|----------------|-------------|-------------|
| `VoxelActor2Joints.cs` | Builds ConfigurableJoints, sets angular limits | **Direct port** — joint config logic |
| `VoxelActor2LimbDrive.cs` | Drives bones toward target rotations via slerp | **Direct port** — used for flinch/pose blending |
| `VoxelActor2Ground.cs` | Ground detection via VoxelWorld probing | **Adapt** — use VoxelCollisionWorld instead |
| `VoxelActor2Revoxel.cs` | Revoxelizes per-bone for rendering | **Not needed** — shader handles rendering |
| `VoxelActor2Balance.cs` | Balance/procedural standing | **Later** — not needed for death ragdoll |

### 5.5 What is new (not in Steel Tide)

| New component | Purpose |
|--------------|---------|
| `VoxelGroupRagdoll.cs` | Creates proxy bones from group data, runs physics, uploads transforms to shader |
| Per-group transform buffer | ComputeBuffer of per-group rotation matrices, uploaded to shader |
| Blend system | Crossfade between keyframe-driven and physics-driven transforms |
| Group voxel bounds computation | C# utility to compute min/max XYZ per groupID from voxel data |

### 5.6 Activation

Ragdoll activates only for characters **involved in combat or mayhem**:
- Normal NPCs: keyframe animation only (no proxy bones, no physics overhead)
- Combat trigger (hit by bullet, explosion, melee): spawn proxy bones, initialize from current pose, hand off to physics
- Death: ragdoll persists until cleanup
- Recovery (flinch without death): physics runs briefly, then blends back to keyframes

---

## 6. Flinch System

### 6.1 Design

Flinches are **one-shot keyframe sequences** triggered by hit events. They use the same interpolation machinery as the walk cycle but play once and blend back.

### 6.2 Flinch types

Based on hit location and force (as discussed in prior design):

| Hit type | Keyframes | Duration | Description |
|----------|-----------|----------|-------------|
| Front torso | KF0 = recoil back, KF1 = lean forward recover | 0.3s | Snap back, then forward |
| Back torso | KF0 = arch forward, KF1 = straighten | 0.3s | Push forward, then recover |
| Head hit | KF0 = snap head back, KF1 = head down | 0.2s | Whiplash |
| Leg hit | KF0 = buckled, KF1 = straighten | 0.4s | Stumble |
| Explosion | KF0 = full body extension, KF1 = curl | 0.5s | Blast impact |

Force scales the amplitude of the keyframe values. Direction determines which keyframe set to use.

### 6.3 Playback

```
1. Hit event → select flinch keyframe set based on hit location/direction/force
2. Set animState = FLINCHING (6), animTime = 0
3. Shader plays flinch keyframes (one-shot, not cyclical)
4. On completion → blend back to previous state (idle/walk)
5. If force exceeds threshold → transition to ragdoll instead of recovery
```

---

## 7. Implementation Phases

### Phase 1: Keyframe Shader Port (current)
- Add walkKeyframes structured buffer to shader
- Port Catmull-Rom, mirror, getWalkPose to HLSL
- Replace hardcoded sin() in ComputeGroupRotation
- Add body bob + weight shift in shader
- C# code to upload keyframe data from loaded .anim.json
- Verify: Unity walk matches animator walk

### Phase 2: Angular Limits in Animator
- Add joint type (Ball/Hinge/Root) per group
- Add minAngle/maxAngle per joint
- Add to export JSON
- These feed the ragdoll ConfigurableJoint setup

### Phase 3: Flinch Keyframes
- Add flinch keyframe sets to animator
- Add flinch playback state to shader
- Add hit-event → flinch trigger in C#
- Blend from flinch back to walk/idle

### Phase 4: Ragdoll Proxy Bones
- Implement VoxelGroupRagdoll.cs
- Create proxy bones from group data + pivots
- Port joint configuration from Steel Tide
- Per-group capsule colliders from voxel bounds
- Physics → per-group transform buffer → shader
- Blend: keyframe → ragdoll on death, ragdoll → keyframe on recovery

### Phase 5: Integration
- Hit event system: damage → flinch or ragdoll based on force
- Cleanup: ragdoll bones pooled/recycled
- Performance: only combat participants have proxy bones

---

## 8. File Inventory

### Existing files (modified)

| File | Role | Changes needed |
|------|------|---------------|
| `VoxelAssetStudio/character_animator.html` | Animator/preview tool | Add angular limits, flinch keyframes, export improvements |
| `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` | Raymarch renderer | Keyframe interp, per-group transform buffer, body bob |
| `Assets/Scripts/Sim/CharacterAnimation.cs` | Animation driver | Upload keyframe data, flinch triggers, ragdoll handoff |
| `Assets/Scripts/Sim/VoxelCharacter.cs` | Character loader | Load animParams, expose group data |

### New files (to create)

| File | Purpose |
|------|---------|
| `Assets/Scripts/Sim/VoxelGroupRagdoll.cs` | Proxy bone physics → transform upload |
| `Assets/Scripts/Sim/VoxelGroupData.cs` | Group voxel bounds, collider sizing utility |

### Steel Tide reference files (port source)

| File | What to port |
|------|-------------|
| `VoxelActor2Joints.cs` | ConfigurableJoint setup, angular limit configuration |
| `VoxelActor2LimbDrive.cs` | Slerp drive for pose blending / flinch recovery |
| `VoxelActor2Ground.cs` | Ground probing pattern (adapt to VoxelCollisionWorld) |

---

## 9. Performance Considerations

- **Normal NPCs**: keyframe animation only — zero physics overhead, shader computes transforms from buffer
- **Combat participants**: proxy bone physics — ~10 Rigidbodies + joints per character, only active during combat
- **Instancing**: all instances of same character type share keyframe buffer; per-instance data is just animState/animTime/animSpeed
- **Ragdoll breaks instancing**: a ragdoll character needs individual proxy bones, so it drops out of the instanced batch and renders individually (same pattern as Steel Tide's tier system)

---

## 10. Backward Compatibility

- Old saved projects (without walkKeyframes) load with defaults — deep-merge handles this
- Old .stasset files (without group data) render as before — groupID defaults to 0 (body)
- Shader falls back to hardcoded sin() if keyframe buffer not set — graceful degradation
- Ragdoll is opt-in per character — no proxy bones unless combat trigger fires
