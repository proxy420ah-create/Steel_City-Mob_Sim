# Steel Tide: Voxel Asset Studio
# skeleton_generator.py - Auto-generate skeleton from voxel volume

import numpy as np
from typing import Dict, List, Tuple, Optional

class SkeletonGenerator:
    """Auto-generate skeleton from existing voxel volume"""
    
    @staticmethod
    def generate_standalone_biped_skeleton(grid_size: Tuple[int, int, int] = (8, 32, 8)) -> Tuple[np.ndarray, Dict]:
        """
        Generate a standalone skeleton model made entirely of bone voxels.
        Returns both the voxel volume AND skeleton data.
        
        Args:
            grid_size: Size of voxel grid (default: 8×32×8 for humanoid)
            
        Returns:
            (voxels, skeleton_data): Voxel array with bone material + skeleton metadata
        """
        # Create empty volume
        voxels = np.zeros(grid_size, dtype=np.uint8)
        
        # Center position
        center_x = grid_size[0] // 2
        center_z = grid_size[2] // 2
        
        # Height segments
        height = grid_size[1]
        root_y = 0
        lower_spine_y = height // 4
        mid_spine_y = height // 2
        upper_spine_y = 3 * height // 4
        head_y = height - 1
        
        # Material ID for bones (use material 253 = BONE_VOXEL)
        # For now, use material 5 (Durasteel) as placeholder until we add 253
        BONE_MATERIAL = 5
        
        # Draw spine as vertical line of voxels
        for y in range(root_y, head_y + 1):
            voxels[center_x, y, center_z] = BONE_MATERIAL
        
        # Draw joints as slightly thicker voxels
        joint_positions = [lower_spine_y, mid_spine_y, upper_spine_y]
        for joint_y in joint_positions:
            # Make joints 3×3 in XZ plane
            for dx in [-1, 0, 1]:
                for dz in [-1, 0, 1]:
                    x = center_x + dx
                    z = center_z + dz
                    if 0 <= x < grid_size[0] and 0 <= z < grid_size[2]:
                        voxels[x, joint_y, z] = BONE_MATERIAL
        
        # Generate skeleton metadata
        bones = []
        joints = []
        
        # Bone 0: Root to lower spine
        bones.append({
            'id': 0,
            'name': 'pelvis',
            'start': [int(center_x), int(root_y), int(center_z)],
            'end': [int(center_x), int(lower_spine_y), int(center_z)],
            'length': float(lower_spine_y - root_y),
            'parent_joint': None,
            'child_joint': 0
        })
        
        # Joint 0: Lower spine (hip)
        joints.append({
            'id': 0,
            'name': 'hip',
            'type': 'BALL',
            'position': [int(center_x), int(lower_spine_y), int(center_z)],
            'max_angle_x': 45.0,
            'max_angle_y': 30.0,
            'max_angle_z': 30.0
        })
        
        # Bone 1: Lower to mid spine
        bones.append({
            'id': 1,
            'name': 'lower_spine',
            'start': [int(center_x), int(lower_spine_y), int(center_z)],
            'end': [int(center_x), int(mid_spine_y), int(center_z)],
            'length': float(mid_spine_y - lower_spine_y),
            'parent_joint': 0,
            'child_joint': 1
        })
        
        # Joint 1: Mid spine
        joints.append({
            'id': 1,
            'name': 'mid_spine',
            'type': 'BALL',
            'position': [int(center_x), int(mid_spine_y), int(center_z)],
            'max_angle_x': 30.0,
            'max_angle_y': 20.0,
            'max_angle_z': 20.0
        })
        
        # Bone 2: Mid to upper spine
        bones.append({
            'id': 2,
            'name': 'upper_spine',
            'start': [int(center_x), int(mid_spine_y), int(center_z)],
            'end': [int(center_x), int(upper_spine_y), int(center_z)],
            'length': float(upper_spine_y - mid_spine_y),
            'parent_joint': 1,
            'child_joint': 2
        })
        
        # Joint 2: Upper spine (neck)
        joints.append({
            'id': 2,
            'name': 'neck',
            'type': 'BALL',
            'position': [int(center_x), int(upper_spine_y), int(center_z)],
            'max_angle_x': 60.0,
            'max_angle_y': 45.0,
            'max_angle_z': 45.0
        })
        
        # Bone 3: Upper spine to head
        bones.append({
            'id': 3,
            'name': 'head',
            'start': [int(center_x), int(upper_spine_y), int(center_z)],
            'end': [int(center_x), int(head_y), int(center_z)],
            'length': float(head_y - upper_spine_y),
            'parent_joint': 2,
            'child_joint': None
        })
        
        skeleton_data = {
            'bones': bones,
            'joints': joints,
            'influence_map': {},  # All bone voxels are 100% influenced
            'attachments': []
        }
        
        # Compute influence weights for the bone voxels
        skeleton_data['influence_map'] = SkeletonGenerator.compute_influence_weights(
            voxels, skeleton_data, radius=3
        )
        
        return voxels, skeleton_data
    
    @staticmethod
    def auto_generate_biped(voxels: np.ndarray) -> Optional[Dict]:
        """
        Generate a simple biped skeleton from voxel volume.
        Detects center spine and basic limb positions.
        
        Args:
            voxels: 3D numpy array of voxel data
            
        Returns:
            Dictionary with 'bones' and 'joints' or None if no voxels
        """
        if voxels is None:
            return None
            
        # Find all filled voxel positions
        filled_positions = np.argwhere(voxels > 0)
        if len(filled_positions) == 0:
            return None
        
        # Calculate bounds
        min_bounds = filled_positions.min(axis=0)
        max_bounds = filled_positions.max(axis=0)
        center_x = (min_bounds[0] + max_bounds[0]) // 2
        center_z = (min_bounds[2] + max_bounds[2]) // 2
        
        # Height-based segmentation
        height = max_bounds[1] - min_bounds[1]
        
        # Define skeleton segments (simple vertical spine)
        bones = []
        joints = []
        
        # Root bone (pelvis/hip area)
        root_y = min_bounds[1]
        lower_spine_y = min_bounds[1] + height // 4
        mid_spine_y = min_bounds[1] + height // 2
        upper_spine_y = min_bounds[1] + 3 * height // 4
        head_y = max_bounds[1]
        
        # Bone 0: Root to lower spine
        bones.append({
            'id': 0,
            'name': 'pelvis',
            'start': [int(center_x), int(root_y), int(center_z)],
            'end': [int(center_x), int(lower_spine_y), int(center_z)],
            'length': float(lower_spine_y - root_y),
            'parent_joint': None,
            'child_joint': 0
        })
        
        # Joint 0: Lower spine (hip)
        joints.append({
            'id': 0,
            'name': 'hip',
            'type': 'BALL',
            'position': [int(center_x), int(lower_spine_y), int(center_z)],
            'max_angle_x': 45.0,
            'max_angle_y': 30.0,
            'max_angle_z': 30.0
        })
        
        # Bone 1: Lower to mid spine
        bones.append({
            'id': 1,
            'name': 'lower_spine',
            'start': [int(center_x), int(lower_spine_y), int(center_z)],
            'end': [int(center_x), int(mid_spine_y), int(center_z)],
            'length': float(mid_spine_y - lower_spine_y),
            'parent_joint': 0,
            'child_joint': 1
        })
        
        # Joint 1: Mid spine
        joints.append({
            'id': 1,
            'name': 'mid_spine',
            'type': 'BALL',
            'position': [int(center_x), int(mid_spine_y), int(center_z)],
            'max_angle_x': 30.0,
            'max_angle_y': 20.0,
            'max_angle_z': 20.0
        })
        
        # Bone 2: Mid to upper spine
        bones.append({
            'id': 2,
            'name': 'upper_spine',
            'start': [int(center_x), int(mid_spine_y), int(center_z)],
            'end': [int(center_x), int(upper_spine_y), int(center_z)],
            'length': float(upper_spine_y - mid_spine_y),
            'parent_joint': 1,
            'child_joint': 2
        })
        
        # Joint 2: Upper spine (neck)
        joints.append({
            'id': 2,
            'name': 'neck',
            'type': 'BALL',
            'position': [int(center_x), int(upper_spine_y), int(center_z)],
            'max_angle_x': 60.0,
            'max_angle_y': 45.0,
            'max_angle_z': 45.0
        })
        
        # Bone 3: Upper spine to head
        bones.append({
            'id': 3,
            'name': 'head',
            'start': [int(center_x), int(upper_spine_y), int(center_z)],
            'end': [int(center_x), int(head_y), int(center_z)],
            'length': float(head_y - upper_spine_y),
            'parent_joint': 2,
            'child_joint': None
        })
        
        return {
            'bones': bones,
            'joints': joints,
            'influence_map': {},  # Will be computed later
            'attachments': []
        }
    
    @staticmethod
    def auto_generate_quadruped(voxels: np.ndarray) -> Optional[Dict]:
        """
        Generate a quadruped skeleton (4-legged mech).
        
        Args:
            voxels: 3D numpy array of voxel data
            
        Returns:
            Dictionary with 'bones' and 'joints' or None if no voxels
        """
        if voxels is None:
            return None
            
        filled_positions = np.argwhere(voxels > 0)
        if len(filled_positions) == 0:
            return None
        
        min_bounds = filled_positions.min(axis=0)
        max_bounds = filled_positions.max(axis=0)
        center_x = (min_bounds[0] + max_bounds[0]) // 2
        center_y = (min_bounds[1] + max_bounds[1]) // 2
        center_z = (min_bounds[2] + max_bounds[2]) // 2
        
        width = max_bounds[0] - min_bounds[0]
        height = max_bounds[1] - min_bounds[1]
        depth = max_bounds[2] - min_bounds[2]
        
        bones = []
        joints = []
        
        # Main body spine (horizontal)
        front_x = min_bounds[0] + width // 4
        back_x = min_bounds[0] + 3 * width // 4
        
        # Bone 0: Main body
        bones.append({
            'id': 0,
            'name': 'body',
            'start': [int(back_x), int(center_y), int(center_z)],
            'end': [int(front_x), int(center_y), int(center_z)],
            'length': float(back_x - front_x),
            'parent_joint': None,
            'child_joint': None
        })
        
        # Leg positions (4 corners)
        leg_offset_x = width // 4
        leg_offset_z = depth // 4
        leg_length = height // 2
        
        leg_positions = [
            ('front_left', front_x, center_z - leg_offset_z),
            ('front_right', front_x, center_z + leg_offset_z),
            ('back_left', back_x, center_z - leg_offset_z),
            ('back_right', back_x, center_z + leg_offset_z)
        ]
        
        bone_id = 1
        joint_id = 0
        
        for leg_name, leg_x, leg_z in leg_positions:
            # Hip joint
            joints.append({
                'id': joint_id,
                'name': f'{leg_name}_hip',
                'type': 'BALL',
                'position': [int(leg_x), int(center_y), int(leg_z)],
                'max_angle_x': 45.0,
                'max_angle_y': 30.0,
                'max_angle_z': 30.0
            })
            
            # Upper leg bone
            bones.append({
                'id': bone_id,
                'name': f'{leg_name}_upper',
                'start': [int(leg_x), int(center_y), int(leg_z)],
                'end': [int(leg_x), int(center_y - leg_length // 2), int(leg_z)],
                'length': float(leg_length // 2),
                'parent_joint': joint_id,
                'child_joint': joint_id + 1
            })
            bone_id += 1
            joint_id += 1
            
            # Knee joint
            joints.append({
                'id': joint_id,
                'name': f'{leg_name}_knee',
                'type': 'HINGE',
                'position': [int(leg_x), int(center_y - leg_length // 2), int(leg_z)],
                'axis': 'X',
                'min_angle': -150.0,
                'max_angle': 0.0
            })
            
            # Lower leg bone
            bones.append({
                'id': bone_id,
                'name': f'{leg_name}_lower',
                'start': [int(leg_x), int(center_y - leg_length // 2), int(leg_z)],
                'end': [int(leg_x), int(min_bounds[1]), int(leg_z)],
                'length': float(leg_length // 2),
                'parent_joint': joint_id,
                'child_joint': None
            })
            bone_id += 1
            joint_id += 1
        
        return {
            'bones': bones,
            'joints': joints,
            'influence_map': {},
            'attachments': []
        }
    
    @staticmethod
    def compute_influence_weights(voxels: np.ndarray, skeleton: Dict, radius: int = 8) -> Dict:
        """
        Auto-assign influence weights to voxels based on proximity to bones.
        
        Args:
            voxels: 3D numpy array
            skeleton: Skeleton dictionary with bones
            radius: Influence radius in voxels
            
        Returns:
            influence_map: Dict mapping voxel positions to [(bone_id, weight), ...]
        """
        influence_map = {}
        filled_positions = np.argwhere(voxels > 0)
        
        for pos in filled_positions:
            pos_tuple = tuple(pos)
            influences = []
            
            # Check distance to each bone
            for bone in skeleton['bones']:
                bone_start = np.array(bone['start'])
                bone_end = np.array(bone['end'])
                
                # Distance to bone segment (simplified: use midpoint)
                bone_mid = (bone_start + bone_end) / 2
                distance = np.linalg.norm(pos - bone_mid)
                
                if distance <= radius:
                    # Linear falloff
                    weight = 1.0 - (distance / radius)
                    influences.append((bone['id'], float(weight)))
            
            # Normalize weights
            if influences:
                total_weight = sum(w for _, w in influences)
                if total_weight > 0:
                    influences = [(bid, w / total_weight) for bid, w in influences]
                    influence_map[f"{pos[0]},{pos[1]},{pos[2]}"] = influences
        
        return influence_map
