# Steel Tide: Voxel Asset Studio
# voxel_editor.py - Main application window

from PyQt6.QtWidgets import (QMainWindow, QWidget, QHBoxLayout, QVBoxLayout,
                             QMenuBar, QStatusBar, QFileDialog, QMessageBox, QScrollArea)
from PyQt6.QtCore import QTimer, Qt
from PyQt6.QtGui import QAction
import numpy as np
import os

# For now, use pyqtgraph - it's simpler and actually works
from viewport_widget import VoxelViewport
print("✅ Using pyqtgraph viewport (fast and reliable)")
    
from tool_panel import ToolPanel
from reference_panel import ReferenceModelPanel
from settings_dialog import SettingsDialog
from mouse_config_dialog import MouseConfigDialog
from stasset_io import load_stasset, save_stasset
from shape_generator import (generate_cube, generate_sphere, generate_hollow_shell, generate_test_cube,
                             generate_armored_cube, generate_armored_sphere, generate_armored_cylinder,
                             generate_truly_hollow_sphere, generate_truly_hollow_cube,
                             generate_material_sampler, generate_simple_humanoid,
                             generate_simple_humanoid_variant)
from procedural_buildings import (generate_building_2story, generate_building_lshape,
                                  generate_building_simple_house, generate_ground_plane, create_lod_version)
from procedural_tilesets import URBAN_RESIDENTIAL_TILESET
from command_system import (CommandHistory, PaintVoxelCommand, EraseVoxelCommand, 
                            FillCommand, ReplaceVoxelsCommand)
from fill_tool import flood_fill_3d
from selection_system import SelectionBox, Clipboard
from shape_tools import bresenham_line_3d, draw_rectangle_3d
from skeleton_generator import SkeletonGenerator
from skeleton_generator_tpose import generate_tpose_biped_skeleton
from skeleton_generator_actor import generate_actor_skeleton

