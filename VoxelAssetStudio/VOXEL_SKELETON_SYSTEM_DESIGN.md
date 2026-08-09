# Voxel Skeleton System Design

**Version**: 1.0.0  
**Date**: June 29, 2026  
**Status**: Design Phase  
**Project**: SteelTide - Voxel Asset Studio

---

## Executive Summary

The **Voxel Skeleton System** enables creation of poseable voxel characters by embedding skeletal data directly into voxel volumes. The skeleton is created in **Voxel Asset Studio** (modeling tool) and brought to life in **Unity** (runtime engine) with physics, IK, and procedural animation.

### Key Innovation

**Separation of Concerns**:
- **Voxel Asset Studio**: Create skeleton + body, export `.stasset` with metadata
- **Unity**: Import skeleton, add physics/joints, animate with IK

Instead of choosing between pre-recorded frames OR runtime voxel manipulation, we embed a **virtual skeleton** using special voxel materials. Bone voxels act as IK targets - when IK solves bone positions (in Unity), we move the voxel clusters influenced by those bones.

### Tool Responsibilities

**Voxel Asset Studio (Python)** - Asset Creation:
- ✅ Generate standalone skeleton (bone voxels)
- ✅ Paint/sculpt body around skeleton
- ✅ Optional: Simple pose editor for reference poses (T-pose, idle)
- ✅ Export `.stasset` with skeleton metadata
- ❌ NO physics simulation
- ❌ NO real-time animation
- ❌ NO procedural walk cycles

