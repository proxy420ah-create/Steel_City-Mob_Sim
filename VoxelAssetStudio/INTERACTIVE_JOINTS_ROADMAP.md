# Interactive Joint Manipulation - Implementation Roadmap

**Version**: 1.0.0  
**Date**: June 29, 2026  
**Status**: Design Phase  
**Priority**: High (Core Feature)

---

## Vision

**Click on joint spheres in Voxel Studio to rotate joints and pose the skeleton in real-time.**

### User Workflow
```
1. Generate or paint skeleton (bone + joint voxels)
2. Click red/green joint sphere → Select joint
3. Drag to rotate joint → All bones below rotate
4. Voxels attached to bones move with rotation
5. Save posed skeleton to .stasset
6. Load in Unity → Physics takes over
```

---

## Current State (Phase 0)

✅ **Implemented**:
- Bone voxels (material 12, white/beige)
- Joint voxels (material 21, red)
- Joint sphere overlays (red=ball, green=hinge)
- Skeleton metadata (joints, bones)
- .stasset export/import
- Unity rendering

❌ **Not Implemented**:
- Joint selection (clicking spheres)
- Joint rotation (dragging)
- Voxel transformation (moving bones)
- Pose saving/loading
- Undo/redo for poses

---

## Implementation Phases

### **Phase 1: Joint Selection** (Week 1)

**Goal**: Click on joint sphere to select it

**Features**:
- Ray casting from mouse to 3D scene
- Detect which joint sphere was clicked
- Highlight selected joint (brighter color)
- Show joint properties in UI panel

**UI Changes**:
```
┌─────────────────────────────────────┐
│  Selected Joint: Left Hip (Ball)    │
│  Position: [7, 16, 4]               │
│  Rotation: [0°, 0°, 0°]             │
│  Constraints:                       │
│    X: ±45°                          │
│    Y: ±30°                          │
│    Z: ±30°                          │
│  [Reset Joint]                      │
└─────────────────────────────────────┘
```

**Code**:
```python
class VoxelViewport:
    def mousePressEvent(self, event):
        if self.skeleton_data and event.button() == Qt.LeftButton:
            # Ray cast to find clicked joint
            clicked_joint = self.raycast_to_joint(event.pos())
            if clicked_joint:
                self.select_joint(clicked_joint)
```

---

### **Phase 2: Joint Rotation** (Week 2)

**Goal**: Drag selected joint to rotate it

**Features**:
- Mouse drag rotates joint on X/Y/Z axes
- Visual feedback (rotating sphere)
- Respect joint constraints (min/max angles)
- Real-time bone line updates

**Controls**:
- **Left drag**: Rotate on X/Y axes (screen-space)
- **Shift + drag**: Rotate on Z axis
- **Ctrl + drag**: Fine control (slower rotation)
- **Right-click**: Reset joint to 0°

**Code**:
```python
class JointManipulator:
    def rotate_joint(self, joint_id, delta_x, delta_y):
        joint = self.skeleton['joints'][joint_id]
        
        # Convert screen-space drag to rotation
        rotation_x = delta_y * 0.5  # Vertical drag = X rotation
        rotation_y = delta_x * 0.5  # Horizontal drag = Y rotation
        
        # Apply constraints
        rotation_x = clamp(rotation_x, -joint['max_angle_x'], joint['max_angle_x'])
        rotation_y = clamp(rotation_y, -joint['max_angle_y'], joint['max_angle_y'])
        
        # Store rotation
        joint['current_rotation'] = [rotation_x, rotation_y, 0]
        
        # Update visualization
        self.update_bone_transforms()
```

---

### **Phase 3: Voxel Transformation** (Week 3)

**Goal**: Move voxels when joints rotate

**Challenge**: When you rotate a hip joint, all leg voxels must move.

**Solution**: Hierarchical transform chain
```python
def update_voxel_positions(self):
    """Move voxels based on joint rotations"""
    new_voxels = np.zeros_like(self.voxels)
    
    for voxel_pos, influences in self.influence_map.items():
        x, y, z = map(int, voxel_pos.split(','))
        
        # Calculate final position from all influencing bones
        final_pos = np.array([0.0, 0.0, 0.0])
        
        for bone_id, weight in influences:
            # Get bone's world transform (includes parent rotations)
            bone_transform = self.get_bone_world_transform(bone_id)
            
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
```

**Performance**: Only update voxels when joint rotation changes (not every frame)

---

### **Phase 4: Pose Presets** (Week 4)

**Goal**: Save and load poses

**Features**:
- Save current joint rotations as preset
- Load preset to apply rotations
- Built-in presets (T-Pose, Idle, Crouch, Jump)
- Custom user presets

**Pose File Format** (JSON):
```json
{
  "name": "Fighting Stance",
  "description": "Ready to strike",
  "joint_rotations": {
    "0": {"x": 0.0, "y": 0.0, "z": 0.0},
    "1": {"x": 10.0, "y": 0.0, "z": 0.0},
    "2": {"x": -5.0, "y": 0.0, "z": 0.0}
  }
}
```

**UI**:
```
Pose Presets:
[T-Pose] [Idle] [Crouch] [Jump] [Custom 1]
[Save Pose...] [Load Pose...]
```