class VoxelEditor(QMainWindow):
    """Main application window for Voxel Asset Studio"""
    
    def __init__(self):
        super().__init__()
        
        self.setWindowTitle("Voxel Asset Studio - Steel Tide")
        self.resize(1400, 900)
        
        # Data
        self.voxels = None
        self.grid_size = (32, 32, 32)
        self.current_file = None
        self.modified = False
        
        # Generation state (for Re-Generate button)
        self.last_generator = None  # Function reference
        self.last_generator_name = None  # Display name
        self.last_seed = None  # Random seed
        
        # Settings
        self.settings = {
            'highlight_hover': True,
            'brush_size': 1
        }
        
        # Command history for undo/redo
        self.command_history = CommandHistory(max_history=50)
        
        # Selection and clipboard
        self.selection_box = SelectionBox()
        self.clipboard = Clipboard()
        
        # Shape drawing state (for line and rectangle tools)
        self.shape_start_pos = None  # First click position for line/rectangle
        self.shape_preview_target = None  # Last locked preview coordinate
        
        # Skeleton data
        self.skeleton_data = None  # Generated skeleton (bones, joints, influence map)
        self.skeleton_visible = False  # Show skeleton overlay
        
        # Build UI
        self.init_ui()
        self.init_menu()
        
        # Status bar update timer
        self.status_timer = QTimer()
        self.status_timer.timeout.connect(self.update_status)
        self.status_timer.start(100)  # Update 10x per second
        
    def init_ui(self):
        """Build the main UI"""
        central = QWidget()
        main_layout = QHBoxLayout()
        
        # Left sidebar: Tool panel + Controls panel
        left_sidebar = QWidget()
        left_layout = QVBoxLayout()
        left_layout.setContentsMargins(0, 0, 0, 0)
        
        # Tool panel
        self.tool_panel = ToolPanel()
        self.tool_panel.material_changed.connect(self.on_material_changed)
        self.tool_panel.tool_changed.connect(self.on_tool_changed)
        self.tool_panel.clear_selection.connect(self.clear_selection)
        self.tool_panel.edit_materials.connect(self.open_material_editor)
        left_layout.addWidget(self.tool_panel)
        
        left_sidebar.setLayout(left_layout)
        main_layout.addWidget(left_sidebar)
        
        # Center: 3D viewport
        self.viewport = VoxelViewport()
        self.viewport.voxel_clicked.connect(self.on_voxel_clicked)
        main_layout.addWidget(self.viewport, stretch=1)
        
        # Right sidebar: Reference models panel + Transform panel
        right_layout = QVBoxLayout()
        right_layout.setContentsMargins(0, 0, 0, 0)
        
        self.reference_panel = ReferenceModelPanel(self.viewport.reference_library)
        self.reference_panel.reference_toggled.connect(self.viewport.toggle_reference_model)
        right_layout.addWidget(self.reference_panel)
        
        from transform_panel import TransformPanel
        self.transform_panel = TransformPanel(self.viewport.reference_library)
        self.transform_panel.offset_changed.connect(self.viewport.set_model_offset)
        self.transform_panel.reset_transform.connect(lambda: self.viewport.set_model_offset(0, 0, 0))
        self.transform_panel.snap_to_reference.connect(self.on_snap_to_reference)
        right_layout.addWidget(self.transform_panel)
        
        from workspace_panel import WorkspacePanel
        self.workspace_panel = WorkspacePanel()
        self.workspace_panel.volume_resized.connect(self.on_workspace_resized)
        right_layout.addWidget(self.workspace_panel)
        
        from volume_offset_panel import VolumeOffsetPanel
        self.volume_offset_panel = VolumeOffsetPanel()
        self.volume_offset_panel.offset_changed.connect(self.on_volume_offset_changed)
        self.volume_offset_panel.reset_offset.connect(self.on_volume_offset_reset)
        right_layout.addWidget(self.volume_offset_panel)
        
        from rigging_panel import RiggingPanel
        self.rigging_panel = RiggingPanel(
            get_selection=lambda: self.selection_box,
            get_voxels=lambda: self.voxels,
            paint_voxels=self._on_rig_paint_voxels,
        )
        self.rigging_panel.skeleton_changed.connect(self._on_rig_skeleton_changed)
        right_layout.addWidget(self.rigging_panel)
        
        right_layout.addStretch()
        
        right_content = QWidget()
        right_content.setLayout(right_layout)
        
        # Wrap in scroll area to prevent cutoff in full screen
        right_scroll = QScrollArea()
        right_scroll.setWidget(right_content)
        right_scroll.setWidgetResizable(True)
        right_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        right_scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        right_scroll.setMaximumWidth(300)
        
        main_layout.addWidget(right_scroll)
        
        central.setLayout(main_layout)
        self.setCentralWidget(central)
        
        # Status bar
        self.statusBar().showMessage("Ready")
        
    def init_menu(self):
        """Create menu bar and toolbar"""
        menubar = self.menuBar()
        
        # Toolbar
        toolbar = self.addToolBar("Main Toolbar")
        toolbar.setMovable(False)
        
        # Re-Generate button
        self.regenerate_action = QAction("🔄 Re-Generate", self)
        self.regenerate_action.setToolTip("Re-generate current asset with new random seed")
        self.regenerate_action.setEnabled(False)  # Disabled until something is generated
        self.regenerate_action.triggered.connect(self.regenerate_current)
        toolbar.addAction(self.regenerate_action)
        
        # File menu
        file_menu = menubar.addMenu("File")
        
        open_action = QAction("Open...", self)
        open_action.setShortcut("Ctrl+O")
        open_action.triggered.connect(self.open_file)
        file_menu.addAction(open_action)
        
        save_action = QAction("Save", self)
        save_action.setShortcut("Ctrl+S")
        save_action.triggered.connect(self.save_file)
        file_menu.addAction(save_action)
        
        save_as_action = QAction("Save As...", self)
        save_as_action.setShortcut("Ctrl+Shift+S")
        save_as_action.triggered.connect(self.save_file_as)
        file_menu.addAction(save_as_action)
        
        file_menu.addSeparator()
        
        exit_action = QAction("Exit", self)
        exit_action.setShortcut("Ctrl+Q")
        exit_action.triggered.connect(self.close)
        file_menu.addAction(exit_action)
        
        # Edit menu
        edit_menu = menubar.addMenu("Edit")
        
        self.undo_action = QAction("Undo", self)
        self.undo_action.setShortcut("Ctrl+Z")
        self.undo_action.triggered.connect(self.undo)
        self.undo_action.setEnabled(False)
        edit_menu.addAction(self.undo_action)
        
        self.redo_action = QAction("Redo", self)
        self.redo_action.setShortcut("Ctrl+Shift+Z")
        self.redo_action.triggered.connect(self.redo)
        self.redo_action.setEnabled(False)
        edit_menu.addAction(self.redo_action)
        
        edit_menu.addSeparator()
        
        self.copy_action = QAction("Copy", self)
        self.copy_action.setShortcut("Ctrl+C")
        self.copy_action.triggered.connect(self.copy_selection)
        self.copy_action.setEnabled(False)
        edit_menu.addAction(self.copy_action)
        
        self.paste_action = QAction("Paste", self)
        self.paste_action.setShortcut("Ctrl+V")
        self.paste_action.triggered.connect(self.paste_clipboard)
        self.paste_action.setEnabled(False)
        edit_menu.addAction(self.paste_action)
        
        self.delete_action = QAction("Delete Selection", self)
        self.delete_action.setShortcut("Del")
        self.delete_action.triggered.connect(self.delete_selection)
        self.delete_action.setEnabled(False)
        edit_menu.addAction(self.delete_action)
        
        self.fill_selection_action = QAction("Fill Selection", self)
        self.fill_selection_action.setShortcut("Ctrl+F")
        self.fill_selection_action.triggered.connect(self.fill_selection_with_material)
        self.fill_selection_action.setEnabled(False)
        edit_menu.addAction(self.fill_selection_action)
        
        # View menu
        view_menu = menubar.addMenu("View")
        
        reset_camera_action = QAction("Reset Camera", self)
        reset_camera_action.setShortcut("Home")
        reset_camera_action.triggered.connect(self.reset_camera)
        view_menu.addAction(reset_camera_action)
        
        # Generate menu
        generate_menu = menubar.addMenu("Generate")
        
        test_cube_action = QAction("Test Cube (8×8×8)", self)
        test_cube_action.triggered.connect(self.generate_test_cube)
        generate_menu.addAction(test_cube_action)
        
        cube_action = QAction("Cube (32×32×32)", self)
        cube_action.triggered.connect(self.generate_full_cube)
        generate_menu.addAction(cube_action)
        
        sphere_action = QAction("Sphere (radius 15)", self)
        sphere_action.triggered.connect(self.generate_sphere)
        generate_menu.addAction(sphere_action)
        
        generate_menu.addSeparator()
        
        # Material sampler (for Unity color mapping)
        material_sampler_action = QAction("🎨 Material Sampler (All Materials Grid)", self)
        material_sampler_action.triggered.connect(self.generate_material_sampler)
        generate_menu.addAction(material_sampler_action)
        
        generate_menu.addSeparator()
        
        # Armored shapes (Steel shell + Concrete core - DENSE)
        armored_cube_action = QAction("🛡️ Armored Cube (Steel + Concrete)", self)
        armored_cube_action.triggered.connect(self.generate_armored_cube)
        generate_menu.addAction(armored_cube_action)
        
        armored_sphere_action = QAction("🛡️ Armored Sphere (Steel + Concrete)", self)
        armored_sphere_action.triggered.connect(self.generate_armored_sphere)
        generate_menu.addAction(armored_sphere_action)
        
        armored_cylinder_action = QAction("🛡️ Armored Cylinder (Steel + Concrete)", self)
        armored_cylinder_action.triggered.connect(self.generate_armored_cylinder)
        generate_menu.addAction(armored_cylinder_action)
        
        generate_menu.addSeparator()
        
        # Truly hollow shapes (Empty interior - LIGHTWEIGHT)
        hollow_sphere_action = QAction("⭕ Truly Hollow Sphere (Empty Interior)", self)
        hollow_sphere_action.triggered.connect(self.generate_truly_hollow_sphere)
        generate_menu.addAction(hollow_sphere_action)
        
        hollow_cube_action = QAction("⭕ Truly Hollow Cube (Empty Interior)", self)
        hollow_cube_action.triggered.connect(self.generate_truly_hollow_cube)
        generate_menu.addAction(hollow_cube_action)
        
        generate_menu.addSeparator()
        
        # Procedural buildings (with auto-LOD)
        building_2story_action = QAction("🏢 Building: 2-Story (32×64×32 + LOD)", self)
        building_2story_action.triggered.connect(self.generate_building_2story)
        generate_menu.addAction(building_2story_action)
        
        building_lshape_action = QAction("🏠 Building: L-Shape House (48×32×48 + LOD)", self)
        building_lshape_action.triggered.connect(self.generate_building_lshape)
        generate_menu.addAction(building_lshape_action)
        
        building_simple_action = QAction("🏘️ Building: Simple House (32×24×24 + LOD)", self)
        building_simple_action.triggered.connect(self.generate_building_simple)
        generate_menu.addAction(building_simple_action)
        
        generate_menu.addSeparator()
        
        # Terrain
        ground_plane_action = QAction("🟫 Terrain: Ground Plane (128×2×128, 16m×0.25m)", self)
        ground_plane_action.triggered.connect(self.generate_ground_plane)
        generate_menu.addAction(ground_plane_action)
        
        generate_menu.addSeparator()
        
        # Character submenu (matches Tilesets structure)
        character_menu = generate_menu.addMenu("🧍 Character Blocks")
        character_block_action = QAction("◇ Default Humanoid", self)
        character_block_action.triggered.connect(self.generate_character_block)
        character_menu.addAction(character_block_action)
        character_variant_action = QAction("◇ Test Humanoid", self)
        character_variant_action.triggered.connect(self.generate_character_block_variant)
        character_menu.addAction(character_variant_action)
        
        generate_menu.addSeparator()
        
        # Tilesets submenu
        tileset_menu = generate_menu.addMenu("🌍 Tilesets")
        
        # Urban Residential tileset
        urban_menu = tileset_menu.addMenu("Urban Residential (16m tiles)")
        for tile_name, tile_func in URBAN_RESIDENTIAL_TILESET.items():
            action = QAction(tile_name, self)
            action.triggered.connect(lambda checked, name=tile_name, func=tile_func: self.generate_tileset(name, func))
            urban_menu.addAction(action)
        
        # Skeleton menu
        skeleton_menu = menubar.addMenu("Skeleton")
        
        standalone_biped_action = QAction("🦴 Generate Standalone Biped Skeleton", self)
        standalone_biped_action.triggered.connect(self.generate_standalone_biped_skeleton)
        skeleton_menu.addAction(standalone_biped_action)
        
        actor_skeleton_action = QAction("🧍 Generate Actor Skeleton (wider bones + voxel bounds)", self)
        actor_skeleton_action.triggered.connect(self.generate_actor_skeleton)
        skeleton_menu.addAction(actor_skeleton_action)
        
        skeleton_menu.addSeparator()
        
        auto_biped_action = QAction("🔍 Auto-Detect Biped Skeleton (from existing)", self)
        auto_biped_action.triggered.connect(self.auto_generate_biped_skeleton)
        skeleton_menu.addAction(auto_biped_action)
        
        auto_quadruped_action = QAction("🦴 Auto-Generate Quadruped Skeleton", self)
        auto_quadruped_action.triggered.connect(self.auto_generate_quadruped_skeleton)
        skeleton_menu.addAction(auto_quadruped_action)
        
        skeleton_menu.addSeparator()
        
        self.toggle_skeleton_action = QAction("👁️ Show/Hide Skeleton", self)
        self.toggle_skeleton_action.setShortcut("Ctrl+K")
        self.toggle_skeleton_action.setCheckable(True)
        self.toggle_skeleton_action.setChecked(False)
        self.toggle_skeleton_action.setEnabled(False)
        self.toggle_skeleton_action.triggered.connect(self.toggle_skeleton_visibility)
        skeleton_menu.addAction(self.toggle_skeleton_action)
        
        clear_skeleton_action = QAction("Clear Skeleton", self)
        clear_skeleton_action.triggered.connect(self.clear_skeleton)
        skeleton_menu.addAction(clear_skeleton_action)

        # Edit menu — Clear All (wipe voxels + skeleton + history)
        edit_menu.addSeparator()
        clear_all_action = QAction("🧹 Clear All", self)
        clear_all_action.setShortcut("Ctrl+Shift+Delete")
        clear_all_action.triggered.connect(self.clear_all)
        edit_menu.addAction(clear_all_action)
        
        # Options menu
        options_menu = menubar.addMenu("Options")
        
        settings_action = QAction("Settings...", self)
        settings_action.triggered.connect(self.open_settings)
        options_menu.addAction(settings_action)
        
        mouse_config_action = QAction("🖱️ Mouse Controls...", self)
        mouse_config_action.triggered.connect(self.open_mouse_config)
        options_menu.addAction(mouse_config_action)
        
    def load_asset(self, filepath):
        """Load .stasset file"""
        try:
            self.voxels, self.grid_size, self.skeleton_data = load_stasset(filepath)
            self.viewport.set_voxels(self.voxels)
            self.current_file = filepath
            self.modified = False
            self.update_title()
            
            # Auto-populate workspace panel with loaded dimensions
            self.workspace_panel.set_dimensions(*self.grid_size)
            
            # Restore skeleton overlay if the asset carried a rig (v2)
            if self.skeleton_data is not None:
                self.skeleton_visible = True
                self.toggle_skeleton_action.setEnabled(True)
                self.toggle_skeleton_action.setChecked(True)
                self.viewport.set_skeleton_overlay(self.skeleton_data)
            else:
                self.skeleton_visible = False
                self.toggle_skeleton_action.setChecked(False)
                self.viewport.set_skeleton_overlay(None)
            
            self.statusBar().showMessage(f"✅ Loaded: {os.path.basename(filepath)}")
        except Exception as e:
            QMessageBox.critical(self, "Error", f"Failed to load file:\n{e}")
            print(f"❌ Error loading {filepath}: {e}")
            
    def open_file(self):
        """Open file dialog"""
        filepath, _ = QFileDialog.getOpenFileName(
            self, "Open Voxel Asset", 
            "../My project/Assets/StreamingAssets",
            "Voxel Assets (*.stasset);;All Files (*.*)"
        )
        if filepath:
            self.load_asset(filepath)
            
    def save_file(self):
        """Save to current file"""
        if self.current_file is None:
            self.save_file_as()
        else:
            self.save_to_file(self.current_file)
            
    def save_file_as(self):
        """Save as dialog"""
        filepath, _ = QFileDialog.getSaveFileName(
            self, "Save Voxel Asset",
            "../My project/Assets/StreamingAssets",
            "Voxel Assets (*.stasset);;All Files (*.*)"
        )
        if filepath:
            if not filepath.endswith('.stasset'):
                filepath += '.stasset'
            self.save_to_file(filepath)
            
    def save_to_file(self, filepath):
        """Save voxels (and skeleton rig, if any) to file"""
        try:
            save_stasset(filepath, self.voxels, self.skeleton_data)
            self.current_file = filepath
            self.modified = False
            self.update_title()
            self.statusBar().showMessage(f"✅ Saved: {os.path.basename(filepath)}")
        except Exception as e:
            QMessageBox.critical(self, "Error", f"Failed to save file:\n{e}")
            print(f"❌ Error saving {filepath}: {e}")
            
    def on_voxel_clicked(self, x, y, z):
        """Handle voxel click (paint, erase, or fill)"""
        if self.voxels is None:
            return
            
        tool = self.tool_panel.get_current_tool()
        
        if tool == "paint":
            material = self.tool_panel.get_current_material()
            # Use command pattern for undo/redo
            positions = [(x, y, z)]  # Single voxel for now (brush size handled later)
            cmd = PaintVoxelCommand(self.voxels, positions, material)
            if self.command_history.execute(cmd):
                self.viewport.update_voxel(x, y, z, material)
                self.modified = True
                self.update_title()
                self.update_undo_redo_state()
                print(f"🖌️ Painted voxel ({x}, {y}, {z}) with material {material}")
                
        elif tool == "erase":
            # Use command pattern for undo/redo
            positions = [(x, y, z)]
            cmd = EraseVoxelCommand(self.voxels, positions)
            if self.command_history.execute(cmd):
                self.viewport.update_voxel(x, y, z, 0)
                self.modified = True
                self.update_title()
                self.update_undo_redo_state()
                print(f"🗑️ Erased voxel ({x}, {y}, {z})")
                
        elif tool == "fill":
            material = self.tool_panel.get_current_material()
            # Calculate filled positions using flood fill
            filled_positions = flood_fill_3d(self.voxels, (x, y, z), material, max_fill=10000)
            
            if filled_positions:
                # Create fill command
                cmd = FillCommand(self.voxels, (x, y, z), material, filled_positions)
                if self.command_history.execute(cmd):
                    # Update viewport for all filled voxels
                    self.viewport.set_voxels(self.voxels)
                    self.modified = True
                    self.update_title()
                    self.update_undo_redo_state()
                    self.statusBar().showMessage(f"🪣 Filled {len(filled_positions)} voxels", 2000)
                    print(f"🪣 Filled {len(filled_positions)} voxels from ({x}, {y}, {z})")
            else:
                self.statusBar().showMessage("Already filled with target material", 2000)
                
        elif tool == "select":
            # Handle selection
            if not self.selection_box.dragging:
                # Start new selection
                self.selection_box.start_selection(x, y, z)
                self.viewport.set_selection(self.selection_box)
                print(f"📦 Started selection at ({x}, {y}, {z})")
            else:
                # Finish selection
                self.selection_box.update_selection(x, y, z)
                self.selection_box.finish_selection()
                self.viewport.set_selection(self.selection_box)
                
                # Update menu states
                self.update_selection_state()
                
                size = self.selection_box.get_size()
                voxel_count = len(self.selection_box.get_voxels())
                self.statusBar().showMessage(
                    f"📦 Selected {size[0]}×{size[1]}×{size[2]} ({voxel_count} voxels)", 
                    2000
                )
                print(f"📦 Finished selection: {size[0]}×{size[1]}×{size[2]} = {voxel_count} voxels")
                
        elif tool == "magic_wand":
            # Flood-fill connected voxels of the same material
            if self.voxels[x, y, z] == 0:
                self.statusBar().showMessage("🪄 Click on a solid voxel", 2000)
                return
            # Use -1 as target so flood_fill_3d always returns the connected cluster
            wand_voxels = flood_fill_3d(self.voxels, (x, y, z), -1, max_fill=50000)
            if wand_voxels:
                self.selection_box.set_voxel_list(wand_voxels)
                self.viewport.set_selection(self.selection_box)
                self.update_selection_state()
                mat_id = int(self.voxels[x, y, z])
                self.statusBar().showMessage(
                    f"🪄 Magic wand: {len(wand_voxels)} voxels (material {mat_id})", 3000
                )
                print(f"🪄 Magic wand selected {len(wand_voxels)} voxels from ({x}, {y}, {z})")
            else:
                self.statusBar().showMessage("🪄 No connected voxels found", 2000)
                
        elif tool == "line":
            # Handle line drawing
            if self.shape_start_pos is None:
                # First click - set start position
                self.shape_start_pos = (x, y, z)
                self.statusBar().showMessage(f"📏 Line start: ({x}, {y}, {z}) - Click end point", 2000)
                print(f"📏 Line started at ({x}, {y}, {z})")
            else:
                # Second click - draw line
                material = self.tool_panel.get_current_material()
                positions = bresenham_line_3d(self.shape_start_pos, (x, y, z))
                
                if positions:
                    cmd = PaintVoxelCommand(self.voxels, positions, material)
                    if self.command_history.execute(cmd):
                        self.viewport.set_voxels(self.voxels)
                        self.modified = True
                        self.update_title()
                        self.update_undo_redo_state()
                        
                        from material_library import get_material_name
                        material_name = get_material_name(material)
                        self.statusBar().showMessage(
                            f"📏 Drew line: {len(positions)} voxels with {material_name}", 
                            2000
                        )
                        print(f"📏 Drew line from {self.shape_start_pos} to ({x}, {y}, {z}): {len(positions)} voxels")
                
                # Reset for next line
                self.shape_start_pos = None
                self.shape_preview_target = None
                self.viewport.clear_shape_preview()
                
        elif tool == "rectangle":
            # Handle rectangle drawing
            if self.shape_start_pos is None:
                # First click - set start corner
                self.shape_start_pos = (x, y, z)
                self.statusBar().showMessage(f"▭ Rectangle corner 1: ({x}, {y}, {z}) - Click opposite corner", 2000)
                print(f"▭ Rectangle started at ({x}, {y}, {z})")
            else:
                # Second click - draw rectangle
                material = self.tool_panel.get_current_material()
                positions = draw_rectangle_3d(self.shape_start_pos, (x, y, z), mode='filled')
                
                if positions:
                    cmd = PaintVoxelCommand(self.voxels, positions, material)
                    if self.command_history.execute(cmd):
                        self.viewport.set_voxels(self.voxels)
                        self.modified = True
                        self.update_title()
                        self.update_undo_redo_state()
                        
                        from material_library import get_material_name
                        material_name = get_material_name(material)
                        self.statusBar().showMessage(
                            f"▭ Drew rectangle: {len(positions)} voxels with {material_name}", 
                            2000
                        )
                        print(f"▭ Drew rectangle from {self.shape_start_pos} to ({x}, {y}, {z}): {len(positions)} voxels")
                
                # Reset for next rectangle
                self.shape_start_pos = None
                self.shape_preview_target = None
                self.viewport.clear_shape_preview()
        
    def on_material_changed(self, material_id):
        """Material selection changed"""
        pass  # Already handled by tool panel
        
    def on_tool_changed(self, tool_name):
        """Tool selection changed"""
        # Clear shape preview when switching tools
        if tool_name not in ["line", "rectangle"]:
            self.shape_start_pos = None
            self.shape_preview_target = None
            if hasattr(self, 'viewport'):
                self.viewport.clear_shape_preview()
    
    def on_hover_grid_changed(self, grid_pos, modifiers=None):
        """Called when hover grid position changes (for shape preview)"""
        if self.shape_start_pos is None:
            self.viewport.clear_shape_preview()
            self.shape_preview_target = None
            return
        
        tool = self.tool_panel.get_current_tool()
        if tool not in ["line", "rectangle"]:
            self.viewport.clear_shape_preview()
            self.shape_preview_target = None
            return
        
        # Get modifier keys from Qt
        from PyQt6.QtCore import Qt
        if modifiers is None:
            from PyQt6.QtWidgets import QApplication
            modifiers = QApplication.keyboardModifiers()
        
        # When modifier is pressed, use CONSTRUCTION PLANE instead of grid_pos!
        # This allows extending into empty space beyond the model surface
        if modifiers & Qt.KeyboardModifier.ShiftModifier:
            # Shift = X-axis FREE (move along X, lock Y and Z)
            # Plane is perpendicular to X, so we can slide along X
            from PyQt6.QtGui import QCursor
            mouse_pos = self.viewport.mapFromGlobal(QCursor.pos())
            plane_pos = self.viewport.get_grid_pos_on_axis_plane(
                mouse_pos.x(), mouse_pos.y(), 
                self.shape_start_pos, 
                'x'
            )
            if plane_pos is None:
                self.viewport.clear_shape_preview()
                return
            
            # Use X from plane, lock Y and Z to start
            locked_pos = (plane_pos[0], self.shape_start_pos[1], self.shape_start_pos[2])
            distance = locked_pos[0] - self.shape_start_pos[0]
            self.statusBar().showMessage(f"X-axis: {distance:+d} voxels", 100)
        
        elif modifiers & Qt.KeyboardModifier.ControlModifier:
            # Ctrl = Y-axis FREE (move along Y, lock X and Z)
            from PyQt6.QtGui import QCursor
            mouse_pos = self.viewport.mapFromGlobal(QCursor.pos())
            plane_pos = self.viewport.get_grid_pos_on_axis_plane(
                mouse_pos.x(), mouse_pos.y(), 
                self.shape_start_pos, 
                'y'
            )
            if plane_pos is None:
                self.viewport.clear_shape_preview()
                return
            
            # Use Y from plane, lock X and Z to start
            locked_pos = (self.shape_start_pos[0], plane_pos[1], self.shape_start_pos[2])
            distance = locked_pos[1] - self.shape_start_pos[1]
            self.statusBar().showMessage(f"Y-axis: {distance:+d} voxels (up/down)", 100)
        
        elif modifiers & Qt.KeyboardModifier.AltModifier:
            # Alt = Z-axis FREE (move along Z, lock X and Y)
            from PyQt6.QtGui import QCursor
            mouse_pos = self.viewport.mapFromGlobal(QCursor.pos())
            plane_pos = self.viewport.get_grid_pos_on_axis_plane(
                mouse_pos.x(), mouse_pos.y(), 
                self.shape_start_pos, 
                'z'
            )
            if plane_pos is None:
                self.viewport.clear_shape_preview()
                return
            
            # Use Z from plane, lock X and Y to start
            locked_pos = (self.shape_start_pos[0], self.shape_start_pos[1], plane_pos[2])
            distance = locked_pos[2] - self.shape_start_pos[2]
            self.statusBar().showMessage(f"Z-axis: {distance:+d} voxels (depth)", 100)
        
        else:
            # No modifier - use grid_pos directly (free movement)
            if grid_pos is None:
                self.viewport.clear_shape_preview()
                self.shape_preview_target = None
                return
            locked_pos = grid_pos
        
        # Clamp to grid bounds
        max_x, max_y, max_z = self.grid_size
        locked_pos = (
            max(0, min(locked_pos[0], max_x - 1)),
            max(0, min(locked_pos[1], max_y - 1)),
            max(0, min(locked_pos[2], max_z - 1))
        )
        
        # Update line preview
        if tool == "line":
            preview_positions = bresenham_line_3d(self.shape_start_pos, locked_pos)
            self.viewport.show_shape_preview(preview_positions)
            self.shape_preview_target = locked_pos
        
        # Update rectangle preview
        elif tool == "rectangle":
            preview_positions = draw_rectangle_3d(self.shape_start_pos, locked_pos, mode='filled')
            self.viewport.show_shape_preview(preview_positions)
            self.shape_preview_target = locked_pos
        
    def reset_camera(self):
        """Reset camera to default position"""
        self.viewport.setCameraPosition(distance=60, elevation=30, azimuth=45)
        
    def open_settings(self):
        """Open settings dialog"""
        dialog = SettingsDialog(self, self.settings)
        if dialog.exec():
            new_settings = dialog.get_settings()
            self.settings.update(new_settings)
            self.apply_settings()
            self.statusBar().showMessage("✅ Settings updated")
    
    def open_material_editor(self):
        """Open the per-material physics property (mass/density) editor."""
        from material_editor_dialog import MaterialEditorDialog
        dialog = MaterialEditorDialog(self)
        if dialog.exec():
            self.statusBar().showMessage("\u2705 Material properties saved")

    def open_mouse_config(self):
        """Open mouse configuration dialog"""
        dialog = MouseConfigDialog(self.viewport.mouse_config, self)
        dialog.config_changed.connect(self.viewport.update_mouse_config)
        dialog.exec()
    
    def apply_settings(self):
        """Apply current settings to viewport"""
        # Apply highlight hover setting
        if hasattr(self.viewport, 'set_highlight_hover'):
            self.viewport.set_highlight_hover(self.settings['highlight_hover'])
    
    def on_brush_size_changed(self, size):
        """Handle brush size change"""
        self.settings['brush_size'] = size
        self.viewport.set_brush_size(size)
        self.statusBar().showMessage(f"✅ Brush size: {size} voxel{'s' if size > 1 else ''}")
    
    def on_snap_to_reference(self, reference_name):
        """Handle snap to reference model"""
        result = self.viewport.snap_to_reference(reference_name)
        if result:
            x, y, z = result
            self.transform_panel.set_offset(x, y, z)
            self.statusBar().showMessage(f"📍 Snapped to {reference_name} at ({x}, {y}, {z})")
        
    def generate_test_cube(self):
        """Generate a simple 8×8×8 test cube"""
        self.voxels = generate_test_cube()
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated 8×8×8 test cube")
        
    def generate_full_cube(self):
        """Generate a full 32×32×32 cube"""
        self.voxels = generate_cube((32, 32, 32), material_id=3)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated 32×32×32 cube")
        
    def generate_sphere(self):
        """Generate a sphere"""
        self.voxels = generate_sphere((32, 32, 32), radius=15, center=(16, 16, 16), material_id=3)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated sphere")
        
    def generate_shell(self):
        """Generate a hollow shell (DEPRECATED - actually two-layer, not hollow)"""
        self.voxels = generate_hollow_shell((32, 32, 32), outer_radius=15, inner_radius=12, center=(16, 16, 16), outer_material=5, inner_material=3)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("⚠️ Generated two-layer shell (NOT hollow - has concrete core)")
    
    def generate_armored_cube(self):
        """Generate armored cube (Steel shell + Concrete core)"""
        self.voxels = generate_armored_cube(size=(32, 32, 32), shell_thickness=2)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated armored cube (Steel + Concrete)")
    
    def generate_armored_sphere(self):
        """Generate armored sphere (Steel shell + Concrete core)"""
        self.voxels = generate_armored_sphere(grid_size=(32, 32, 32), radius=15, center=(16, 16, 16), shell_thickness=2)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated armored sphere (Steel + Concrete)")
    
    def generate_armored_cylinder(self):
        """Generate armored cylinder (Steel shell + Concrete core)"""
        self.voxels = generate_armored_cylinder(grid_size=(32, 32, 32), radius=12, height=28, center=(16, 16, 2), shell_thickness=2)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated armored cylinder (Steel + Concrete)")
    
    def generate_truly_hollow_sphere(self):
        """Generate truly hollow sphere (empty interior)"""
        self.voxels = generate_truly_hollow_sphere(grid_size=(32, 32, 32), outer_radius=15, shell_thickness=2, center=(16, 16, 16), material=5)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated truly hollow sphere (empty interior)")
    
    def generate_truly_hollow_cube(self):
        """Generate truly hollow cube (empty interior)"""
        self.voxels = generate_truly_hollow_cube(size=(32, 32, 32), shell_thickness=2, material=5)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated truly hollow cube (empty interior)")
    
    def generate_material_sampler(self):
        """Generate material sampler grid (all materials in clean pattern)"""
        self.voxels = generate_material_sampler()
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        self.statusBar().showMessage("✅ Generated material sampler grid (for Unity color mapping)")
    
    def generate_building_2story(self):
        """Generate 2-story building with auto-LOD"""
        import time
        start = time.time()
        
        # Generate detailed building
        self.voxels = generate_building_2story(width=32, height=64, depth=32, seed=None)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        
        # Create LOD version
        lod_grid = create_lod_version(self.voxels, lod_factor=8)
        
        elapsed = time.time() - start
        voxel_count = np.count_nonzero(self.voxels)
        lod_count = np.count_nonzero(lod_grid)
        
        self.statusBar().showMessage(
            f"✅ Generated 2-story building | "
            f"Detail: {voxel_count:,} voxels ({self.grid_size[0]}×{self.grid_size[1]}×{self.grid_size[2]}) | "
            f"LOD: {lod_count:,} voxels ({lod_grid.shape[0]}×{lod_grid.shape[1]}×{lod_grid.shape[2]}) | "
            f"Time: {elapsed:.2f}s"
        )
        
        # TODO: Save LOD version as separate file
        print(f"🏢 2-Story Building Generated:")
        print(f"   Detail Grid: {self.grid_size} = {voxel_count:,} voxels")
        print(f"   LOD Grid: {lod_grid.shape} = {lod_count:,} voxels")
        print(f"   LOD Reduction: {voxel_count/max(1,lod_count):.1f}x fewer voxels")
    
    def generate_building_lshape(self):
        """Generate L-shaped house with auto-LOD"""
        import time
        start = time.time()
        
        # Generate detailed building (48×32×48 - horizontal L, taller)
        self.voxels = generate_building_lshape(width=48, height=32, depth=48, seed=None)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        
        # Create LOD version
        lod_grid = create_lod_version(self.voxels, lod_factor=8)
        
        elapsed = time.time() - start
        voxel_count = np.count_nonzero(self.voxels)
        lod_count = np.count_nonzero(lod_grid)
        
        self.statusBar().showMessage(
            f"✅ Generated L-shape house | "
            f"Detail: {voxel_count:,} voxels | "
            f"LOD: {lod_count:,} voxels | "
            f"Time: {elapsed:.2f}s"
        )
        
        print(f"🏠 L-Shape House Generated:")
        print(f"   Detail Grid: {self.grid_size} = {voxel_count:,} voxels")
        print(f"   LOD Grid: {lod_grid.shape} = {lod_count:,} voxels")
        print(f"   LOD Reduction: {voxel_count/max(1,lod_count):.1f}x fewer voxels")
    
    def generate_building_simple(self):
        """Generate simple house with auto-LOD"""
        import time
        start = time.time()
        
        # Generate detailed building (32×24×24 - single story, taller)
        self.voxels = generate_building_simple_house(width=32, height=24, depth=24, seed=None)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        
        # Create LOD version
        lod_grid = create_lod_version(self.voxels, lod_factor=8)
        
        elapsed = time.time() - start
        voxel_count = np.count_nonzero(self.voxels)
        lod_count = np.count_nonzero(lod_grid)
        
        self.statusBar().showMessage(
            f"✅ Generated simple house | "
            f"Detail: {voxel_count:,} voxels | "
            f"LOD: {lod_count:,} voxels | "
            f"Time: {elapsed:.2f}s"
        )
        
        print(f"🏘️ Simple House Generated:")
        print(f"   Detail Grid: {self.grid_size} = {voxel_count:,} voxels")
        print(f"   LOD Grid: {lod_grid.shape} = {lod_count:,} voxels")
        print(f"   LOD Reduction: {voxel_count/max(1,lod_count):.1f}x fewer voxels")
    
    def generate_ground_plane(self):
        """Generate ground plane for streets/terrain"""
        import time
        start = time.time()
        
        # Generate ground plane (128×2×128 - 16m × 0.25m × 16m)
        self.voxels = generate_ground_plane(width=128, height=2, depth=128)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        
        elapsed = time.time() - start
        voxel_count = np.count_nonzero(self.voxels)
        
        self.statusBar().showMessage(
            f"✅ Generated ground plane | "
            f"Size: 16m × 0.25m × 16m | "
            f"Voxels: {voxel_count:,} | "
            f"Time: {elapsed:.2f}s"
        )
        
        print(f"🟫 Ground Plane Generated:")
        print(f"   Grid: {self.grid_size} = {voxel_count:,} voxels")
        print(f"   World Size: 16m × 0.25m × 16m")
        print(f"   Perfect for streets and terrain!")
    
    def generate_character_block(self):
        """Generate a stylized humanoid stand-in that preserves the armored material layout."""
        import time
        start = time.time()
        block_size = (4, 14, 4)  # Human-scale: matches reference model (0.5m × 1.8m × 0.5m)
        
        self.voxels = generate_simple_humanoid(size=block_size)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        
        elapsed = time.time() - start
        voxel_count = np.count_nonzero(self.voxels)
        
        # Calculate real-world size
        real_width = block_size[0] * 0.125  # 1 voxel = 12.5cm
        real_height = block_size[1] * 0.125
        real_depth = block_size[2] * 0.125
        
        self.statusBar().showMessage(
            f"✅ Generated default humanoid | "
            f"Voxels: {voxel_count:,} | "
            f"Size: {real_width:.2f}m × {real_height:.2f}m × {real_depth:.2f}m | "
            f"Time: {elapsed:.2f}s"
        )
        
        print(f"🧍 Default Humanoid Generated:")
        print(f"   Grid: {self.grid_size} = {voxel_count:,} voxels")
        print(f"   Size: {block_size[0]}×{block_size[1]}×{block_size[2]} voxels")
        print(f"   Real-world: {real_width:.2f}m × {real_height:.2f}m × {real_depth:.2f}m")
        print(f"   (Matches human reference: 0.5m × 1.8m × 0.5m)")
        
        # Store generation state for re-run (size is fixed, seed unused)
        self.last_generator = lambda seed=None: generate_simple_humanoid(size=block_size)
        self.last_generator_name = "Default Humanoid"
        self.last_seed = None
        self.regenerate_action.setEnabled(True)
    
    def generate_character_block_variant(self):
        """Generate the secondary humanoid variant (currently same as base for future edits)."""
        import time
        start = time.time()
        block_size = (4, 14, 4)
        self.voxels = generate_simple_humanoid_variant(size=block_size)
        self.grid_size = self.voxels.shape
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        self.modified = True
        self.update_title()
        elapsed = time.time() - start
        voxel_count = np.count_nonzero(self.voxels)
        real_width = block_size[0] * 0.125
        real_height = block_size[1] * 0.125
        real_depth = block_size[2] * 0.125
        self.statusBar().showMessage(
            f"✅ Generated test humanoid | Voxels: {voxel_count:,} | "
            f"Size: {real_width:.2f}m × {real_height:.2f}m × {real_depth:.2f}m | "
            f"Time: {elapsed:.2f}s",
            3000
        )
        print("🧍 Test Humanoid Generated")
        self.last_generator = lambda seed=None: generate_simple_humanoid_variant(size=block_size)
        self.last_generator_name = "Test Humanoid"
        self.last_seed = None
        self.regenerate_action.setEnabled(True)
    
    def update_status(self):
        """Update status bar with live info"""
        if self.voxels is None:
            return
            
        voxel_count = np.count_nonzero(self.voxels)
        total = self.voxels.size
        memory_kb = (total * 2) / 1024  # 2 bytes per voxel
        fps = self.viewport.get_fps()
        
        status = (f"Grid: {self.grid_size[0]}×{self.grid_size[1]}×{self.grid_size[2]} | "
                 f"Voxels: {voxel_count:,}/{total:,} | "
                 f"Memory: {memory_kb:.1f} KB | "
                 f"FPS: {fps}")
        
        self.statusBar().showMessage(status)
        
    def update_title(self):
        """Update window title"""
        filename = os.path.basename(self.current_file) if self.current_file else "Untitled"
        modified = "*" if self.modified else ""
        self.setWindowTitle(f"Voxel Asset Studio - {filename}{modified}")
    
    def undo(self):
        """Undo last command"""
        if self.command_history.undo():
            self.viewport.set_voxels(self.voxels)
            self.modified = True
            self.update_title()
            self.update_undo_redo_state()
            desc = self.command_history.get_undo_description()
            if desc:
                self.statusBar().showMessage(f"↩️ Undone: {desc}", 2000)
            print(f"↩️ Undo successful")
    
    def redo(self):
        """Redo next command"""
        if self.command_history.redo():
            self.viewport.set_voxels(self.voxels)
            self.modified = True
            self.update_title()
            self.update_undo_redo_state()
            desc = self.command_history.get_redo_description()
            if desc:
                self.statusBar().showMessage(f"↪️ Redone: {desc}", 2000)
            print(f"↪️ Redo successful")
    
    def update_undo_redo_state(self):
        """Update undo/redo menu item enabled state"""
        self.undo_action.setEnabled(self.command_history.can_undo())
        self.redo_action.setEnabled(self.command_history.can_redo())
    
    def update_selection_state(self):
        """Update selection-related menu item enabled state"""
        has_selection = self.selection_box.active
        has_clipboard = self.clipboard.has_data()
        
        self.copy_action.setEnabled(has_selection)
        self.delete_action.setEnabled(has_selection)
        self.fill_selection_action.setEnabled(has_selection)
        self.paste_action.setEnabled(has_clipboard)
    
    def copy_selection(self):
        """Copy selected voxels to clipboard"""
        if not self.selection_box.active:
            return
        
        if self.clipboard.copy(self.voxels, self.selection_box):
            size = self.selection_box.get_size()
            voxel_count = len(self.selection_box.get_voxels())
            self.statusBar().showMessage(f"📋 Copied {size[0]}×{size[1]}×{size[2]} ({voxel_count} voxels)", 2000)
            self.update_selection_state()
            print(f"📋 Copied {voxel_count} voxels to clipboard")
    
    def paste_clipboard(self):
        """Paste clipboard contents at selection origin or (0,0,0)"""
        if not self.clipboard.has_data():
            return
        
        # Paste at selection origin if available, otherwise at (0,0,0)
        if self.selection_box.active and self.selection_box.start_pos:
            paste_pos = self.selection_box.start_pos
        else:
            paste_pos = (0, 0, 0)
        
        modified_positions = self.clipboard.paste(self.voxels, paste_pos)
        
        if modified_positions:
            # Update viewport
            self.viewport.set_voxels(self.voxels)
            self.modified = True
            self.update_title()
            
            self.statusBar().showMessage(f"📋 Pasted {len(modified_positions)} voxels at {paste_pos}", 2000)
            print(f"📋 Pasted {len(modified_positions)} voxels at {paste_pos}")
    
    def clear_selection(self):
        """Clear current selection without deleting voxels"""
        if not self.selection_box.active:
            return
        
        self.selection_box.clear()
        self.viewport.clear_selection()
        self.update_selection_state()
        
        self.statusBar().showMessage("📦 Selection cleared", 2000)
        print("📦 Selection cleared")
    
    def delete_selection(self):
        """Delete selected voxels (set to air)"""
        if not self.selection_box.active:
            return
        
        positions = self.selection_box.get_voxels()
        if positions:
            # Use erase command for undo support
            cmd = EraseVoxelCommand(self.voxels, positions)
            if self.command_history.execute(cmd):
                self.viewport.set_voxels(self.voxels)
                self.modified = True
                self.update_title()
                self.update_undo_redo_state()
                
                # Clear selection after delete
                self.selection_box.clear()
                self.viewport.clear_selection()
                self.update_selection_state()
                
                self.statusBar().showMessage(f"🗑️ Deleted {len(positions)} voxels", 2000)
                print(f"🗑️ Deleted {len(positions)} voxels")
    
    def fill_selection_with_material(self):
        """Fill all voxels in selection with current material"""
        if not self.selection_box.active:
            return
        
        material = self.tool_panel.get_current_material()
        positions = self.selection_box.get_voxels()
        
        if positions:
            # Use paint command for undo support
            cmd = PaintVoxelCommand(self.voxels, positions, material)
            if self.command_history.execute(cmd):
                self.viewport.set_voxels(self.voxels)
                self.modified = True
                self.update_title()
                self.update_undo_redo_state()
                
                from material_library import get_material_name
                material_name = get_material_name(material)
                self.statusBar().showMessage(
                    f"🎨 Filled {len(positions)} voxels with {material_name}", 
                    2000
                )
                print(f"🎨 Filled selection with {material_name} ({len(positions)} voxels)")
        
    def keyPressEvent(self, event):
        """Handle key press events"""
        from PyQt6.QtCore import Qt
        
        # Escape key - cancel current shape drawing or clear selection
        if event.key() == Qt.Key.Key_Escape:
            if self.shape_start_pos is not None:
                self.shape_start_pos = None
                self.viewport.clear_shape_preview()
                self.statusBar().showMessage("❌ Shape drawing cancelled", 2000)
                print("❌ Shape drawing cancelled")
                event.accept()
                return
            elif self.selection_box.active:
                self.clear_selection()
                event.accept()
                return
        
        # Pass other keys to parent
        super().keyPressEvent(event)
    
    def closeEvent(self, event):
        """Handle window close"""
        if self.modified:
            reply = QMessageBox.question(
                self, "Unsaved Changes",
                "You have unsaved changes. Save before closing?",
                QMessageBox.StandardButton.Save | 
                QMessageBox.StandardButton.Discard | 
                QMessageBox.StandardButton.Cancel
            )
            
            if reply == QMessageBox.StandardButton.Save:
                self.save_file()
                event.accept()
            elif reply == QMessageBox.StandardButton.Discard:
                event.accept()
            else:
                event.ignore()
        else:
            event.accept()
    
    # ========== TILESET GENERATION ==========
    
    def generate_tileset(self, tile_name, tile_func):
        """Generate a tileset tile with random seed"""
        import time
        seed = int(time.time() * 1000) % 10000  # Random seed from timestamp
        
        print(f"🌍 Generating tileset: {tile_name} (seed: {seed})")
        
        # Generate tile
        self.voxels = tile_func(seed=seed)
        self.grid_size = self.voxels.shape
        
        # Update viewport
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        
        # Store generation state for Re-Generate
        self.last_generator = tile_func
        self.last_generator_name = tile_name
        self.last_seed = seed
        self.regenerate_action.setEnabled(True)
        
        # Mark as modified
        self.modified = True
        self.update_title()
        self.update_status()
        
        print(f"✅ Generated {tile_name}: {self.grid_size[0]}×{self.grid_size[1]}×{self.grid_size[2]} voxels")
    
    def regenerate_current(self):
        """Re-generate the current asset with a new random seed"""
        if self.last_generator is None:
            print("⚠️ No generator to re-run")
            return
        
        import time
        new_seed = int(time.time() * 1000) % 10000  # New random seed
        
        print(f"🔄 Re-generating: {self.last_generator_name} (new seed: {new_seed})")
        
        # Re-generate with new seed
        self.voxels = self.last_generator(seed=new_seed)
        self.grid_size = self.voxels.shape
        
        # Update viewport
        self.viewport.set_voxels(self.voxels)
        self.workspace_panel.set_dimensions(*self.grid_size)
        
        # Update seed
        self.last_seed = new_seed
        
        # Mark as modified
        self.modified = True
        self.update_title()
        self.update_status()
        
        print(f"✅ Re-generated {self.last_generator_name} with new variation")
    
    def on_workspace_resized(self, new_x, new_y, new_z):
        """Handle workspace volume resize"""
        old_shape = self.voxels.shape
        new_shape = (new_x, new_y, new_z)
        
        print(f"📦 Resizing workspace: {old_shape} → {new_shape}")
        
        # Create new array
        new_voxels = np.zeros(new_shape, dtype=np.uint8)
        
        # Copy existing data (preserve what fits)
        copy_x = min(old_shape[0], new_x)
        copy_y = min(old_shape[1], new_y)
        copy_z = min(old_shape[2], new_z)
        
        new_voxels[:copy_x, :copy_y, :copy_z] = self.voxels[:copy_x, :copy_y, :copy_z]
        
        # Update voxels
        self.voxels = new_voxels
        self.grid_size = new_shape
        
        # Update viewport
        self.viewport.set_voxels(self.voxels)
        
        # Update workspace panel
        self.workspace_panel.set_dimensions(new_x, new_y, new_z)
        
        # Mark as modified
        self.modified = True
        self.update_title()
        
        self.statusBar().showMessage(
            f"📦 Workspace resized to {new_x}×{new_y}×{new_z} voxels",
            3000
        )
        print(f"✅ Workspace resized successfully")
    
    def on_volume_offset_changed(self, shift_x, shift_y, shift_z):
        """Handle volume offset shift - move asset within the numpy volume"""
        if self.voxels is None:
            return
        
        if shift_x == 0 and shift_y == 0 and shift_z == 0:
            return
        
        old_shape = self.voxels.shape
        print(f"📦 Shifting volume by ({shift_x}, {shift_y}, {shift_z})")
        
        # Create new array with same shape
        new_voxels = np.zeros(old_shape, dtype=np.uint8)
        
        # Calculate source and destination slices
        # Positive shift: move data right/up/forward (copy from left/down/back)
        # Negative shift: move data left/down/back (copy from right/up/forward)
        
        src_x_start = max(0, -shift_x)
        src_x_end = min(old_shape[0], old_shape[0] - shift_x)
        dst_x_start = max(0, shift_x)
        dst_x_end = min(old_shape[0], old_shape[0] + shift_x)
        
        src_y_start = max(0, -shift_y)
        src_y_end = min(old_shape[1], old_shape[1] - shift_y)
        dst_y_start = max(0, shift_y)
        dst_y_end = min(old_shape[1], old_shape[1] + shift_y)
        
        src_z_start = max(0, -shift_z)
        src_z_end = min(old_shape[2], old_shape[2] - shift_z)
        dst_z_start = max(0, shift_z)
        dst_z_end = min(old_shape[2], old_shape[2] + shift_z)
        
        # Copy shifted data
        new_voxels[dst_x_start:dst_x_end, dst_y_start:dst_y_end, dst_z_start:dst_z_end] = \
            self.voxels[src_x_start:src_x_end, src_y_start:src_y_end, src_z_start:src_z_end]
        
        # Update voxels
        self.voxels = new_voxels
        
        # Update viewport
        self.viewport.set_voxels(self.voxels)
        
        # Mark as modified
        self.modified = True
        self.update_title()
        self.statusBar().showMessage(f"📦 Volume shifted by ({shift_x}, {shift_y}, {shift_z})")
    
    def on_volume_offset_reset(self):
        """Reset volume offset to center"""
        self.volume_offset_panel.set_offset(0, 0, 0)
        self.statusBar().showMessage("📦 Volume offset reset to center")
    
    # ==================== SKELETON SYSTEM ====================
    
    def generate_standalone_biped_skeleton(self):
        """Generate a standalone T-Pose skeleton model made entirely of bone and joint voxels"""
        print("🦴 Generating T-Pose biped skeleton model...")
        
        # Generate T-Pose skeleton voxels + metadata
        self.voxels, self.skeleton_data = generate_tpose_biped_skeleton(
            grid_size=(18, 32, 8)  # Wider for arms
        )
        self.grid_size = self.voxels.shape
        
        # Update viewport
        self.viewport.set_voxels(self.voxels)
        self.viewport.grid_size = self.grid_size
        
        # Enable skeleton visibility
        self.skeleton_visible = True
        self.toggle_skeleton_action.setEnabled(True)
        self.toggle_skeleton_action.setChecked(True)
        
        # Show skeleton overlay
        self.viewport.set_skeleton_overlay(self.skeleton_data)
        
        # Update workspace panel
        self.workspace_panel.set_dimensions(*self.grid_size)
        
        bone_count = len(self.skeleton_data['bones'])
        joint_count = len(self.skeleton_data['joints'])
        voxel_count = np.count_nonzero(self.voxels)
        
        self.statusBar().showMessage(
            f"🦴 Standalone skeleton: {bone_count} bones, {joint_count} joints, {voxel_count} bone voxels",
            5000
        )
        print(f"✅ Standalone skeleton: {self.grid_size}, {voxel_count} voxels")
        
        self.current_file = None
        self.modified = True
        self.update_title()
    
    def generate_actor_skeleton(self):
        """Generate the Actor skeleton model with wider bones, wider joints, and voxel bounds."""
        print("🧍 Generating Actor skeleton model...")

        self.voxels, self.skeleton_data = generate_actor_skeleton()
        self.grid_size = self.voxels.shape

        self.viewport.set_voxels(self.voxels)
        self.viewport.grid_size = self.grid_size

        self.skeleton_visible = True
        self.toggle_skeleton_action.setEnabled(True)
        self.toggle_skeleton_action.setChecked(True)
        self.viewport.set_skeleton_overlay(self.skeleton_data)

        self.workspace_panel.set_dimensions(*self.grid_size)

        bone_count = len(self.skeleton_data['bones'])
        joint_count = len(self.skeleton_data['joints'])
        voxel_count = np.count_nonzero(self.voxels)
        bones_with_bounds = sum(1 for b in self.skeleton_data['bones'] if 'voxel_bounds_min' in b)

        self.statusBar().showMessage(
            f"🧍 Actor skeleton: {bone_count} bones, {joint_count} joints, {voxel_count} voxels, {bones_with_bounds} with bounds",
            5000
        )
        print(f"✅ Actor skeleton: {self.grid_size}, {voxel_count} voxels, {bones_with_bounds}/{bone_count} bones with bounds")

        self.current_file = None
        self.modified = True
        self.update_title()

    def auto_generate_biped_skeleton(self):
        """Auto-generate biped skeleton from current voxel volume"""
        if self.voxels is None:
            QMessageBox.warning(self, "No Voxels", "Load or generate voxels first before creating a skeleton.")
            return
        
        print("🦴 Auto-generating biped skeleton...")
        self.skeleton_data = SkeletonGenerator.auto_generate_biped(self.voxels)
        
        if self.skeleton_data is None:
            QMessageBox.warning(self, "Generation Failed", "No voxels found to generate skeleton from.")
            return
        
        # Compute influence weights
        print("🦴 Computing influence weights...")
        self.skeleton_data['influence_map'] = SkeletonGenerator.compute_influence_weights(
            self.voxels, self.skeleton_data, radius=8
        )
        
        # Enable skeleton visibility
        self.skeleton_visible = True
        self.toggle_skeleton_action.setEnabled(True)
        self.toggle_skeleton_action.setChecked(True)
        
        # Update viewport with skeleton overlay
        self.viewport.set_skeleton_overlay(self.skeleton_data)
        
        bone_count = len(self.skeleton_data['bones'])
        joint_count = len(self.skeleton_data['joints'])
        influence_count = len(self.skeleton_data['influence_map'])
        
        self.statusBar().showMessage(
            f"🦴 Biped skeleton generated: {bone_count} bones, {joint_count} joints, {influence_count} influenced voxels",
            5000
        )
        print(f"✅ Biped skeleton: {bone_count} bones, {joint_count} joints")
        
        self.modified = True
        self.update_title()
    
    def auto_generate_quadruped_skeleton(self):
        """Auto-generate quadruped skeleton from current voxel volume"""
        if self.voxels is None:
            QMessageBox.warning(self, "No Voxels", "Load or generate voxels first before creating a skeleton.")
            return
        
        print("🦴 Auto-generating quadruped skeleton...")
        self.skeleton_data = SkeletonGenerator.auto_generate_quadruped(self.voxels)
        
        if self.skeleton_data is None:
            QMessageBox.warning(self, "Generation Failed", "No voxels found to generate skeleton from.")
            return
        
        # Compute influence weights
        print("🦴 Computing influence weights...")
        self.skeleton_data['influence_map'] = SkeletonGenerator.compute_influence_weights(
            self.voxels, self.skeleton_data, radius=8
        )
        
        # Enable skeleton visibility
        self.skeleton_visible = True
        self.toggle_skeleton_action.setEnabled(True)
        self.toggle_skeleton_action.setChecked(True)
        
        # Update viewport with skeleton overlay
        self.viewport.set_skeleton_overlay(self.skeleton_data)
        
        bone_count = len(self.skeleton_data['bones'])
        joint_count = len(self.skeleton_data['joints'])
        influence_count = len(self.skeleton_data['influence_map'])
        
        self.statusBar().showMessage(
            f"🦴 Quadruped skeleton generated: {bone_count} bones, {joint_count} joints, {influence_count} influenced voxels",
            5000
        )
        print(f"✅ Quadruped skeleton: {bone_count} bones, {joint_count} joints")
        
        self.modified = True
        self.update_title()
    
    def toggle_skeleton_visibility(self):
        """Toggle skeleton overlay visibility"""
        self.skeleton_visible = self.toggle_skeleton_action.isChecked()
        
        if self.skeleton_data is not None:
            if self.skeleton_visible:
                self.viewport.set_skeleton_overlay(self.skeleton_data)
                self.statusBar().showMessage("👁️ Skeleton visible", 2000)
            else:
                self.viewport.set_skeleton_overlay(None)
                self.statusBar().showMessage("👁️ Skeleton hidden", 2000)
    
    def _on_rig_paint_voxels(self, coords, material_id):
        """RiggingPanel painted voxels with bone/joint material — re-render the viewport."""
        self.viewport.render_voxels()
        self.viewport.update()
        self.modified = True

    def _on_rig_skeleton_changed(self, skeleton):
        """Live update from the Rigging panel: adopt its skeleton + refresh overlay."""
        self.skeleton_data = skeleton
        if skeleton is not None:
            self.skeleton_visible = True
            self.toggle_skeleton_action.setEnabled(True)
            self.toggle_skeleton_action.setChecked(True)
            self.viewport.set_skeleton_overlay(skeleton)
        else:
            self.viewport.set_skeleton_overlay(None)
        self.modified = True

    def clear_all(self):
        """Wipe everything: voxels, skeleton, selection, undo history, file state."""
        reply = QMessageBox.question(
            self, "Clear All",
            "Wipe all voxels, skeleton, and undo history?\nThis cannot be undone.",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No
        )
        if reply != QMessageBox.StandardButton.Yes:
            return

        self.voxels = None
        self.grid_size = (32, 32, 32)
        self.skeleton_data = None
        self.skeleton_visible = False
        self.current_file = None
        self.modified = False
        self.last_generator = None
        self.last_generator_args = {}
        self.shape_start_pos = None
        self.shape_preview_target = None

        self.selection_box.clear()
        self.clipboard.clear()
        self.command_history.clear()

        self.viewport.set_voxels(np.zeros((1, 1, 1), dtype=np.uint16))
        self.viewport.set_selection(self.selection_box)
        self.viewport.set_skeleton_overlay(None)
        self.viewport.clear_shape_preview()

        self.toggle_skeleton_action.setEnabled(False)
        self.toggle_skeleton_action.setChecked(False)
        self.regenerate_action.setEnabled(False)
        self.update_undo_redo_state()
        self.update_selection_state()
        self.update_title()
        self.statusBar().showMessage("🧹 Cleared all voxels and skeleton", 3000)
        print("🧹 Clear All: wiped voxels, skeleton, selection, and history")

    def clear_skeleton(self):
        """Clear skeleton data"""
        self.skeleton_data = None
        self.skeleton_visible = False
        self.toggle_skeleton_action.setEnabled(False)
        self.toggle_skeleton_action.setChecked(False)
        self.viewport.set_skeleton_overlay(None)
        self.statusBar().showMessage("🦴 Skeleton cleared", 2000)
        print("✅ Skeleton cleared")
    
