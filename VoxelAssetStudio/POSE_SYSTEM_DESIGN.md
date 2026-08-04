# Voxel Skeleton Pose System Design

**Version**: 0.2.0  
**Date**: June 29, 2026  
**Inspired By**: Toribash (turn-based joint manipulation)  
**Status**: Design Phase

---

## Executive Summary

A **Toribash-inspired pose editor** for voxel skeletons that lets you manipulate individual joints to create custom poses, fighting stances, and animations. Instead of painting voxels, you **rotate joints** and the skeleton (with attached voxels) moves accordingly.

### Key Innovation

**Joint-based posing** instead of keyframe animation:
- Select a joint (hip, knee, neck, etc.)
- Set rotation state: **EXTEND**, **CONTRACT**, **RELAX**, **HOLD**
- Preview pose in real-time
- Save pose as preset
- Chain poses together for animation sequences

---

## Toribash System Analysis

### Core Mechanics

**Joint States** (4 options per joint):
1. **EXTEND** - Apply force to straighten joint (e.g., extend leg)
2. **CONTRACT** - Apply force to bend joint (e.g., bend knee)
3. **RELAX** - No force, joint moves freely with physics
4. **HOLD** - Lock joint at current angle

**Turn-Based Control**:
- Set all joint states
- Simulate physics for N frames (e.g., 10 frames)
- Repeat

**Result**: Emergent movement from simple joint commands

### Why This Works for Voxel Skeletons

✅ **Intuitive** - "Bend knee" is easier than "rotate 45° on X-axis"  
✅ **Discoverable** - Try different combinations, see what happens  
✅ **Reusable** - Save poses as presets (fighting stance, idle, crouch)  
✅ **Physics-ready** - States map directly to Unity joint motors  

---

## Voxel Pose System Design

### UI Layout

```
┌─────────────────────────────────────────────────────┐
│  Voxel Asset Studio - Pose Editor                  │
├─────────────────────────────────────────────────────┤
│  Skeleton: Biped (4 bones, 3 joints)                │
│                                                     │
│  ┌─────────────────┐  ┌─────────────────────────┐  │
│  │  Joint List     │  │  3D Viewport            │  │
│  │                 │  │                         │  │
│  │  ⊕ Hip (BALL)   │  │      🦴 Skeleton        │  │
│  │  ⊕ Mid Spine    │  │      with pose          │  │
│  │  ⊕ Neck         │  │                         │  │
│  │                 │  │                         │  │
│  │  [Selected:     │  │                         │  │
│  │   Hip Joint]    │  │                         │  │
│  │                 │  │                         │  │
│  │  Rotation X:    │  │                         │  │
│  │  ◀─────●────▶   │  │                         │  │
│  │  -45°  0°  45°  │  │                         │  │
│  │                 │  │                         │  │
│  │  Rotation Y:    │  │                         │  │
│  │  ◀─────●────▶   │  │                         │  │
│  │                 │  │                         │  │
│  │  Rotation Z:    │  │                         │  │
│  │  ◀─────●────▶   │  │                         │  │
│  │                 │  │                         │  │
│  │  [Reset Joint]  │  │                         │  │
│  │  [Reset All]    │  │                         │  │
│  └─────────────────┘  └─────────────────────────┘  │
│                                                     │
│  Pose Presets:                                      │
│  [Idle] [T-Pose] [Crouch] [Jump] [Custom 1]        │
│  [Save Pose...] [Load Pose...]                      │
└─────────────────────────────────────────────────────┘
```

### Joint Control System

**For Ball Joints** (hip, spine, neck):
```python
class BallJointControl:
    rotation_x: float  # -max_angle_x to +max_angle_x
    rotation_y: float  # -max_angle_y to +max_angle_y
    rotation_z: float  # -max_angle_z to +max_angle_z
    
    # Toribash-style states (optional)
    state_x: str  # 'EXTEND', 'CONTRACT', 'RELAX', 'HOLD'
    state_y: str
    state_z: str
```

**For Hinge Joints** (knees, elbows):
```python
class HingeJointControl:
    rotation: float  # min_angle to max_angle (single axis)
    state: str  # 'EXTEND', 'CONTRACT', 'RELAX', 'HOLD'
```

### Pose Data Structure