---

### **Phase 5: Unity Integration** (Week 5)

**Goal**: Export posed skeleton to Unity

**Extended .stasset Format** (Version 2):
```python
# Add pose data to .stasset
{
    'magic': 'STAS',
    'version': 2,
    'dims': [18, 32, 8],
    'voxels': uint16_array,
    'skeleton': {
        'joints': [...],
        'bones': [...],
        'current_pose': {  # NEW
            'joint_rotations': {...}
        }
    }
}
```

**Unity Loader**:
```csharp
public class VoxelSkeletonLoader {
    public void LoadStAsset(string path) {
        // Load voxels
        var voxels = LoadVoxelData(path);
        
        // Load skeleton metadata
        var skeleton = LoadSkeletonData(path);
        
        // Create GameObject hierarchy
        var root = CreateSkeletonHierarchy(skeleton);
        
        // Apply saved pose
        ApplyPose(skeleton.current_pose);
        
        // Add physics components
        AddRigidbodies(root);
        AddJoints(root, skeleton);
    }
}
```

---

## Technical Challenges

### **1. Ray Casting to Spheres**

**Problem**: Detecting mouse clicks on 3D spheres

**Solution**: Use sphere-ray intersection math
```python
def raycast_to_joint(self, screen_pos):
    # Convert screen coords to 3D ray
    ray_origin, ray_direction = self.screen_to_ray(screen_pos)
    
    # Check each joint sphere
    for joint in self.skeleton['joints']:
        sphere_center = joint['position']
        sphere_radius = self.voxel_size * 2.5
        
        # Sphere-ray intersection test
        if ray_intersects_sphere(ray_origin, ray_direction, 
                                 sphere_center, sphere_radius):
            return joint
    
    return None
```

### **2. Hierarchical Transforms**

**Problem**: Rotating hip should rotate entire leg

**Solution**: Parent-child bone hierarchy
```python
def get_bone_world_transform(self, bone_id):
    """Get bone's transform including all parent rotations"""
    bone = self.bones[bone_id]
    transform = bone.local_transform
    
    # Walk up parent chain
    parent_joint = bone.parent_joint
    while parent_joint is not None:
        parent_bone = self.get_parent_bone(parent_joint)
        transform = parent_bone.local_transform @ transform
        parent_joint = parent_bone.parent_joint
    
    return transform
```

### **3. Voxel Gaps After Rotation**

**Problem**: Rotating bones can leave gaps in voxel data

**Solution**: 
- Option A: Interpolate voxels between rotations
- Option B: Accept gaps (user fills manually)
- Option C: Only rotate skeleton overlay, not voxels (until export)

**Recommendation**: Option C for Voxel Studio (preview only), apply transforms on Unity import

---

## UI Mockup

```
┌─────────────────────────────────────────────────────────┐
│  Voxel Asset Studio - Skeleton Pose Editor              │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────────────────┐   │
│  │  Joint List     │  │  3D Viewport                │   │
│  │                 │  │                             │   │
│  │  ⊕ Hip (BALL)   │  │      🦴 Skeleton            │   │
│  │  ⊕ Mid Spine    │  │      🔴 ← Selected joint    │   │
│  │  ⊕ Neck         │  │                             │   │
│  │  ⊗ L Knee       │  │  Click sphere to select     │   │
│  │  ⊗ R Knee       │  │  Drag to rotate             │   │
│  │                 │  │                             │   │
│  │  [Selected:     │  │                             │   │
│  │   Hip Joint]    │  │                             │   │
│  │                 │  │                             │   │
│  │  Rotation:      │  │                             │   │
│  │  X: 0°          │  │                             │   │
│  │  Y: 0°          │  │                             │   │
│  │  Z: 0°          │  │                             │   │
│  │                 │  │                             │   │
│  │  [Reset Joint]  │  │                             │   │
│  │  [Reset All]    │  │                             │   │
│  └─────────────────┘  └─────────────────────────────┘   │
│                                                          │
│  Pose Presets:                                           │
│  [T-Pose] [Idle] [Crouch] [Jump] [Custom 1]             │
│  [Save Pose...] [Load Pose...]                           │
└──────────────────────────────────────────────────────────┘
```

---

## Success Criteria

✅ **Phase 1**: Click joint sphere → highlights and shows properties  
✅ **Phase 2**: Drag joint sphere → rotates smoothly with constraints  
✅ **Phase 3**: Rotating joint → voxels move correctly  
✅ **Phase 4**: Save pose → load pose → same rotations  
✅ **Phase 5**: Export to Unity → pose preserved, physics works  

---

## Timeline

- **Week 1**: Joint selection (clickable spheres)
- **Week 2**: Joint rotation (dragging)
- **Week 3**: Voxel transformation (moving bones)
- **Week 4**: Pose presets (save/load)
- **Week 5**: Unity integration (export poses)

**Total**: 5 weeks to full interactive pose system

---

## Next Immediate Steps

1. ✅ Make joint spheres smaller and transparent (DONE)
2. ⏭️ Implement ray casting for joint selection
3. ⏭️ Add joint properties panel to UI
4. ⏭️ Test joint selection with mouse clicks

**Current Status**: Ready to begin Phase 1 (Joint Selection)
