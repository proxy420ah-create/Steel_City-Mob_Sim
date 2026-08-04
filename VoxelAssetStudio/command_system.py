# Steel Tide: Voxel Asset Studio
# command_system.py - Undo/Redo command pattern implementation

from abc import ABC, abstractmethod
from typing import List, Optional
import numpy as np
from copy import deepcopy

class Command(ABC):
    """Base class for all undoable commands"""
    
    @abstractmethod
    def execute(self) -> bool:
        """Execute the command. Returns True if successful."""
        pass
    
    @abstractmethod
    def undo(self) -> bool:
        """Undo the command. Returns True if successful."""
        pass
    
    @abstractmethod
    def get_description(self) -> str:
        """Get human-readable description for UI"""
        pass


class PaintVoxelCommand(Command):
    """Command for painting single voxel or brush stroke"""
    
    def __init__(self, voxel_data: np.ndarray, positions: List[tuple], material: int):
        """
        Args:
            voxel_data: Reference to the voxel array
            positions: List of (x, y, z) tuples to paint
            material: Material ID to paint
        """
        self.voxel_data = voxel_data
        self.positions = positions
        self.new_material = material
        
        # Store previous state for undo
        self.old_materials = {}
        for pos in positions:
            x, y, z = pos
            self.old_materials[pos] = voxel_data[x, y, z]
    
    def execute(self) -> bool:
        for pos in self.positions:
            x, y, z = pos
            self.voxel_data[x, y, z] = self.new_material
        return True
    
    def undo(self) -> bool:
        for pos in self.positions:
            x, y, z = pos
            self.voxel_data[x, y, z] = self.old_materials[pos]
        return True
    
    def get_description(self) -> str:
        if len(self.positions) == 1:
            return f"Paint voxel at {self.positions[0]}"
        return f"Paint {len(self.positions)} voxels"


class EraseVoxelCommand(Command):
    """Command for erasing voxels"""
    
    def __init__(self, voxel_data: np.ndarray, positions: List[tuple]):
        self.voxel_data = voxel_data
        self.positions = positions
        
        # Store previous state
        self.old_materials = {}
        for pos in positions:
            x, y, z = pos
            self.old_materials[pos] = voxel_data[x, y, z]
    
    def execute(self) -> bool:
        for pos in self.positions:
            x, y, z = pos
            self.voxel_data[x, y, z] = 0  # 0 = Air
        return True
    
    def undo(self) -> bool:
        for pos in self.positions:
            x, y, z = pos
            self.voxel_data[x, y, z] = self.old_materials[pos]
        return True
    
    def get_description(self) -> str:
        if len(self.positions) == 1:
            return f"Erase voxel at {self.positions[0]}"
        return f"Erase {len(self.positions)} voxels"


class FillCommand(Command):
    """Command for flood-fill operation"""
    
    def __init__(self, voxel_data: np.ndarray, start_pos: tuple, new_material: int, filled_positions: List[tuple]):
        self.voxel_data = voxel_data
        self.start_pos = start_pos
        self.new_material = new_material
        self.filled_positions = filled_positions
        
        # Store previous state
        self.old_materials = {}
        for pos in filled_positions:
            x, y, z = pos
            self.old_materials[pos] = voxel_data[x, y, z]
    
    def execute(self) -> bool:
        for pos in self.filled_positions:
            x, y, z = pos
            self.voxel_data[x, y, z] = self.new_material
        return True
    
    def undo(self) -> bool:
        for pos in self.filled_positions:
            x, y, z = pos
            self.voxel_data[x, y, z] = self.old_materials[pos]
        return True
    
    def get_description(self) -> str:
        return f"Fill {len(self.filled_positions)} voxels from {self.start_pos}"


class ReplaceVoxelsCommand(Command):
    """Command for replacing entire voxel array (used by procedural generation)"""
    
    def __init__(self, voxel_data_ref: np.ndarray, new_voxels: np.ndarray, description: str = "Replace voxels"):
        self.voxel_data = voxel_data_ref
        self.new_voxels = new_voxels.copy()
        self.old_voxels = voxel_data_ref.copy()
        self.description = description
    
    def execute(self) -> bool:
        np.copyto(self.voxel_data, self.new_voxels)
        return True
    
    def undo(self) -> bool:
        np.copyto(self.voxel_data, self.old_voxels)
        return True
    
    def get_description(self) -> str:
        return self.description


class CommandHistory:
    """Manages undo/redo history with memory limits"""
    
    def __init__(self, max_history: int = 50):
        self.max_history = max_history
        self.history: List[Command] = []
        self.current_index = -1  # Points to last executed command
    
    def execute(self, command: Command) -> bool:
        """Execute a command and add to history"""
        if command.execute():
            # Remove any commands after current index (redo branch)
            self.history = self.history[:self.current_index + 1]
            
            # Add new command
            self.history.append(command)
            self.current_index += 1
            
            # Trim history if too long
            if len(self.history) > self.max_history:
                self.history.pop(0)
                self.current_index -= 1
            
            return True
        return False
    
    def undo(self) -> bool:
        """Undo last command"""
        if self.can_undo():
            command = self.history[self.current_index]
            if command.undo():
                self.current_index -= 1
                return True
        return False
    
    def redo(self) -> bool:
        """Redo next command"""
        if self.can_redo():
            self.current_index += 1
            command = self.history[self.current_index]
            if command.execute():
                return True
            else:
                self.current_index -= 1
        return False
    
    def can_undo(self) -> bool:
        """Check if undo is available"""
        return self.current_index >= 0
    
    def can_redo(self) -> bool:
        """Check if redo is available"""
        return self.current_index < len(self.history) - 1
    
    def get_undo_description(self) -> Optional[str]:
        """Get description of command that would be undone"""
        if self.can_undo():
            return self.history[self.current_index].get_description()
        return None
    
    def get_redo_description(self) -> Optional[str]:
        """Get description of command that would be redone"""
        if self.can_redo():
            return self.history[self.current_index + 1].get_description()
        return None
    
    def clear(self):
        """Clear all history"""
        self.history.clear()
        self.current_index = -1
    
    def get_memory_usage(self) -> int:
        """Estimate memory usage in bytes (rough approximation)"""
        # Each command stores voxel positions + materials
        # Rough estimate: 100 bytes per voxel position
        total = 0
        for cmd in self.history:
            if hasattr(cmd, 'positions'):
                total += len(cmd.positions) * 100
            elif hasattr(cmd, 'old_voxels'):
                total += cmd.old_voxels.nbytes * 2  # old + new
        return total