**Unity (C#)** - Runtime Animation:
- ✅ Import `.stasset` with skeleton
- ✅ Add Rigidbody + joints (physics)
- ✅ IK solver (FABRIK)
- ✅ Procedural animation (walk cycles, terrain adaptation)
- ✅ Damage system integration
- ✅ Real-time pose manipulation

---

## Core Concept

### Continuous vs Discrete Architecture

**Critical Design Decision**: The system uses **continuous float storage** for skeleton data with **discrete voxel snapping** for data manipulation, enabling smooth 60fps animation despite voxel grid constraints.

#### Three-Layer Architecture

**1. Data Layer (Discrete)**
```python
# Voxel volume: Integer grid positions
volume[32, 24, 16] = BONE_VOXEL  # Discrete storage (ushort array)

# Influence map: Integer keys, float weights
influence_map = {
    (32, 24, 16): [(bone_id=0, weight=1.0)],  # Grid position, float weight
    (32, 25, 16): [(0, 0.7), (1, 0.3)]        # Blended influence
}
```

**2. Skeleton Layer (Continuous)**
```python
# Bones: Float positions and rotations
bones = [
    {
        'id': 0,
        'position': [32.47, 24.83, 16.12],  # CONTINUOUS (floats)
        'rotation': [0.0, 0.0, 0.0, 1.0],   # Quaternion (floats)
        'length': 8.5                        # FLOAT
    }
]

# Joints: Float positions and constraints
joints = [
    {
        'id': 0,
        'position': [32.5, 24.0, 16.0],     # CONTINUOUS (sub-voxel precision)
        'axis': [1.0, 0.0, 0.0],            # Normalized vector (floats)
        'min_angle': -150.0,                 # FLOAT (degrees)
        'max_angle': 0.0
    }
]
```

**3. Rendering Layer (Continuous)**
```csharp
// Unity: Bones move in continuous space
Vector3 bonePosition = new Vector3(32.47f, 24.83f, 16.12f);  // FLOATS

// IK solver: Works in continuous space (smooth)
ikSolver.SolveLegChain(leg, targetFootPos);

// Rendering: Sub-voxel interpolation
Vector3 renderPos = boneTransform.MultiplyPoint3x4(voxelGridPos);
GenerateVoxelMesh(renderPos);  // Smooth visual result
```

#### Performance Optimization: Boundary-Crossing Updates

Voxel data only updates when bones cross voxel boundaries:

```
Bone moves 0.1 units per frame (continuous)
├─ Frames 1-9: Bone at (32.1, 32.2, ..., 32.9)
│  └─ Voxel data: UNCHANGED (still at grid position 32)
│  └─ Rendering: SMOOTH (uses continuous bone position)
│
└─ Frame 10: Bone crosses to 33.0
   └─ Voxel data: UPDATED (move from 32 to 33)
   └─ Rendering: SMOOTH (continuous interpolation)

Result: Only 1 voxel data update per 10 frames
        But 60fps smooth rendering throughout
```

**Benefits**:
- ✅ **Smooth animation** - IK solves in continuous space
- ✅ **Reduced updates** - Voxel data changes only at boundaries
- ✅ **No jitter** - Sub-voxel interpolation for rendering
- ✅ **Precise constraints** - Joint limits stored as floats
- ✅ **Performance-friendly** - 90% fewer voxel data updates

### Material System Extension

```python
# Existing materials (0-20)
AIR = 0
DURASTEEL = 5
REACTIVE_ARMOR = 16
# ... etc

# NEW: Skeleton materials (253-255)
BONE_VOXEL = 253        # Rigid bone segment (invisible, indestructible)
JOINT_VOXEL = 254       # Rotation center with constraints
ATTACHMENT_VOXEL = 255  # Equipment mount point
```

### Rendering Behavior

- **Normal mode**: Skeleton voxels invisible (not rendered)
- **Skeleton-only mode**: Only skeleton voxels rendered (for authoring)
- **Damage exposure**: Skeleton becomes visible when surrounding voxels destroyed
- **Debug mode**: Shows joint constraints and IK targets

### Architecture

```
Single Voxel Volume (64×64×64)
├─ Visible Voxels (armor, flesh, mechanical parts) - Destructible
├─ Bone Voxels (skeleton segments) - Indestructible
├─ Joint Voxels (rotation centers) - Indestructible
└─ Attachment Voxels (weapon mounts) - Indestructible
```

---

## Skeleton Voxel Types

### 1. BONE_VOXEL (Rigid Segment)

Defines rigid bone segments connecting joints.

```python
class BoneVoxel:
    id: int                    # Unique identifier
    start: Vector3Int          # Start position in volume
    end: Vector3Int            # End position in volume
    length: int                # Length in voxels
    parent_joint: int          # Parent joint ID (None for root)
    child_joint: int           # Child joint ID
    influenced_voxels: List    # Voxels that move with this bone
```

**Visual**:
```
Hip Joint ─────── Bone Segment (8 voxels) ─────── Knee Joint
    ⊕                    ║║║║║║║║                      ⊗
```

### 2. JOINT_VOXEL (Rotation Center)

Defines rotation centers with constraints.

**Hinge Joint** (1-axis rotation):
```python
class HingeJoint:
    id: int
    position: Vector3Int
    axis: str                  # 'X', 'Y', or 'Z'
    min_angle: float           # Minimum rotation (degrees)
    max_angle: float           # Maximum rotation (degrees)
```

**Example**: Knee joint
```python
knee = HingeJoint(
    position=(32, 24, 32),
    axis='X',
    min_angle=-150,  # Bends backward only
    max_angle=0
)
```

**Ball Joint** (3-axis rotation):
```python
class BallJoint:
    id: int
    position: Vector3Int
    max_angle_x: float
    max_angle_y: float
    max_angle_z: float
```

**Example**: Hip joint
```python
hip = BallJoint(
    position=(32, 48, 32),
    max_angle_x=90,
    max_angle_y=45,
    max_angle_z=30
)
```

### 3. ATTACHMENT_VOXEL (Equipment Mount)

Defines attachment points for weapons/equipment.

```python
class AttachmentPoint:
    id: int
    position: Vector3Int
    type: str                  # 'weapon', 'equipment', 'turret'
    orientation: Quaternion    # Default orientation
    parent_bone: int           # Bone this follows
```

---

## Authoring Workflow

### Phase 1: Sculpting (Existing)

Build character/mech with normal voxels:
```python
mech = VoxelAsset(dims=(64, 64, 64))
mech.fill_region(start=(28, 40, 28), end=(36, 56, 36), material=DURASTEEL)
```

### Phase 2: Rigging (NEW)

Switch to Skeleton Editing Mode:

```python
# Enable skeleton-only view
studio.set_render_mode('SKELETON_ONLY')

# Place bone chain
studio.place_bone_chain(
    start=(32, 16, 32),   # Foot
    end=(32, 48, 32),     # Hip
    segments=3,           # Creates 3 bones + joints
    joint_types=['BALL', 'HINGE', 'HINGE']
)

# Configure joint constraints
studio.configure_joint(
    joint_id=1,  # Knee
    type='HINGE',
    axis='X',
    limits=(-150, 0)
)

# Auto-assign influence weights
studio.auto_assign_weights(bone_id=0, radius=8)

# Manual paint for complex areas
studio.paint_influence(bone_id=2, brush_size=3)
```

### Phase 3: Animation Testing (NEW)

Preview animations before export:

```python
# Test walk cycle
studio.test_animation(type='walk_cycle', speed=1.0)

# Test terrain adaptation
studio.test_animation(
    type='walk_cycle',
    terrain=studio.generate_test_terrain(roughness=0.5)
)

# Test damage response
studio.simulate_damage(target='right_leg', damage_percent=75)
studio.test_animation(type='walk_cycle')  # See 3-legged walk
```

### Phase 4: Export

```python
studio.export_asset(
    filename='quadruped_mech.stasset',
    include_skeleton=True
)
```

---

## File Format (.stasset Extended)

```python
{
    'version': '2.0.0',
    'dims': [64, 64, 64],
    'volume': np.array(...),  # Includes skeleton voxels
    
    'skeleton': {
        'bones': [
            {
                'id': 0,
                'start': [32, 16, 32],
                'end': [32, 24, 32],
                'length': 8
            }
        ],
        
        'joints': [
            {
                'id': 0,
                'type': 'HINGE',
                'position': [32, 24, 32],
                'axis': 'X',
                'min_angle': -150,
                'max_angle': 0
            }
        ],
        
        'influence_map': {
            '32,17,32': [(0, 1.0)],           # Bone 0, weight 1.0
            '32,23,32': [(0, 0.5), (1, 0.5)]  # Blended
        },
        
        'attachments': [
            {
                'id': 0,
                'type': 'weapon',
                'position': [40, 32, 32],
                'parent_bone': 5
            }
        ]
    }
}
```

---

## Runtime Animation System

### Unity Component

```csharp
public class VoxelSkeletonAnimator : MonoBehaviour {
    public VoxelAsset asset;
    private VoxelSkeleton skeleton;
    private VoxelIKSolver ikSolver;
    private Dictionary<Vector3Int, Vector3> voxelRenderPositions;  // CONTINUOUS positions
    
    void Start() {
        skeleton = ExtractSkeleton(asset);
        ikSolver = new VoxelIKSolver(skeleton);
        voxelRenderPositions = new Dictionary<Vector3Int, Vector3>();
    }
    
    void Update() {
        // 1. Solve IK for all limbs (CONTINUOUS space)
        foreach (var leg in skeleton.legs) {
            Vector3 target = CalculateFootTarget(leg);
            ikSolver.SolveLegChain(leg, target);  // Returns continuous positions
        }
        
        // 2. Update bone transforms (CONTINUOUS)
        UpdateBoneTransforms();
        
        // 3. Apply transforms to influenced voxels (boundary-crossing only)
        ApplyBoneTransformsToVoxels();
        
        // 4. Update renderer with interpolated positions (SMOOTH)
        voxelRenderer.UpdateWithInterpolation(voxelData, voxelRenderPositions);
    }
    
    void UpdateBoneTransforms() {
        foreach (var bone in skeleton.bones) {
            // Store continuous position (floats)
            bone.continuousPosition = ikSolver.GetBonePosition(bone.id);
            
            // Calculate discrete voxel position
            Vector3Int newVoxelPos = Vector3Int.RoundToInt(bone.continuousPosition);
            
            // Only update voxel data if crossed boundary
            if (newVoxelPos != bone.voxelPosition) {
                bone.voxelPosition = newVoxelPos;
                bone.hasCrossedBoundary = true;
            }
        }
    }
}
```

### IK Solver (FABRIK Algorithm)

```csharp
public class VoxelIKSolver {
    private int maxIterations = 10;
    private float tolerance = 0.01f;
    
    public void SolveLegChain(Leg leg, Vector3 targetFootPos) {
        // FABRIK (Forward And Backward Reaching Inverse Kinematics)
        // Works in CONTINUOUS space for smooth results
        
        List<Bone> chain = leg.bones;
        Vector3 rootPos = leg.hipPosition;
        
        for (int iteration = 0; iteration < maxIterations; iteration++) {
            // Forward pass: Move end effector to target
            chain[chain.Count - 1].continuousPosition = targetFootPos;
            
            // Propagate backward up the chain
            for (int i = chain.Count - 1; i > 0; i--) {
                Bone currentBone = chain[i];
                Bone parentBone = chain[i - 1];
                
                Vector3 direction = (parentBone.continuousPosition - currentBone.continuousPosition).normalized;
                parentBone.continuousPosition = currentBone.continuousPosition + direction * currentBone.length;
                
                // Apply joint constraints (in continuous space)
                if (currentBone.parentJoint != null) {
                    ApplyJointConstraints(currentBone.parentJoint, ref parentBone.continuousPosition);
                }
            }
            
            // Backward pass: Restore root position
            chain[0].continuousPosition = rootPos;
            
            // Propagate forward down the chain
            for (int i = 0; i < chain.Count - 1; i++) {
                Bone currentBone = chain[i];
                Bone childBone = chain[i + 1];
                
                Vector3 direction = (childBone.continuousPosition - currentBone.continuousPosition).normalized;
                childBone.continuousPosition = currentBone.continuousPosition + direction * currentBone.length;
                
                // Apply joint constraints
                if (childBone.parentJoint != null) {
                    ApplyJointConstraints(childBone.parentJoint, ref childBone.continuousPosition);
                }
            }
            
            // Check convergence
            float distance = Vector3.Distance(chain[chain.Count - 1].continuousPosition, targetFootPos);
            if (distance < tolerance) break;
        }
        
        // CRITICAL: Snap to voxel grid for data manipulation
        // But KEEP continuous positions for rendering
        foreach (var bone in chain) {
            bone.voxelPosition = SnapToVoxelGrid(bone.continuousPosition);
        }
    }
    
    Vector3Int SnapToVoxelGrid(Vector3 continuousPos) {
        // Round to nearest voxel grid position
        return new Vector3Int(
            Mathf.RoundToInt(continuousPos.x),
            Mathf.RoundToInt(continuousPos.y),
            Mathf.RoundToInt(continuousPos.z)
        );
    }
    
    void ApplyJointConstraints(Joint joint, ref Vector3 position) {
        // Constraints work in CONTINUOUS space (float angles)
        if (joint.type == JointType.HINGE) {
            // Calculate current angle
            Vector3 parentDir = (joint.parentBone.continuousPosition - joint.position).normalized;
            Vector3 childDir = (position - joint.position).normalized;
            
            float angle = Vector3.SignedAngle(parentDir, childDir, joint.axis);
            
            // Clamp to constraint limits (FLOAT precision)
            angle = Mathf.Clamp(angle, joint.minAngle, joint.maxAngle);
            
            // Recalculate position from clamped angle
            Quaternion rotation = Quaternion.AngleAxis(angle, joint.axis);
            Vector3 direction = rotation * parentDir;
            position = joint.position + direction * joint.childBone.length;
        }
        else if (joint.type == JointType.BALL) {
            // Clamp rotation on all axes (FLOAT precision)
            Vector3 parentDir = (joint.parentBone.continuousPosition - joint.position).normalized;
            Vector3 childDir = (position - joint.position).normalized;
            
            Quaternion rotation = Quaternion.FromToRotation(parentDir, childDir);
            Vector3 angles = rotation.eulerAngles;
            
            // Clamp each axis
            angles.x = ClampAngle(angles.x, -joint.maxAngleX, joint.maxAngleX);
            angles.y = ClampAngle(angles.y, -joint.maxAngleY, joint.maxAngleY);
            angles.z = ClampAngle(angles.z, -joint.maxAngleZ, joint.maxAngleZ);
            
            // Recalculate position
            rotation = Quaternion.Euler(angles);
            Vector3 direction = rotation * parentDir;
            position = joint.position + direction * joint.childBone.length;
        }
    }
    
    float ClampAngle(float angle, float min, float max) {
        if (angle > 180) angle -= 360;
        return Mathf.Clamp(angle, min, max);
    }
}
```

### Smooth Voxel Renderer (Sub-Voxel Interpolation)

```csharp
public class SmoothVoxelRenderer : MonoBehaviour {
    private Dictionary<Vector3Int, Vector3> voxelRenderPositions;  // CONTINUOUS
    private ushort[] voxelData;  // DISCRETE
    private Mesh voxelMesh;
    
    public void UpdateWithInterpolation(ushort[] newVoxelData, 
                                       Dictionary<int, Matrix4x4> boneTransforms) {
        // For each voxel that's influenced by a bone
        foreach (var kvp in voxelInfluenceMap) {
            Vector3Int gridPos = kvp.Key;
            int boneId = kvp.Value;
            
            // Get bone's continuous transform
            Matrix4x4 boneTransform = boneTransforms[boneId];
            
            // Calculate continuous render position
            Vector3 localOffset = new Vector3(gridPos.x, gridPos.y, gridPos.z);
            Vector3 renderPos = boneTransform.MultiplyPoint3x4(localOffset);
            
            // Store for mesh generation (FLOATS, not snapped to grid)
            voxelRenderPositions[gridPos] = renderPos;
        }
        
        // Regenerate mesh with interpolated positions
        RegenerateMesh();
    }
    
    void RegenerateMesh() {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        
        foreach (var kvp in voxelRenderPositions) {
            Vector3Int gridPos = kvp.Key;
            Vector3 renderPos = kvp.Value;  // CONTINUOUS position
            
            ushort voxel = voxelData[GetIndex(gridPos)];
            if (voxel == 0) continue;  // Skip air
            
            // Generate cube at CONTINUOUS position (smooth)
            AddCubeToMesh(renderPos, voxelSize, vertices, triangles);
        }
        
        voxelMesh.Clear();
        voxelMesh.vertices = vertices.ToArray();
        voxelMesh.triangles = triangles.ToArray();
        voxelMesh.RecalculateNormals();
    }
    
    void AddCubeToMesh(Vector3 position, float size, 
                      List<Vector3> vertices, List<int> triangles) {
        // Generate cube vertices at CONTINUOUS position
        // No snapping to grid - smooth sub-voxel positioning
        int startIndex = vertices.Count;
        
        // 8 vertices of cube (using continuous position)
        vertices.Add(position + new Vector3(-0.5f, -0.5f, -0.5f) * size);
        vertices.Add(position + new Vector3(0.5f, -0.5f, -0.5f) * size);
        // ... (remaining 6 vertices)
        
        // 12 triangles (6 faces × 2 triangles)
        // ... (triangle indices)
    }
}
```

### Procedural Walk Cycle

```csharp
public class ProceduralWalkController : MonoBehaviour {
    void Update() {
        // Move body forward
        transform.position += transform.forward * walkSpeed * Time.deltaTime;
        
        // Check each leg for step requirement
        foreach (var leg in animator.skeleton.legs) {
            Vector3 idealPos = GetIdealFootPosition(leg);
            float distance = Vector3.Distance(leg.footPosition, idealPos);
            
            if (distance > stepDistance) {
                StartCoroutine(StepLeg(leg, idealPos));
            }
        }
    }
    
    IEnumerator StepLeg(Leg leg, Vector3 target) {
        leg.isStepping = true;
        Vector3 startPos = leg.footPosition;
        Vector3 midPos = (startPos + target) / 2 + Vector3.up * stepHeight;
        
        float duration = 0.3f;
        float elapsed = 0f;
        
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            // Bezier curve for natural step (CONTINUOUS)
            Vector3 pos = BezierCurve(startPos, midPos, target, t);
            
            // IK solver moves leg to CONTINUOUS position
            animator.ikSolver.SolveLegChain(leg, pos);
            
            yield return null;
        }
        
        leg.footPosition = target;
        leg.isStepping = false;
    }
    
    Vector3 BezierCurve(Vector3 p0, Vector3 p1, Vector3 p2, float t) {
        float u = 1 - t;
        return u * u * p0 + 2 * u * t * p1 + t * t * p2;
    }
}
```

### Terrain Adaptation

```csharp
public class TerrainAdaptation : MonoBehaviour {
    void Update() {
        foreach (var leg in animator.skeleton.legs) {
            // Raycast down to find ground
            RaycastHit hit;
            if (Physics.Raycast(leg.hipPosition + Vector3.up * 2, Vector3.down, out hit, 10f)) {
                // IK extends/contracts leg to reach ground
                animator.ikSolver.SolveLegChain(leg, hit.point);
            }
        }
    }
}
```

---

## Damage System Integration

### Indestructible Skeleton

```csharp
void OnProjectileHit(Vector3Int hitPos, float damage) {
    ushort voxel = voxelData[hitPos];
    
    // Skeleton voxels are indestructible
    if (IsSkeletonVoxel(voxel)) {
        return;  // Projectile passes through
    }
    
    // Destroy visible voxels
    if (damage > GetHardness(voxel) * 100) {
        ExtractDebris(hitPos, radius: 8);
        ClearRegion(hitPos, radius: 8, preserveSkeleton: true);
        
        // Skeleton remains intact - armor peeled away
    }
}
```

### Progressive Damage Visualization

```
Frame 0: Pristine Mech
████████  ← Armor (visible)
██ ⊕ ██  ← Hip bone (invisible)
  ████    ← Hydraulics (visible)
██ ⊗ ██  ← Knee joint (invisible)

Frame 50: Armor Damaged
██  ░░    ← Armor destroyed
░░ ⊕ ░░  ← Hip bone EXPOSED (now visible)
  ░░      ← Hydraulics damaged
░░ ⊗ ░░  ← Knee joint EXPOSED

Frame 100: Skeleton Fully Exposed
░░  ░░    ← All armor gone
░░ ⊕ ░░  ← Skeleton visible
  ░░      ← All hydraulics destroyed
░░ ⊗ ░░  ← Joints exposed

⊕ = Ball joint (indestructible)
⊗ = Hinge joint (indestructible)
█ = Visible voxels (destructible)
░ = Air (destroyed)
```

**Visual Effect**: Mech becomes "skeletal walker" - terrifying and functional even when stripped of armor.

---

## Voxel Studio Implementation

### New UI Components

1. **Skeleton Editing Mode**
   - Place Bones tool
   - Place Joints tool
   - Paint Weights tool
   - Place Attachments tool

2. **Render Mode Selector**
   - Full View (normal editing)
   - Skeleton Only (hide visible voxels)
   - Skeleton Overlay (transparent mesh + skeleton)
   - Influence Zones (color by bone influence)

3. **Animation Preview Panel**
   - Playback controls (play/pause/stop/speed)
   - Test scenarios (walk, terrain, aim, damage)
   - Visualization options (skeleton, targets, constraints)
   - Timeline scrubber

### Core Functions

```python
class SkeletonEditor:
    def place_bone_chain(self, start, end, segments, joint_types):
        """Place chain of bones with joints"""
        
    def auto_assign_weights(self, bone_id, radius=8, falloff='linear'):
        """Auto-assign influence weights to nearby voxels"""
        
    def mirror_skeleton(self, axis='X'):
        """Mirror skeleton across axis"""
        
class AnimationTester:
    def test_walk_cycle(self, speed=1.0, terrain='flat'):
        """Preview procedural walk animation"""
        
    def test_terrain_adaptation(self, terrain_heightmap):
        """Test IK on uneven terrain"""
        
    def simulate_damage(self, target, damage_percent):
        """Simulate damage to test skeleton exposure"""
```

---

## Performance Considerations

### Optimization Strategies

1. **Voxel Update Batching**: Only update moved voxels (~100-500 vs 262,144)
2. **Influence Map Caching**: Pre-compute bone→voxel mappings at load time
3. **IK Update Frequency**: Legs 60fps, arms 30fps, accessories 15fps
4. **Skeleton Voxel Culling**: Don't render skeleton in normal mode
5. **Spatial Partitioning**: Use octree for large assets

### Performance Targets

| Asset Size | Bone Count | Target FPS | Updates/Frame |
|------------|------------|------------|---------------|
| 32³ (Small) | 10-20 | 60 | <200 voxels |
| 64³ (Medium) | 20-40 | 60 | <500 voxels |
| 128³ (Large) | 40-80 | 30-60 | <1000 voxels |

---

## Implementation Roadmap

### Phase 1: Foundation (Week 1-2)
- [ ] Add skeleton materials (253-255) to material system
- [ ] Extend .stasset format with skeleton data
- [ ] Implement skeleton data structures (Bone, Joint, Attachment)
- [ ] Create skeleton-only render mode in Voxel Studio

### Phase 2: Authoring Tools (Week 3-4)
- [ ] Implement bone placement tool
- [ ] Implement joint placement tool
- [ ] Implement weight painting tool
- [ ] Add auto-assign weights function
- [ ] Add skeleton mirroring function

### Phase 3: Animation Testing (Week 5-6)
- [ ] Implement IK solver (FABRIK algorithm)
- [ ] Create animation preview system
- [ ] Add walk cycle test
- [ ] Add terrain adaptation test
- [ ] Add damage simulation test

### Phase 4: Unity Runtime (Week 7-8)
- [ ] Create VoxelSkeletonAnimator component
- [ ] Implement VoxelIKSolver in C#
- [ ] Create ProceduralWalkController
- [ ] Create TerrainAdaptation system
- [ ] Integrate with damage system

### Phase 5: Polish & Optimization (Week 9-10)
- [ ] Optimize voxel update batching
- [ ] Implement spatial partitioning
- [ ] Add debug visualization (gizmos)
- [ ] Performance profiling and tuning
- [ ] Documentation and examples

---

## Benefits Summary

✅ **Runtime procedural animation** - IK works on bone voxels  
✅ **Smooth 60fps animation** - Continuous float storage + sub-voxel interpolation  
✅ **Dynamic terrain adaptation** - Legs adjust to ground in real-time  
✅ **Damage system compatible** - Armor peels, skeleton remains functional  
✅ **No mesh dependency** - Pure voxel system  
✅ **Authoring-time flexibility** - Place bones like sculpting  
✅ **Performance-friendly** - Only update voxels at boundary crossings (90% reduction)  
✅ **Visual progression** - Skeleton exposure as damage increases  
✅ **Works with VoxelWorld** - Bone voxels are just special materials  
✅ **No animation jitter** - Float constraints prevent quantization issues  

---

## Smooth Animation Summary

### How Smooth Animation Works

**Problem**: Voxels are discrete (integer grid), but smooth animation requires continuous movement.

**Solution**: Three-layer architecture separates concerns:

1. **Skeleton Layer** (Continuous)
   - Bones store float positions: `(32.47, 24.83, 16.12)`
   - IK solves in continuous space (smooth)
   - Joint constraints use float angles (precise)

2. **Data Layer** (Discrete)
   - Voxel array uses integer positions: `[32, 25, 16]`
   - Only updates when bones cross voxel boundaries
   - 90% fewer updates than per-frame manipulation

3. **Rendering Layer** (Continuous)
   - Mesh vertices use continuous bone positions
   - Sub-voxel interpolation for smooth visuals
   - 60fps smooth animation despite discrete data

**Result**: Smooth, fluid animation that looks natural while maintaining voxel data integrity.

### Addressing Voxel Snapping Concerns

**Problem**: IK solves continuous positions `(32.7, 24.3, 32.1)`, but voxel data requires integer positions `(33, 24, 32)`. Naive snapping causes jitter.

**Solution**: Dual-position tracking with boundary-crossing updates.

```csharp
public class Bone {
    // CONTINUOUS position (used by IK and rendering)
    public Vector3 continuousPosition;  // (32.7, 24.3, 32.1)
    
    // DISCRETE position (used for voxel data)
    public Vector3Int voxelPosition;    // (33, 24, 32)
    
    // Track boundary crossings
    public bool hasCrossedBoundary;
}

void Update() {
    // 1. IK solves in CONTINUOUS space
    ikSolver.SolveLegChain(leg, targetFootPos);
    // Result: bone.continuousPosition = (32.7, 24.3, 32.1)
    
    // 2. Snap to grid for voxel data
    Vector3Int newVoxelPos = SnapToVoxelGrid(bone.continuousPosition);
    // Result: newVoxelPos = (33, 24, 32)
    
    // 3. Only update voxel data if crossed boundary
    if (newVoxelPos != bone.voxelPosition) {
        // Voxel crossed from (32, 24, 32) to (33, 24, 32)
        MoveVoxelCluster(bone.voxelPosition, newVoxelPos);
        bone.voxelPosition = newVoxelPos;
    }
    
    // 4. Render at CONTINUOUS position (no jitter)
    voxelRenderer.UpdateWithInterpolation(voxelData, boneTransforms);
    // Uses bone.continuousPosition for smooth visuals
}
```

**Why This Prevents Jitter**:

1. **Rendering uses continuous positions** - Mesh vertices are at `(32.7, 24.3, 32.1)`, not snapped
2. **Voxel data updates only at boundaries** - No rapid back-and-forth snapping
3. **Sub-voxel interpolation** - Smooth transition between grid positions
4. **No precision loss** - IK target accuracy maintained in continuous space

**Example Timeline**:

```
Frame 1: IK solves to (32.1, 24.0, 32.0)
├─ Voxel position: (32, 24, 32) - NO CHANGE
└─ Render position: (32.1, 24.0, 32.0) - SMOOTH

Frame 2: IK solves to (32.3, 24.0, 32.0)
├─ Voxel position: (32, 24, 32) - NO CHANGE
└─ Render position: (32.3, 24.0, 32.0) - SMOOTH

Frame 3: IK solves to (32.5, 24.0, 32.0)
├─ Voxel position: (33, 24, 32) - BOUNDARY CROSSED! Update data
└─ Render position: (32.5, 24.0, 32.0) - SMOOTH (no visual snap)

Frame 4: IK solves to (32.7, 24.0, 32.0)
├─ Voxel position: (33, 24, 32) - NO CHANGE
└─ Render position: (32.7, 24.0, 32.0) - SMOOTH

Result: Voxel data updates once (frame 3), but rendering is smooth every frame.
        No jitter, no drift, precise IK targeting maintained.
```

**Drift Prevention**: Since IK works in continuous space and rendering uses continuous positions, there's no accumulation of rounding errors. The voxel grid position is just a "shadow" of the true continuous position, updated only when necessary for data manipulation.

### Performance Impact

```
Traditional Approach (per-frame voxel updates):
- 500 voxels × 60 fps = 30,000 updates/second
- High CPU cost, potential stuttering

Boundary-Crossing Approach (this system):
- 500 voxels × 6 fps (avg boundary crossings) = 3,000 updates/second
- 90% reduction in voxel data updates
- Rendering still 60fps smooth via interpolation
```

---

## Design Concerns Addressed

### ✅ Material ID Range (253-255)
**Status**: Solved  
**Solution**: Material system uses 0-20, so 253-255 is clear with plenty of buffer space.

### ✅ Voxel Snapping During IK
**Status**: Solved  
**Solution**: Dual-position tracking (continuous + discrete) with boundary-crossing updates. IK solves in continuous space, rendering uses continuous positions (no jitter), voxel data updates only at grid boundaries. See "Addressing Voxel Snapping Concerns" section (lines 841-911).

### ✅ Influence Weight Complexity
**Status**: Solved  
**Solution**: Auto-assign by radius (line 173) + manual paint tool (line 176). Provides both automation and artist control for complex geometry.

### ✅ Performance of Voxel Cluster Movement
**Status**: Solved  
**Solution**: Dedicated optimization section (lines 913-923) with batching, caching, update frequency optimization, and specific performance targets per asset size. Boundary-crossing approach reduces updates by 90%.

### ✅ Joint Constraint Precision
**Status**: Solved  
**Solution**: Constraints stored as floats (min_angle, max_angle in degrees). IK solver applies constraints in continuous space before snapping to grid. No quantization issues.

---

## Next Steps

1. Review and approve design document
2. Begin Phase 1 implementation (material system extension)
3. Create proof-of-concept: Simple 2-bone leg with IK + smooth rendering
4. Verify sub-voxel interpolation produces smooth visuals
5. Test boundary-crossing logic prevents jitter
6. Iterate based on testing results

**Status**: All design concerns addressed. Ready for implementation approval.
