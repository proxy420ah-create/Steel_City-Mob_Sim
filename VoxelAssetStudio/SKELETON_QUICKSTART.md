# Voxel Skeleton System - Quick Start Guide

**Version**: 0.1.0 (Proof of Concept)  
**Date**: June 29, 2026  
**Status**: Phase 0 - Auto-Generation Prototype

---

## What's Implemented

✅ **Auto-skeleton generation** from existing voxel volumes  
✅ **Biped skeleton** (4 bones: pelvis → lower spine → upper spine → head)  
✅ **Quadruped skeleton** (13 bones: body + 4 legs with hip/knee joints)  
✅ **Influence weight computation** (auto-assign voxels to bones by proximity)  
✅ **Visual overlay** (yellow bones, red ball joints, green hinge joints)  
✅ **Toggle visibility** (Ctrl+K to show/hide skeleton)

---

## How to Use

### Step 1: Create or Load a Voxel Model

**Option A**: Generate a test model
- Menu: `Generate` → `🧍 Simple Humanoid`
- Or: `Generate` → `Test Cube (8×8×8)`

**Option B**: Load existing model
- Menu: `File` → `Open...`
- Select any `.stasset` file

### Step 2: Auto-Generate Skeleton

**For humanoid/character models**:
- Menu: `Skeleton` → `🦴 Auto-Generate Biped Skeleton`
- Creates vertical spine with 4 bones and 3 ball joints

**For mechs/quadrupeds**:
- Menu: `Skeleton` → `🦴 Auto-Generate Quadruped Skeleton`
- Creates horizontal body + 4 legs (hip + knee joints per leg)

### Step 3: View the Skeleton

The skeleton overlay appears automatically:
- **Yellow lines** = Bones (rigid segments)
- **Red spheres** = Ball joints (3-axis rotation)
- **Green spheres** = Hinge joints (1-axis rotation, knees)

**Toggle visibility**:
- Menu: `Skeleton` → `👁️ Show/Hide Skeleton`
- Shortcut: `Ctrl+K`

### Step 4: Check the Console

The console shows skeleton details:
```
🦴 Auto-generating biped skeleton...
🦴 Computing influence weights...
✅ Biped skeleton: 4 bones, 3 joints
```

Status bar shows:
```
🦴 Biped skeleton generated: 4 bones, 3 joints, 1247 influenced voxels
```

---

## Understanding the Skeleton

### Biped Skeleton Structure

```
Head (Bone 3)
    │
    ⊕ Joint 2 (Neck - Ball)
    │
Upper Spine (Bone 2)
    │
    ⊕ Joint 1 (Mid Spine - Ball)
    │
Lower Spine (Bone 1)
    │
    ⊕ Joint 0 (Hip - Ball)
    │
Pelvis (Bone 0)
```

### Quadruped Skeleton Structure

```
        Front Left Leg          Front Right Leg
             │                        │
        ⊕ Hip (Ball)            ⊕ Hip (Ball)
             │                        │
        Upper Leg               Upper Leg
             │                        │
        ⊗ Knee (Hinge)          ⊗ Knee (Hinge)
             │                        │
        Lower Leg               Lower Leg
             │                        │
             
═══════════════ Body (Horizontal) ═══════════════

        Back Left Leg           Back Right Leg
             │                        │
        ⊕ Hip (Ball)            ⊕ Hip (Ball)
             │                        │
        Upper Leg               Upper Leg
             │                        │
        ⊗ Knee (Hinge)          ⊗ Knee (Hinge)
             │                        │
        Lower Leg               Lower Leg
```

---

## What Happens Behind the Scenes

### 1. Voxel Analysis
```python
# Find all filled voxels
filled_positions = np.argwhere(voxels > 0)

# Calculate bounds
min_bounds = filled_positions.min(axis=0)
max_bounds = filled_positions.max(axis=0)
center = (min_bounds + max_bounds) // 2
```