```python
pose = {
    'name': 'Fighting Stance',
    'description': 'Ready to strike',
    'joint_rotations': {
        0: {'x': 0.0, 'y': 0.0, 'z': 0.0},  # Hip
        1: {'x': 10.0, 'y': 0.0, 'z': 0.0},  # Mid spine (lean forward)
        2: {'x': -5.0, 'y': 0.0, 'z': 0.0}   # Neck (look up)
    },
    'thumbnail': 'base64_image_data'  # Preview image
}
```

---

## Implementation Phases

### Phase 1: Basic Joint Rotation (Week 1)

**Goal**: Manually rotate joints with sliders

```python
# In voxel_editor.py
class PoseEditor:
    def __init__(self, skeleton_data):
        self.skeleton = skeleton_data
        self.current_pose = self._get_default_pose()
        
    def rotate_joint(self, joint_id, axis, angle):
        """Rotate a joint and update voxel positions"""
        joint = self.skeleton['joints'][joint_id]
        
        # Clamp to joint limits
        if axis == 'x':
            angle = clamp(angle, -joint['max_angle_x'], joint['max_angle_x'])
        
        # Store rotation
        self.current_pose['joint_rotations'][joint_id][axis] = angle
        
        # Update bone transforms
        self._apply_pose_to_skeleton()
        
        # Move influenced voxels
        self._update_voxel_positions()
```

**UI**:
- Add "Pose Editor" tab to right sidebar
- List all joints
- Slider for each rotation axis
- Real-time preview in viewport

### Phase 2: Toribash-Style States (Week 2)

**Goal**: Use EXTEND/CONTRACT/RELAX/HOLD instead of raw angles

```python
class ToribashJointController:
    def apply_state(self, joint, state, delta_time):
        """Apply Toribash-style state to joint"""
        if state == 'EXTEND':
            # Apply force to straighten
            target_angle = joint['max_angle']
            current_angle = joint['current_angle']
            joint['current_angle'] += (target_angle - current_angle) * 0.1
            
        elif state == 'CONTRACT':
            # Apply force to bend
            target_angle = joint['min_angle']
            current_angle = joint['current_angle']
            joint['current_angle'] += (target_angle - current_angle) * 0.1
            
        elif state == 'RELAX':
            # No force, gravity/momentum takes over
            pass
            
        elif state == 'HOLD':
            # Lock at current angle
            pass
```

**UI**:
- Replace sliders with 4 buttons per axis: [E] [C] [R] [H]
- Click to toggle state
- Visual feedback (button highlights)

### Phase 3: Pose Presets (Week 3)

**Goal**: Save and load poses

```python
class PoseLibrary:
    def save_pose(self, name, description):
        """Save current pose to library"""
        pose = {
            'name': name,
            'description': description,
            'joint_rotations': copy.deepcopy(self.current_pose['joint_rotations']),
            'thumbnail': self._capture_thumbnail()
        }
        
        # Save to JSON file
        with open(f'poses/{name}.json', 'w') as f:
            json.dump(pose, f)
    
    def load_pose(self, name):
        """Load pose from library"""
        with open(f'poses/{name}.json', 'r') as f:
            pose = json.load(f)
        
        self.current_pose = pose
        self._apply_pose_to_skeleton()
```

**Built-in Presets**:
- **T-Pose**: Arms out, legs straight (default rigging pose)
- **Idle**: Slight slouch, relaxed stance
- **Crouch**: Knees bent, spine forward
- **Jump**: Legs extended, arms up
- **Fighting Stance**: One leg forward, arms ready

### Phase 4: Animation Sequencing (Week 4)

**Goal**: Chain poses together for simple animations

```python
class AnimationSequence:
    def __init__(self):
        self.keyframes = []  # List of (time, pose) tuples
        
    def add_keyframe(self, time, pose):
        """Add pose at specific time"""
        self.keyframes.append((time, pose))
        self.keyframes.sort(key=lambda x: x[0])
    
    def interpolate(self, time):
        """Get interpolated pose at time"""
        # Find surrounding keyframes
        before = None
        after = None
        
        for i, (t, pose) in enumerate(self.keyframes):
            if t <= time:
                before = (t, pose)
            if t >= time and after is None:
                after = (t, pose)
                break
        
        # Lerp between poses
        if before and after:
            t0, pose0 = before
            t1, pose1 = after
            alpha = (time - t0) / (t1 - t0)
            return self._lerp_poses(pose0, pose1, alpha)
```

