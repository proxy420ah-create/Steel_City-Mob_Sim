# Steel Tide: Voxel Asset Studio
# selection_system.py - Selection box and clipboard for voxel regions

from typing import List, Tuple, Optional
import numpy as np

class SelectionBox:
    """Manages rectangular or irregular selection in 3D voxel space"""
    
    def __init__(self):
        self.start_pos: Optional[Tuple[int, int, int]] = None
        self.end_pos: Optional[Tuple[int, int, int]] = None
        self.active: bool = False
        self.dragging: bool = False
        self.voxel_list: Optional[List[Tuple[int, int, int]]] = None  # For magic wand (irregular)
    
    def set_voxel_list(self, coords: List[Tuple[int, int, int]]):
        """Set an irregular selection from a list of voxel coords (magic wand)."""
        if not coords:
            self.clear()
            return
        arr = np.array(coords)
        mins = arr.min(axis=0)
        maxs = arr.max(axis=0)
        self.start_pos = (int(mins[0]), int(mins[1]), int(mins[2]))
        self.end_pos = (int(maxs[0]), int(maxs[1]), int(maxs[2]))
        self.voxel_list = list(coords)
        self.active = True
        self.dragging = False
    
    def start_selection(self, x: int, y: int, z: int):
        """Begin new selection"""
        self.start_pos = (x, y, z)
        self.end_pos = (x, y, z)
        self.active = True
        self.dragging = True
        self.voxel_list = None  # Clear irregular mode
    
    def update_selection(self, x: int, y: int, z: int):
        """Update selection endpoint while dragging"""
        if self.dragging:
            self.end_pos = (x, y, z)
    
    def finish_selection(self):
        """Finish dragging (selection remains active)"""
        self.dragging = False
    
    def clear(self):
        """Clear selection"""
        self.start_pos = None
        self.end_pos = None
        self.active = False
        self.dragging = False
        self.voxel_list = None
    
    def get_bounds(self) -> Optional[Tuple[int, int, int, int, int, int]]:
        """
        Get selection bounds as (min_x, max_x, min_y, max_y, min_z, max_z)
        Returns None if no active selection
        """
        if not self.active or self.start_pos is None or self.end_pos is None:
            return None
        
        x1, y1, z1 = self.start_pos
        x2, y2, z2 = self.end_pos
        
        return (
            min(x1, x2), max(x1, x2),
            min(y1, y2), max(y1, y2),
            min(z1, z2), max(z1, z2)
        )
    
    def get_voxels(self) -> List[Tuple[int, int, int]]:
        """
        Get list of all voxel positions in selection.
        For magic wand (irregular) selections, returns the explicit voxel list.
        For rectangular selections, returns all voxels in the bounding box.
        Returns empty list if no active selection.
        """
        if self.voxel_list is not None:
            return self.voxel_list
        
        bounds = self.get_bounds()
        if not bounds:
            return []
        
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        voxels = []
        
        for x in range(min_x, max_x + 1):
            for y in range(min_y, max_y + 1):
                for z in range(min_z, max_z + 1):
                    voxels.append((x, y, z))
        
        return voxels
    
    def get_size(self) -> Tuple[int, int, int]:
        """Get selection size (width, height, depth)"""
        bounds = self.get_bounds()
        if not bounds:
            return (0, 0, 0)
        
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        return (
            max_x - min_x + 1,
            max_y - min_y + 1,
            max_z - min_z + 1
        )
    
    def contains(self, x: int, y: int, z: int) -> bool:
        """Check if position is inside selection"""
        bounds = self.get_bounds()
        if not bounds:
            return False
        
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        return (min_x <= x <= max_x and 
                min_y <= y <= max_y and 
                min_z <= z <= max_z)


class Clipboard:
    """Manages copy/paste operations for voxel regions"""
    
    def __init__(self):
        self.voxels: Optional[np.ndarray] = None
        self.origin: Optional[Tuple[int, int, int]] = None
        self.size: Optional[Tuple[int, int, int]] = None
    
    def copy(self, voxel_data: np.ndarray, selection_box: SelectionBox):
        """Copy voxels from selection"""
        bounds = selection_box.get_bounds()
        if not bounds:
            return False
        
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        
        width = max_x - min_x + 1
        height = max_y - min_y + 1
        depth = max_z - min_z + 1
        
        # Create copy of voxel data
        self.voxels = np.zeros((width, height, depth), dtype=np.uint16)
        self.origin = (min_x, min_y, min_z)
        self.size = (width, height, depth)
        
        # Copy voxel data
        for x in range(width):
            for y in range(height):
                for z in range(depth):
                    src_x = min_x + x
                    src_y = min_y + y
                    src_z = min_z + z
                    
                    # Bounds check
                    if (0 <= src_x < voxel_data.shape[0] and
                        0 <= src_y < voxel_data.shape[1] and
                        0 <= src_z < voxel_data.shape[2]):
                        self.voxels[x, y, z] = voxel_data[src_x, src_y, src_z]
        
        return True
    
    def paste(self, voxel_data: np.ndarray, paste_pos: Tuple[int, int, int]) -> List[Tuple[int, int, int]]:
        """
        Paste voxels at position
        Returns list of modified positions
        """
        if self.voxels is None or self.size is None:
            return []
        
        px, py, pz = paste_pos
        width, height, depth = self.size
        
        modified_positions = []
        
        for x in range(width):
            for y in range(height):
                for z in range(depth):
                    target_x = px + x
                    target_y = py + y
                    target_z = pz + z
                    
                    # Bounds check
                    if (0 <= target_x < voxel_data.shape[0] and
                        0 <= target_y < voxel_data.shape[1] and
                        0 <= target_z < voxel_data.shape[2]):
                        
                        voxel_data[target_x, target_y, target_z] = self.voxels[x, y, z]
                        modified_positions.append((target_x, target_y, target_z))
        
        return modified_positions
    
    def has_data(self) -> bool:
        """Check if clipboard has data"""
        return self.voxels is not None
    
    def clear(self):
        """Clear clipboard"""
        self.voxels = None
        self.origin = None
        self.size = None