### 2. Bone Placement
```python
# Biped: Vertical spine segments
height = max_bounds[1] - min_bounds[1]
lower_spine_y = min_bounds[1] + height // 4
mid_spine_y = min_bounds[1] + height // 2
upper_spine_y = min_bounds[1] + 3 * height // 4
```

### 3. Influence Weight Computation
```python
# For each voxel, find nearby bones
for voxel_pos in filled_positions:
    for bone in bones:
        distance = distance_to_bone(voxel_pos, bone)
        if distance <= radius:
            weight = 1.0 - (distance / radius)  # Linear falloff
            influence_map[voxel_pos].append((bone_id, weight))
```

---

## Next Steps (Not Yet Implemented)

### Phase 1: Export to Unity
- [ ] Extend `.stasset` format to include skeleton data
- [ ] Unity loader reads skeleton from file
- [ ] Create GameObject hierarchy for bones

### Phase 2: Physics Test
- [ ] Add Rigidbody to each bone
- [ ] Connect bones with Unity joints
- [ ] Apply gravity → watch ragdoll

### Phase 3: Balance Controller
- [ ] Raycast to find ground
- [ ] Apply forces to legs
- [ ] Keep torso upright

### Phase 4: IK Animation
- [ ] Replace physics with IK solver
- [ ] Procedural walk cycle
- [ ] Terrain adaptation

---

## Troubleshooting

**"No voxels found to generate skeleton from"**
- The voxel volume is empty
- Load or generate a model first

**Skeleton not visible**
- Check: `Skeleton` → `👁️ Show/Hide Skeleton` is checked
- Try pressing `Ctrl+K` to toggle

**Skeleton looks wrong**
- Auto-generation uses simple heuristics (center + height)
- Works best with upright characters/mechs
- Manual skeleton editing coming in Phase 2

**Influence weights missing**
- This is normal - they're computed but not visualized yet
- Check console for: `Computing influence weights...`

---

## Technical Details

### Files Modified
- `skeleton_generator.py` - Auto-generation algorithms
- `voxel_editor.py` - Menu integration + skeleton data storage
- `viewport_widget.py` - Visual overlay rendering

### Data Structure
```python
skeleton_data = {
    'bones': [
        {
            'id': 0,
            'name': 'pelvis',
            'start': [16, 0, 16],
            'end': [16, 8, 16],
            'length': 8.0,
            'parent_joint': None,
            'child_joint': 0
        }
    ],
    'joints': [
        {
            'id': 0,
            'name': 'hip',
            'type': 'BALL',
            'position': [16, 8, 16],
            'max_angle_x': 45.0,
            'max_angle_y': 30.0,
            'max_angle_z': 30.0
        }
    ],
    'influence_map': {
        '16,7,16': [(0, 1.0)],  # Voxel fully influenced by bone 0
        '16,8,16': [(0, 0.5), (1, 0.5)]  # Blended between bones 0 and 1
    },
    'attachments': []
}
```

### Rendering
- **Bones**: `GLLinePlotItem` (yellow lines, 3px width)
- **Joints**: `GLScatterPlotItem` (red/green spheres, 1.5x voxel size)
- **Coordinate conversion**: Voxel grid → World space (×0.125 scale)

---

## Feedback & Testing

This is a **proof of concept** to ground you in the skeleton system before building the full animation pipeline.

**Test these scenarios**:
1. Generate humanoid → auto-skeleton → verify spine alignment
2. Generate quadruped → auto-skeleton → verify leg positions
3. Toggle visibility (Ctrl+K) → verify overlay shows/hides
4. Load different models → verify skeleton adapts to size

**Report issues**:
- Skeleton misaligned with model
- Joints in wrong positions
- Influence weights seem incorrect
- Visualization glitches

---

## What You've Learned

✅ Skeleton data structure (bones, joints, influence map)  
✅ Auto-generation from voxel bounds  
✅ Visual debugging (overlay rendering)  
✅ Influence weight computation  

**Next**: Export to Unity and test physics/ragdoll behavior!