**UI**:
- Timeline scrubber at bottom
- Add keyframe button
- Play/pause animation
- Export as .gif or .mp4

---

## Voxel Movement During Posing

### Challenge

When you rotate a joint, all voxels influenced by bones below that joint must move.

### Solution: Hierarchical Transform

```python
def _update_voxel_positions(self):
    """Move voxels based on bone transforms"""
    new_voxels = np.zeros_like(self.voxels)
    
    for voxel_pos, influences in self.skeleton['influence_map'].items():
        # Parse position
        x, y, z = map(int, voxel_pos.split(','))
        
        # Calculate weighted transform
        final_pos = np.array([0.0, 0.0, 0.0])
        
        for bone_id, weight in influences:
            bone = self.skeleton['bones'][bone_id]
            
            # Get bone's world transform (includes parent rotations)
            bone_transform = self._get_bone_world_transform(bone_id)
            
            # Transform voxel position
            local_pos = np.array([x, y, z])
            world_pos = bone_transform @ local_pos
            
            # Weighted blend
            final_pos += world_pos * weight
        
        # Round to voxel grid
        new_x, new_y, new_z = np.round(final_pos).astype(int)
        
        # Bounds check
        if (0 <= new_x < self.grid_size[0] and
            0 <= new_y < self.grid_size[1] and
            0 <= new_z < self.grid_size[2]):
            new_voxels[new_x, new_y, new_z] = self.voxels[x, y, z]
    
    self.voxels = new_voxels
    self.viewport.set_voxels(self.voxels)
```

---

## Use Cases

### 1. **Character Creation Workflow**

```
1. Generate standalone skeleton (8×32×8)
2. Enter Pose Editor
3. Set T-Pose (arms out, legs straight)
4. Paint flesh/armor around skeleton
5. Save as "character_base.stasset"
6. Create pose variations:
   - Idle stance
   - Fighting stance
   - Crouch
   - Jump
7. Export all poses to Unity
```

### 2. **Mech Design Workflow**

```
1. Generate quadruped skeleton
2. Enter Pose Editor
3. Set "standing" pose (legs extended)
4. Paint mech body around skeleton
5. Create poses:
   - Walking (leg 1 forward, leg 3 back)
   - Crouching (all legs bent)
   - Jumping (all legs extended)
6. Test in Unity with physics
```

### 3. **Animation Preview**

```
1. Load character with skeleton
2. Create animation sequence:
   - Frame 0: Idle
   - Frame 10: Crouch
   - Frame 20: Jump
   - Frame 30: Idle
3. Play animation in viewport
4. Export as .gif for portfolio
```

---

## Technical Advantages

### vs. Traditional Keyframe Animation

**Keyframe**:
- Manually set every bone rotation at every frame
- Time-consuming
- Hard to iterate

**Toribash-style**:
- Set joint states (EXTEND/CONTRACT)
- Physics simulates movement
- Emergent, natural motion

### vs. Mocap/IK

**Mocap**:
- Requires expensive equipment
- Limited to recorded motions

**IK**:
- Complex math
- Can be unpredictable

**Toribash-style**:
- Simple 4-state system
- Predictable results
- Easy to learn

---

## Next Steps

1. **Implement Phase 1** (basic joint rotation with sliders)
2. **Test with standalone skeleton** (verify voxels move correctly)
3. **Add pose presets** (T-pose, idle, crouch)
4. **User feedback** (is this intuitive? what's missing?)
5. **Iterate** based on testing

---

## Benefits Summary

✅ **Intuitive posing** - Rotate joints, not individual voxels  
✅ **Toribash-inspired** - EXTEND/CONTRACT/RELAX/HOLD states  
✅ **Pose library** - Save and reuse poses  
✅ **Animation sequencing** - Chain poses for simple animations  
✅ **Physics-ready** - States map to Unity joint motors  
✅ **Creative freedom** - Design unique characters beyond "block guys"  

**Result**: You can create **dynamic, poseable voxel characters** instead of static blocks!
