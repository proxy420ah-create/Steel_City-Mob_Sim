# T-Pose Reference Guide

**Version**: 1.0.0  
**Date**: June 29, 2026  
**Purpose**: Understanding T-Pose for voxel character rigging

---

## What is T-Pose?

**T-Pose** (also called "reference pose" or "bind pose") is the **standard neutral position** for 3D character rigging. The character stands upright with arms extended horizontally, forming a "T" shape.

```
        O  ← Head
        |
    ────┼────  ← Arms extended (90° from body)
        |
        |  ← Torso/spine
       / \
      /   \  ← Legs straight, shoulder-width apart
```

---

## Why T-Pose is Critical

### 1. **Rigging Standard**

T-Pose is the **universal starting point** for skeletal rigging in 3D animation:

- **Unity**: Expects humanoid rigs in T-Pose
- **Blender**: Default rigging pose
- **Unreal Engine**: Standard for character imports
- **Motion Capture**: Calibration pose

**Why**: It provides maximum joint visibility and symmetry for weight painting and bone assignment.

### 2. **Joint Rotation Baseline**

All joint rotations are measured **relative to T-Pose**:

```
T-Pose (0° rotation):
- Shoulders: 90° from body (horizontal)
- Elbows: 0° (straight)
- Hips: 0° (neutral)
- Knees: 0° (straight)

Idle Pose (rotations from T-Pose):
- Shoulders: -45° (arms down)
- Elbows: +30° (slight bend)
- Hips: 0° (neutral)
- Knees: +5° (slight bend)
```

**Why**: Easier to calculate rotations from a known baseline than arbitrary starting positions.

### 3. **Symmetry for Mirroring**

T-Pose is perfectly symmetrical:

```
Left Arm = Right Arm (mirrored)
Left Leg = Right Leg (mirrored)
```

**Why**: You can rig one side, then mirror to the other side automatically.

### 4. **Weight Painting Clarity**

With arms extended, you can clearly see:
- Where shoulder ends and arm begins
- Where torso ends and legs begin
- Joint boundaries (elbow, knee, wrist)

**Why**: Prevents voxels from being assigned to wrong bones (e.g., armpit voxels assigned to torso instead of arm).

---

## T-Pose for Voxel Characters

### Biped T-Pose (Humanoid)

```
Dimensions: 16×32×16 voxels (with arms extended)

Top View (Y-axis):
    ████████████████  ← Arms (8 voxels each side)
        ████          ← Torso (4 voxels wide)
        
Front View (Z-axis):
        ████          ← Head (4 voxels)
        ████          ← Neck
    ████████████      ← Shoulders + Arms
        ████          ← Torso
        ████          ← Hips
       ██  ██         ← Legs (2 voxels each)
       ██  ██
       ██  ██
```

**Key Measurements**:
- **Arm span**: 16 voxels (8 left + 8 right)
- **Body width**: 4 voxels
- **Leg spacing**: 2 voxels apart
- **Total height**: 32 voxels

### Quadruped T-Pose (Mech)

For quadrupeds, "T-Pose" means **standing neutral**:

```
Side View:
    ████████████████  ← Body (horizontal)
    ║       ║         ← Front/back legs
    ║       ║
    ║       ║
```

**Key Measurements**:
- **Body length**: 16 voxels
- **Leg height**: 12 voxels
- **Leg spacing**: 4 voxels (front-to-back), 6 voxels (left-to-right)

---

## T-Pose vs. Other Reference Poses

### **T-Pose** (Standard Rigging)
```
Arms: Horizontal (90° from body)
Legs: Straight, shoulder-width
Use: Initial rigging, weight painting
```

**Pros**:
- Industry standard
- Maximum joint visibility
- Easy to mirror

**Cons**:
- Unnatural (no one stands like this)
- Arms can intersect geometry in tight spaces

### **A-Pose** (Alternative)
```
Arms: 45° down from horizontal
Legs: Straight, shoulder-width
Use: Alternative rigging pose
```

**Pros**:
- More natural arm position
- Less geometry intersection
- Better for characters with shoulder armor

**Cons**:
- Not as widely supported
- Harder to calculate rotations

### **Idle Pose** (Natural Stance)
```
Arms: Down at sides, slight bend
Legs: Slight knee bend, relaxed
Use: Default in-game pose
```

**Pros**:
- Natural, realistic
- Good for previews

**Cons**:
- NOT for rigging (joints obscured)
- Asymmetrical (harder to mirror)

---

## Creating T-Pose in Voxel Studio

### Method 1: Generate with Arms (Future Feature)

```python
# Generate biped skeleton in T-Pose
skeleton, voxels = SkeletonGenerator.generate_standalone_biped_skeleton(
    grid_size=(16, 32, 16),  # Wider for arms
    pose='T_POSE'
)

# Skeleton includes:
# - Spine (vertical)
# - Left arm (horizontal, -X direction)
# - Right arm (horizontal, +X direction)
# - Legs (vertical, slight spread)
```

### Method 2: Manual Pose Editor (Current Approach)

```
1. Generate basic biped skeleton (8×32×8)
2. Open Pose Editor
3. Rotate shoulder joints:
   - Left shoulder: +90° on Z-axis
   - Right shoulder: -90° on Z-axis
4. Extend arms:
   - Add arm bones manually
   - Paint arm voxels
5. Save as "T_Pose.stasset"
```

### Method 3: Paint Around Skeleton

```
1. Generate basic skeleton (no arms yet)
2. Manually paint arm voxels:
   - Left arm: X=-8 to X=-4, Y=24, Z=0
   - Right arm: X=4 to X=8, Y=24, Z=0
3. Auto-detect skeleton will find arms
4. Save as "T_Pose.stasset"
```

---

## T-Pose Workflow for Unity

### Step 1: Create T-Pose in Voxel Studio

```
1. Generate standalone skeleton
2. Set T-Pose (arms horizontal)
3. Paint body around skeleton
4. Export "Character_TPose.stasset"
```

### Step 2: Import to Unity

```csharp
// Load T-Pose asset
VoxelAsset tPoseAsset = LoadStAsset("Character_TPose.stasset");

// Extract skeleton
VoxelSkeleton skeleton = tPoseAsset.skeleton;

// Create GameObject hierarchy
GameObject root = CreateSkeletonHierarchy(skeleton);

// This is now your "bind pose" - all rotations are relative to this
```

### Step 3: Create Animator Controller

```csharp
// T-Pose is the reference
// All animations are deltas from T-Pose

// Idle animation:
// - Shoulders: -45° (arms down)
// - Elbows: +30° (slight bend)

// Walk animation:
// - Shoulders: -45° + swing motion
// - Hips: rotate for leg movement
```

---

## Common T-Pose Mistakes

### ❌ **Arms Too High**
```
    \O/  ← Arms above horizontal
     |
```
**Problem**: Shoulder joint at wrong angle  
**Fix**: Arms should be exactly horizontal (90° from spine)

### ❌ **Arms Too Low**
```
     O   ← Arms below horizontal
    /|\
```
**Problem**: Not a T-Pose, this is A-Pose  
**Fix**: Raise arms to horizontal

### ❌ **Legs Too Wide**
```
     O
     |
    / \  ← Legs spread too far
   /   \
```
**Problem**: Hip joint stretched, hard to animate  
**Fix**: Legs should be shoulder-width (1-2 voxels apart)

### ❌ **Asymmetrical**
```
     O
    /|   ← One arm higher than other
     |
```
**Problem**: Can't mirror, rigging will be broken  
**Fix**: Ensure perfect left-right symmetry

---

## T-Pose Checklist

Before exporting to Unity, verify:

- [ ] Arms are **exactly horizontal** (90° from body)
- [ ] Arms are **same length** (left = right)
- [ ] Legs are **straight** (knees at 0°)
- [ ] Legs are **shoulder-width apart** (1-2 voxels)
- [ ] Spine is **vertical** (no lean)
- [ ] Head is **centered** and **upright**
- [ ] **Left-right symmetry** is perfect
- [ ] All joints are at **0° rotation** (neutral)

---

## T-Pose Variations by Character Type

### **Humanoid** (Standard T-Pose)
```
Arms: Horizontal, palms down
Legs: Straight, shoulder-width
Height: 32 voxels (1.8m)
```

### **Mech** (Standing Neutral)
```
Arms: Down at sides (weapons ready)
Legs: Straight, wide stance
Height: 48 voxels (3.0m)
```

### **Creature** (Quadruped Neutral)
```
Legs: All extended, evenly spaced
Tail: Straight back (if applicable)
Height: 24 voxels (1.5m)
```

---

## Why We Start with T-Pose

**In Voxel Studio**:
1. Generate skeleton in T-Pose
2. Paint body around it
3. Export as reference

**In Unity**:
1. Import T-Pose asset
2. This becomes your "bind pose"
3. All animations are rotations from this pose
4. Unity's Animator expects this

**Result**: Your character is **rigging-ready** for Unity's animation system.

---

## Next Steps

1. **Generate T-Pose skeleton** in Voxel Studio
2. **Paint body** around the skeleton
3. **Export** as `.stasset`
4. **Import to Unity** as bind pose
5. **Create animations** (idle, walk, run) as rotations from T-Pose

**T-Pose is your foundation** - everything else builds on it!
