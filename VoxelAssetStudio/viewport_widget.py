# Steel Tide: Voxel Asset Studio
# viewport_widget.py - 3D OpenGL viewport for voxel rendering

from pyqtgraph.opengl import GLViewWidget, GLScatterPlotItem, GLGridItem, GLBoxItem, GLLinePlotItem
from pyqtgraph.opengl.MeshData import MeshData
from PyQt6.QtGui import QVector3D as Vec3
from PyQt6.QtCore import pyqtSignal, QTimer, Qt
from PyQt6.QtGui import QMouseEvent, QVector3D
from PyQt6.QtWidgets import QLabel, QApplication
import numpy as np
from material_library import get_material_color, get_material_name
import pyqtgraph.opengl as gl
from reference_models import ReferenceModelLibrary

class VoxelViewport(GLViewWidget):
    """3D viewport for rendering and interacting with voxels"""
    
    voxel_clicked = pyqtSignal(int, int, int)  # x, y, z coordinates
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
        # Camera setup (similar to RGVS widget!)
        self.setCameraPosition(distance=60, elevation=30, azimuth=45)
        
        # Mouse configuration (default: L=Paint, M=Pan, R=Orbit)
        self.mouse_config = {
            "orbit": "Right Button",
            "pan": "Middle Button",
            "paint": "Left Button",
            "orbit_sensitivity": 1.0,
            "pan_sensitivity": 1.0,
            "zoom_sensitivity": 1.0,
            "keyboard_sensitivity": 1.0
        }
        self.noRepeatKeys = []
        
        # Keyboard pan settings
        self.pan_speed = 0.5  # Units per frame when moving
        self.keys_pressed = set()  # Track which keys are currently pressed
        
        # Camera mode: 'orbit' (locked around center) or 'free' (move camera position)
        self.camera_mode = 'orbit'  # Default to orbit mode
        
        # Continuous movement timer (60 FPS for smooth movement)
        self.move_timer = QTimer()
        self.move_timer.timeout.connect(self._update_continuous_movement)
        self.move_timer.start(16)  # ~60 FPS
        
        # Enable keyboard focus
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        
        # Enable mouse tracking (so we get mouseMoveEvent even without buttons pressed!)
        self.setMouseTracking(True)
        
        # Add grid helper (aligned to voxel boundaries)
        grid = GLGridItem()
        grid.scale(2, 2, 1)
        grid.translate(1, 1, 0)  # Shift so grid lines align with 16-voxel boundaries
        self.addItem(grid)
        
        # Voxel data
        self.voxels = None
        self.grid_size = (32, 32, 32)
        self.voxel_size = 0.125  # World units per voxel
        self.model_offset = (0, 0, 0)  # Offset for loaded model (x, y, z in voxels)
        
        # Rendering
        self.voxel_plot = None  # Scatter plot for all voxels
        self.hover_highlight = None  # Highlight for voxel under cursor
        self.hover_glow = None  # Glowing point for hovered voxel
        
        # Reference models
        self.reference_library = ReferenceModelLibrary()
        self.reference_plots = {}  # name -> GLScatterPlotItem
        self._render_reference_models()
        
        # Voxel coordinate mapping (for mouse picking)
        self.voxel_coords = []  # List of (x,y,z) tuples matching scatter plot order
        
        # Settings
        self.highlight_hover_enabled = True  # Now works with proper ray casting!
        self.brush_size = 1
        self.hover_voxel = None  # Current voxel under cursor (solid voxels only)
        self.hover_grid_pos = None  # Grid coordinate under cursor (even in empty space)
        
        # Selection rendering
        self.selection_box_plot = None  # Wireframe box
        self.current_selection = None  # SelectionBox reference
        
        # Shape preview rendering (for line/rectangle tools)
        self.shape_preview_plot = None  # Preview voxels
        self.shape_preview_positions = []  # List of preview positions
        
        # Workspace volume wireframe (shows the editable numpy array bounds)
        self.workspace_volume_plot = None  # Wireframe showing grid_size bounds
        
        # Skeleton overlay
        self.skeleton_data = None  # Skeleton data (bones, joints)
        self.skeleton_bone_plots = []  # List of GLLinePlotItem for bones
        self.skeleton_joint_plots = []  # List of GLScatterPlotItem for joints
        
        # Debug: Add axis compass (RGB = XYZ)
        self._add_axis_compass()
        
        # FPS counter
        self.fps = 0
        self.frame_count = 0
        self.fps_timer = QTimer()
        self.fps_timer.timeout.connect(self._update_fps)
        self.fps_timer.start(1000)  # Update every second
        
        # Material label overlay (shows material name on hover)
        self.material_label = QLabel(self)
        self.material_label.setStyleSheet("""
            QLabel {
                background-color: rgba(0, 0, 0, 200);
                color: white;
                padding: 8px 12px;
                border-radius: 4px;
                font-size: 14px;
                font-weight: bold;
            }
        """)
        self.material_label.hide()  # Hidden by default

    # ========== COORDINATE SYSTEM TRANSFORMS ==========
    # Voxel data uses Y as up/height axis: [X, Y, Z] where Y=up, Z=depth
    # Viewport uses Z as up/height axis: [X, Y, Z] where Y=depth, Z=up
    # These methods centralize the coordinate transforms to avoid scattered Y/Z swaps

    def voxel_to_viewport_coords(self, x, y, z):
        """
        Convert voxel data coordinates (Y-up) to viewport coordinates (Z-up).
        Input: (x, y, z) where y=up, z=depth
        Output: (x, y, z) where y=depth, z=up
        """
        return (x, z, y)  # Swap Y and Z

    def viewport_to_voxel_coords(self, x, y, z):
        """
        Convert viewport coordinates (Z-up) to voxel data coordinates (Y-up).
        Input: (x, y, z) where y=depth, z=up
        Output: (x, y, z) where y=up, z=depth
        """
        return (x, z, y)  # Swap Y and Z

    def voxel_to_world_coords(self, x, y, z):
        """
        Convert voxel grid coordinates to world space coordinates.
        Applies Y/Z swap to match viewport's Z-up convention.
        Input: (x, y, z) voxel grid coordinates (y=up, z=depth)
        Output: (world_x, world_y, world_z) in viewport space (y=depth, z=up)
        """
        world_x = x * self.voxel_size
        world_y = z * self.voxel_size  # viewport Y = voxel Z (depth)
        world_z = y * self.voxel_size  # viewport Z = voxel Y (up)
        return (world_x, world_y, world_z)
        
    def set_voxels(self, voxels):
        """Load and render voxel data"""
        self.voxels = voxels
        self.grid_size = voxels.shape
        self.render_voxels()
        self.render_workspace_volume()  # Show the editable volume bounds
    
    def set_model_offset(self, x, y, z):
        """Set offset for loaded model position"""
        self.model_offset = (x, y, z)
        self.render_voxels()
        print(f"📐 Model offset: ({x}, {y}, {z}) voxels")
    
    def snap_to_reference(self, reference_name):
        """Snap loaded model next to a reference model"""
        model = self.reference_library.get_model_by_name(reference_name)
        if model is None:
            print(f"⚠️ Reference model '{reference_name}' not found")
            return None
        
        # Snap position: place model 10 voxels to the right of reference
        ref_x, ref_y, ref_z = model.position
        snap_x = ref_x + 10  # 10 voxels to the right
        snap_y = ref_y       # Same ground level
        snap_z = ref_z       # Same Z position
        
        self.set_model_offset(snap_x, snap_y, snap_z)
        print(f"📍 Snapped to {model.icon} {model.name} at ({snap_x}, {snap_y}, {snap_z})")
        return (snap_x, snap_y, snap_z)
        
    def render_voxels(self):
        """Render all non-air voxels as fast scatter plot"""
        if self.voxels is None:
            return
            
        # Remove old plot
        if self.voxel_plot is not None:
            self.removeItem(self.voxel_plot)
            self.voxel_plot = None
        
        # Find non-air voxels
        coords = np.argwhere(self.voxels > 0)
        
        if len(coords) == 0:
            print("⚠️ No voxels to render (all air)")
            return
        
        # Store voxel coordinates for mouse picking (CRITICAL!)
        self.voxel_coords = [tuple(c) for c in coords]
        
        # Scale coordinates to world space and apply Y/Z swap + model offset
        # Use centralized transform for consistency
        offset_x, offset_y, offset_z = self.model_offset
        positions = np.array([
            self.voxel_to_world_coords(x + offset_x, y + offset_y, z + offset_z) 
            for x, y, z in coords
        ])
        
        # For now: Use material ID to determine color intensity
        # This is a workaround - scatter plot has limited color support
        materials = np.array([self.voxels[x, y, z] for x, y, z in coords])
        
        # Map materials to colors (simplified - just use grayscale based on material ID)
        # Material 0=Air (skip), 3=Concrete, 5=Steel, 13=DamagedConcrete, etc.
        colors = np.zeros((len(materials), 4))
        for i, mat in enumerate(materials):
            color = get_material_color(mat)
            colors[i] = color
            colors[i][3] = 1.0  # Force full opacity (no transparency!)
        
        # Create scatter plot with SOLID rendering
        self.voxel_plot = GLScatterPlotItem(
            pos=positions,
            color=colors,
            size=self.voxel_size,
            pxMode=False,
            glOptions='opaque'  # Force opaque rendering!
        )
        
        # Replace the default sphere mesh with a cube mesh
        # 8 vertices of a unit cube centered at origin
        verts = np.array([
            [-0.5, -0.5, -0.5], [0.5, -0.5, -0.5], [0.5, 0.5, -0.5], [-0.5, 0.5, -0.5],  # Bottom
            [-0.5, -0.5,  0.5], [0.5, -0.5,  0.5], [0.5, 0.5,  0.5], [-0.5, 0.5,  0.5],  # Top
        ], dtype=float)
        
        # 12 triangles (2 per face, 6 faces)
        faces = np.array([
            [0,1,2], [0,2,3],  # Bottom
            [4,6,5], [4,7,6],  # Top
            [0,4,5], [0,5,1],  # Front
            [2,6,7], [2,7,3],  # Back
            [0,3,7], [0,7,4],  # Left
            [1,5,6], [1,6,2],  # Right
        ], dtype=np.uint32)
        
        # Create cube mesh and FORCE replace the scatter plot's mesh
        cube_mesh = MeshData(vertexes=verts, faces=faces)
        self.voxel_plot.setData(pos=positions, color=colors, size=self.voxel_size, pxMode=False)
        self.voxel_plot.mesh = cube_mesh
        self.voxel_plot.meshdata = cube_mesh  # Try both attributes
        
        self.addItem(self.voxel_plot)
        
        print(f"✅ Rendered {len(coords):,} voxels (fast mode)")
    
    def _render_reference_models(self):
        """Render all visible reference models in the scene"""
        # Clear existing reference plots
        for plot in self.reference_plots.values():
            self.removeItem(plot)
        self.reference_plots.clear()
        
        # Render each visible model
        for model in self.reference_library.get_visible_models():
            coords = []
            colors = []
            
            # Track first voxel position for debug output
            first_voxel_pos = None
            
            # Convert voxels to world coordinates
            for x in range(model.voxels.shape[0]):
                for y in range(model.voxels.shape[1]):
                    for z in range(model.voxels.shape[2]):
                        if model.voxels[x, y, z] != 0:  # Not air
                            # Transform to world position (position is now in voxel coordinates)
                            world_x = (model.position[0] + x) * self.voxel_size
                            world_y = (model.position[2] + z) * self.voxel_size  # Y/Z swap
                            world_z = (model.position[1] + y) * self.voxel_size  # Y/Z swap
                            
                            if first_voxel_pos is None:
                                first_voxel_pos = (world_x, world_y, world_z)
                            
                            coords.append([world_x, world_y, world_z])
                            
                            # Semi-transparent yellow tint
                            color = list(model.color_tint) + [model.opacity]
                            colors.append(color)
            
            # Debug: Print actual world position
            if first_voxel_pos:
                print(f"📍 {model.icon} {model.name}: coded={model.position}, world=({first_voxel_pos[0]:.2f}, {first_voxel_pos[1]:.2f}, {first_voxel_pos[2]:.2f})")
            
            if coords:
                coords = np.array(coords)
                colors = np.array(colors)
                
                # Create scatter plot for this reference model
                plot = GLScatterPlotItem(
                    pos=coords,
                    color=colors,
                    size=self.voxel_size,
                    pxMode=False,
                    glOptions='translucent'  # Enable transparency
                )
                
                # Use cube mesh (same as main voxels)
                verts = np.array([
                    [-0.5, -0.5, -0.5], [0.5, -0.5, -0.5], [0.5, 0.5, -0.5], [-0.5, 0.5, -0.5],
                    [-0.5, -0.5,  0.5], [0.5, -0.5,  0.5], [0.5, 0.5,  0.5], [-0.5, 0.5,  0.5],
                ], dtype=float)
                
                faces = np.array([
                    [0,1,2], [0,2,3], [4,6,5], [4,7,6],
                    [0,4,5], [0,5,1], [2,6,7], [2,7,3],
                    [0,3,7], [0,7,4], [1,5,6], [1,6,2],
                ], dtype=np.uint32)
                
                cube_mesh = MeshData(vertexes=verts, faces=faces)
                plot.setData(pos=coords, color=colors, size=self.voxel_size, pxMode=False)
                plot.mesh = cube_mesh
                plot.meshdata = cube_mesh
                
                self.addItem(plot)
                self.reference_plots[model.name] = plot
        
        if self.reference_plots:
            print(f"📏 Rendered {len(self.reference_plots)} reference models")
    
    def toggle_reference_model(self, name, visible):
        """Show/hide a specific reference model"""
        model = self.reference_library.get_model_by_name(name)
        if model:
            model.visible = visible
            self._render_reference_models()
        
    def update_voxel(self, x, y, z, material_id):
        """Update voxel(s) based on brush size"""
        if self.voxels is None:
            return
        
        # Calculate brush radius
        radius = (self.brush_size - 1) // 2
        
        # Paint all voxels in brush area
        for dx in range(-radius, radius + 1):
            for dy in range(-radius, radius + 1):
                for dz in range(-radius, radius + 1):
                    vx = x + dx
                    vy = y + dy
                    vz = z + dz
                    
                    # Check bounds
                    if (0 <= vx < self.grid_size[0] and
                        0 <= vy < self.grid_size[1] and
                        0 <= vz < self.grid_size[2]):
                        self.voxels[vx, vy, vz] = material_id
        
        # Re-render entire grid to show changes
        self.render_voxels()
        # Force viewport update
        self.update()
            
    def paintGL(self):
        """Override to count frames for FPS"""
        super().paintGL()
        self.frame_count += 1
        
    def _update_fps(self):
        """Update FPS counter"""
        self.fps = self.frame_count
        self.frame_count = 0
        
    def get_fps(self):
        """Get current FPS"""
        return self.fps
        
    def set_mouse_config(self, config):
        """Update mouse button configuration"""
        self.mouse_config = config.copy()
        print(f"🖱️ Mouse config updated: {config}")
        
    def _get_button_for_action(self, action):
        """Get which Qt button is assigned to an action"""
        button_name = self.mouse_config.get(action, "Disabled")
        button_map = {
            "Left Button": Qt.MouseButton.LeftButton,
            "Middle Button": Qt.MouseButton.MiddleButton,
            "Right Button": Qt.MouseButton.RightButton,
            "Disabled": None
        }
        return button_map.get(button_name)
        
    def mousePressEvent(self, ev):
        """Handle mouse press with configurable buttons"""
        orbit_btn = self._get_button_for_action("orbit")
        pan_btn = self._get_button_for_action("pan")
        paint_btn = self._get_button_for_action("paint")
        
        # Right-click - cancel shape drawing
        if ev.button() == Qt.MouseButton.RightButton:
            editor = self.window()
            if hasattr(editor, 'shape_start_pos') and editor.shape_start_pos is not None:
                editor.shape_start_pos = None
                if hasattr(editor, 'shape_preview_target'):
                    editor.shape_preview_target = None
                self.clear_shape_preview()
                if hasattr(editor, 'statusBar'):
                    editor.statusBar().showMessage("❌ Shape drawing cancelled", 2000)
                print("❌ Shape drawing cancelled (right-click)")
                return
        
        if ev.button() == paint_btn and paint_btn is not None:
            # Paint/interact button - pick voxel using ray casting
            mouse_pos = ev.position()
            hit_voxel = self._pick_voxel_at_mouse(mouse_pos.x(), mouse_pos.y())
            
            if hit_voxel is not None:
                # Emit signal with voxel coordinates
                self.voxel_clicked.emit(hit_voxel[0], hit_voxel[1], hit_voxel[2])
                print(f"🎯 Clicked voxel: {hit_voxel}")
            else:
                editor = self.window()
                preview_target = getattr(editor, 'shape_preview_target', None)
                if (preview_target is not None and
                        getattr(editor, 'shape_start_pos', None) is not None):
                    px, py, pz = map(int, preview_target)
                    self.voxel_clicked.emit(px, py, pz)
                    print(f"🎯 Clicked preview voxel: {(px, py, pz)}")
                else:
                    print("❌ No voxel hit")
            return
        elif ev.button() == orbit_btn and orbit_btn is not None:
            # Orbit button → convert to left-click for GLViewWidget
            fake_ev = QMouseEvent(
                ev.type(),
                ev.position(),
                Qt.MouseButton.LeftButton,
                Qt.MouseButton.LeftButton,
                ev.modifiers()
            )
            super().mousePressEvent(fake_ev)
        elif ev.button() == pan_btn and pan_btn is not None:
            # Pan button → convert to middle-click for GLViewWidget
            fake_ev = QMouseEvent(
                ev.type(),
                ev.position(),
                Qt.MouseButton.MiddleButton,
                Qt.MouseButton.MiddleButton,
                ev.modifiers()
            )
            super().mousePressEvent(fake_ev)
        else:
            # Unassigned button - ignore
            pass
            
    def mouseMoveEvent(self, ev):
        """Handle mouse drag with configurable buttons"""
        orbit_btn = self._get_button_for_action("orbit")
        pan_btn = self._get_button_for_action("pan")
        
        # ALWAYS update hover highlight on mouse move
        if self.highlight_hover_enabled:
            mouse_pos = ev.position()
            self._update_hover_highlight(mouse_pos.x(), mouse_pos.y())
        
        # Handle button-specific drag behavior
        if ev.buttons() & orbit_btn if orbit_btn else False:
            # Orbit drag → convert to left-drag
            fake_ev = QMouseEvent(
                ev.type(),
                ev.position(),
                Qt.MouseButton.LeftButton,
                Qt.MouseButton.LeftButton,
                ev.modifiers()
            )
            super().mouseMoveEvent(fake_ev)
        elif ev.buttons() & pan_btn if pan_btn else False:
            # Pan drag → convert to middle-drag
            fake_ev = QMouseEvent(
                ev.type(),
                ev.position(),
                Qt.MouseButton.MiddleButton,
                Qt.MouseButton.MiddleButton,
                ev.modifiers()
            )
            super().mouseMoveEvent(fake_ev)
        else:
            # No buttons pressed - just hovering (this is normal!)
            pass
            
    def mouseReleaseEvent(self, ev):
        """Handle mouse release with configurable buttons"""
        orbit_btn = self._get_button_for_action("orbit")
        pan_btn = self._get_button_for_action("pan")
        paint_btn = self._get_button_for_action("paint")
        
        if ev.button() == paint_btn and paint_btn is not None:
            # Paint button release - ignore
            return
        elif ev.button() == orbit_btn and orbit_btn is not None:
            # Orbit release → convert to left-release
            fake_ev = QMouseEvent(
                ev.type(),
                ev.position(),
                Qt.MouseButton.LeftButton,
                Qt.MouseButton.LeftButton,
                ev.modifiers()
            )
            super().mouseReleaseEvent(fake_ev)
        elif ev.button() == pan_btn and pan_btn is not None:
            # Pan release → convert to middle-release
            fake_ev = QMouseEvent(
                ev.type(),
                ev.position(),
                Qt.MouseButton.MiddleButton,
                Qt.MouseButton.MiddleButton,
                ev.modifiers()
            )
            super().mouseReleaseEvent(fake_ev)
        else:
            # Unassigned button - ignore
            pass
            
    def _pick_voxel_at_mouse(self, mouse_x, mouse_y):
        """
        Pick voxel at mouse position using proper ray casting.
        Based on PyQtGraph GitHub issue #2647 solution.
        Returns (x, y, z) voxel coordinates or None if no hit.
        """
        if self.voxels is None:
            return None
        
        # Get ray from mouse position (proper method!)
        ray_origin, ray_direction = self._get_ray_from_mouse(mouse_x, mouse_y)
        
        # Use DDA raymarch to find voxel intersection
        hit_voxel = self._dda_raymarch(ray_origin, ray_direction)
        
        return hit_voxel
    
    def _get_ray_from_mouse(self, x_coord, y_coord):
        """
        Get ray origin and direction from mouse click.
        Uses PyQtGraph's projection/view matrices for accurate ray casting.
        
        Returns: (ray_origin, ray_direction) as numpy arrays
        
        Based on: https://github.com/pyqtgraph/pyqtgraph/issues/2647
        """
        # Get viewport dimensions
        viewport = self.getViewport()
        x0, y0, width, height = viewport
        
        # Mouse coordinates from ev.position() are already widget-relative
        # Do NOT subtract viewport offset (that would be double-offsetting)
        mouse_x = x_coord
        mouse_y = y_coord
        
        # Ray origin is camera position
        ray_origin = np.array(self.cameraPosition())
        
        # Get projection and view matrices from PyQtGraph
        # projectionMatrix(region, viewport) - region is the area to render (use full viewport)
        region = viewport  # Use full viewport as region
        projection_matrix = np.array(self.projectionMatrix(region, viewport).data()).reshape(4, 4)
        view_matrix = np.array(self.viewMatrix().data()).reshape(4, 4)
        
        # CRITICAL: PyQtGraph's viewMatrix needs to be transposed!
        view_matrix = np.transpose(view_matrix)
        
        # Convert mouse coordinates to NDC (Normalized Device Coordinates)
        # CRITICAL: Qt Y=0 is at TOP, OpenGL NDC Y=-1 is at BOTTOM - must invert Y
        ndc_x = (2.0 * mouse_x) / width - 1.0
        ndc_y = 1.0 - (2.0 * mouse_y) / height
        
        # Create ray in clip space (near plane)
        ray_clip = np.array([ndc_x, ndc_y, -1.0, 1.0])
        
        # Unproject to eye space
        ray_eye = np.linalg.inv(projection_matrix) @ ray_clip
        # Convert to direction (w=0 for direction vector)
        ray_eye = np.array([ray_eye[0], ray_eye[1], -1.0, 0.0])
        
        # Transform to world space
        ray_world = np.linalg.inv(view_matrix) @ ray_eye
        ray_direction = ray_world[:3]
        ray_direction = ray_direction / np.linalg.norm(ray_direction)
        
        return ray_origin, ray_direction
        
    def _dda_raymarch(self, ray_origin, ray_dir, max_steps=256):
        """
        DDA (Digital Differential Analyzer) raymarching through voxel grid.
        Returns (x, y, z) of first solid voxel hit, or None.
        
        IMPORTANT: Returns voxel grid coordinates (integers), NOT world coordinates!
        """
        # Convert ray to voxel space (ray_dir stays normalized, just scale origin)
        voxel_origin = ray_origin / self.voxel_size
        
        # Current voxel position (integer grid coordinates)
        voxel_pos = np.floor(voxel_origin).astype(int)
        
        # Step direction (+1 or -1 for each axis)
        step = np.sign(ray_dir).astype(int)
        
        # Distance to next voxel boundary along each axis
        # (ray_dir is already normalized in world space, works for voxel stepping)
        t_delta = np.abs(1.0 / (ray_dir + 1e-10))  # Avoid division by zero
        
        # Distance to first voxel boundary
        if ray_dir[0] > 0:
            t_max_x = (np.floor(voxel_origin[0]) + 1 - voxel_origin[0]) * t_delta[0]
        else:
            t_max_x = (voxel_origin[0] - np.floor(voxel_origin[0])) * t_delta[0]
            
        if ray_dir[1] > 0:
            t_max_y = (np.floor(voxel_origin[1]) + 1 - voxel_origin[1]) * t_delta[1]
        else:
            t_max_y = (voxel_origin[1] - np.floor(voxel_origin[1])) * t_delta[1]
            
        if ray_dir[2] > 0:
            t_max_z = (np.floor(voxel_origin[2]) + 1 - voxel_origin[2]) * t_delta[2]
        else:
            t_max_z = (voxel_origin[2] - np.floor(voxel_origin[2])) * t_delta[2]
        
        t_max = np.array([t_max_x, t_max_y, t_max_z])
        
        # March through grid
        for _ in range(max_steps):
            # Check if current voxel is in bounds (viewport space: X, Y=depth, Z=up)
            if (0 <= voxel_pos[0] < self.grid_size[0] and
                0 <= voxel_pos[1] < self.grid_size[2] and  # viewport Y = voxel Z (depth)
                0 <= voxel_pos[2] < self.grid_size[1]):  # viewport Z = voxel Y (up)
                
                # Check if voxel is solid (non-air)
                # Use centralized transform: viewport space → voxel data space
                voxel_x, voxel_y, voxel_z = self.viewport_to_voxel_coords(*voxel_pos)
                
                material = self.voxels[voxel_x, voxel_y, voxel_z]
                if material > 0:  # Hit!
                    return (voxel_x, voxel_y, voxel_z)
            
            # Step to next voxel
            if t_max[0] < t_max[1]:
                if t_max[0] < t_max[2]:
                    voxel_pos[0] += step[0]
                    t_max[0] += t_delta[0]
                else:
                    voxel_pos[2] += step[2]
                    t_max[2] += t_delta[2]
            else:
                if t_max[1] < t_max[2]:
                    voxel_pos[1] += step[1]
                    t_max[1] += t_delta[1]
                else:
                    voxel_pos[2] += step[2]
                    t_max[2] += t_delta[2]
        
        return None  # No hit
    
    def _get_grid_pos_at_mouse(self, mouse_x, mouse_y, max_distance=50):
        """
        Get grid coordinate at mouse position, even in empty space.
        Projects ray and returns the nearest grid coordinate along the ray.
        
        Args:
            mouse_x, mouse_y: Mouse position
            max_distance: Maximum distance to project ray
        
        Returns:
            (x, y, z) grid coordinates or None if out of bounds
        """
        if self.voxels is None:
            return None
        
        # Get ray from mouse position
        ray_origin, ray_direction = self._get_ray_from_mouse(mouse_x, mouse_y)
        
        if ray_origin is None or ray_direction is None:
            return None
        
        # First, try to hit an existing voxel
        hit_voxel = self._pick_voxel_at_mouse(mouse_x, mouse_y)
        if hit_voxel is not None:
            return hit_voxel
        
        # No existing voxel hit - project ray into empty space
        # Find the closest grid point along the ray within bounds
        for t in np.linspace(0, max_distance, 200):  # Sample along ray
            point = ray_origin + t * ray_direction
            
            # Convert to voxel grid coordinates
            grid_x = int(np.round(point[0] / self.voxel_size))
            grid_y = int(np.round(point[2] / self.voxel_size))  # viewport Z = voxel Y
            grid_z = int(np.round(point[1] / self.voxel_size))  # viewport Y = voxel Z
            
            # Check if in bounds
            if (0 <= grid_x < self.grid_size[0] and
                0 <= grid_y < self.grid_size[1] and
                0 <= grid_z < self.grid_size[2]):
                return (grid_x, grid_y, grid_z)
        
        return None
    
    def _intersect_ray_with_plane(self, ray_origin, ray_direction, plane_point, plane_normal):
        """
        Intersect ray with an infinite plane.
        
        Args:
            ray_origin: Ray starting point (numpy array)
            ray_direction: Ray direction (normalized numpy array)
            plane_point: Any point on the plane (numpy array)
            plane_normal: Plane normal vector (numpy array)
        
        Returns:
            Intersection point (numpy array) or None if no intersection
        """
        # Calculate denominator (dot product of normal and ray direction)
        denom = np.dot(plane_normal, ray_direction)
        
        # Check if ray is parallel to plane (or nearly parallel)
        if abs(denom) < 1e-6:
            return None
        
        # Calculate t parameter
        t = np.dot(plane_normal, plane_point - ray_origin) / denom
        
        # Check if intersection is behind the ray origin
        if t < 0:
            return None
        
        # Calculate intersection point
        intersection = ray_origin + t * ray_direction
        return intersection
    
    def get_grid_pos_on_axis_plane(self, mouse_x, mouse_y, anchor_pos, axis):
        """Project mouse ray onto a virtual construction plane for axis-locked movement."""
        # Validate axis value early
        if axis not in ('x', 'y', 'z'):
            return None

        # Build ray from cursor into world
        ray_origin, ray_direction = self._get_ray_from_mouse(mouse_x, mouse_y)
        if ray_origin is None or ray_direction is None:
            return None

        # World translation caused by model offset (remember Y/Z swap!)
        offset_world = np.array([
            self.model_offset[0] * self.voxel_size,
            self.model_offset[2] * self.voxel_size,  # viewport Y = voxel Z
            self.model_offset[1] * self.voxel_size   # viewport Z = voxel Y
        ])

        # Anchor in world space with offset applied
        anchor_world = np.array(self.voxel_to_world_coords(*anchor_pos)) + offset_world

        # Axis direction in world space (normalized)
        axis_vectors = {
            'x': np.array([1.0, 0.0, 0.0]),
            'y': np.array([0.0, 0.0, 1.0]),  # voxel Y (up) == world Z
            'z': np.array([0.0, 1.0, 0.0])   # voxel Z (depth) == world Y
        }
        axis_dir_world = axis_vectors[axis]
        axis_dir_world = axis_dir_world / np.linalg.norm(axis_dir_world)

        # True camera view direction (from camera toward GLViewWidget center)
        camera_pos = self.cameraPosition()
        cam_vec = np.array([camera_pos.x(), camera_pos.y(), camera_pos.z()])
        center = np.array([
            self.opts['center'][0],
            self.opts['center'][1],
            self.opts['center'][2]
        ])
        view_direction = center - cam_vec
        view_norm = np.linalg.norm(view_direction)
        if view_norm < 1e-6:
            return None
        view_direction = view_direction / view_norm

        # Plane normal must contain the axis and face the camera: use cross-product
        plane_normal = np.cross(axis_dir_world, view_direction)
        plane_norm = np.linalg.norm(plane_normal)
        if plane_norm < 1e-6:
            # Camera parallel to axis – fall back to a safe up vector
            fallback = np.array([0.0, 0.0, 1.0])
            plane_normal = np.cross(axis_dir_world, fallback)
            plane_norm = np.linalg.norm(plane_normal)
            if plane_norm < 1e-6:
                # Final fallback to world Y if still degenerate
                plane_normal = np.cross(axis_dir_world, np.array([0.0, 1.0, 0.0]))
                plane_norm = np.linalg.norm(plane_normal)
                if plane_norm < 1e-6:
                    return None
        plane_normal = plane_normal / plane_norm

        # Intersect the ray with the plane anchored at the voxel
        intersection = self._intersect_ray_with_plane(
            ray_origin,
            ray_direction,
            anchor_world,
            plane_normal
        )
        if intersection is None:
            return None

        # Project the intersection onto the requested axis
        delta_world = intersection - anchor_world
        axis_delta = np.dot(delta_world, axis_dir_world)
        target_world = anchor_world + axis_dir_world * axis_delta

        # Remove model offset before converting back to grid coordinates
        relative_world = target_world - offset_world
        grid_x = int(np.round(relative_world[0] / self.voxel_size))
        grid_y = int(np.round(relative_world[2] / self.voxel_size))  # viewport Z = voxel Y
        grid_z = int(np.round(relative_world[1] / self.voxel_size))  # viewport Y = voxel Z

        # Clamp to grid bounds
        grid_x = max(0, min(grid_x, self.grid_size[0] - 1))
        grid_y = max(0, min(grid_y, self.grid_size[1] - 1))
        grid_z = max(0, min(grid_z, self.grid_size[2] - 1))

        return (grid_x, grid_y, grid_z)
        
    def set_highlight_hover(self, enabled):
        """Enable/disable hover highlighting"""
        self.highlight_hover_enabled = enabled
        if not enabled:
            self._clear_hover_highlight()
        print(f"🎨 Hover highlight: {'ON' if enabled else 'OFF'}")
    
    def update_mouse_config(self, config):
        """Update mouse configuration including sensitivity"""
        self.mouse_config.update(config)
        print(f"🖱️ Mouse config updated:")
        print(f"   Orbit: {config.get('orbit_sensitivity', 1.0):.1f}x")
        print(f"   Pan: {config.get('pan_sensitivity', 1.0):.1f}x")
        print(f"   Zoom: {config.get('zoom_sensitivity', 1.0):.1f}x")
        print(f"   Keyboard (WASD): {config.get('keyboard_sensitivity', 1.0):.1f}x")
    
    def set_brush_size(self, size):
        """Set brush size for painting"""
        self.brush_size = max(1, min(10, size))
        print(f"🖌️ Brush size: {self.brush_size}")
    
    def _update_hover_highlight(self, mouse_x, mouse_y):
        """Update hover highlight to show voxel under cursor"""
        if self.voxels is None:
            return
        
        # Pick voxel at mouse position using proper ray casting
        hit_voxel = self._pick_voxel_at_mouse(mouse_x, mouse_y)
        
        # Also get grid position (even in empty space) for shape preview
        grid_pos = self._get_grid_pos_at_mouse(mouse_x, mouse_y)
        
        # Update hover voxel (silent - use Spacebar to debug)
        if hit_voxel != self.hover_voxel:
            self.hover_voxel = hit_voxel
            self._render_hover_highlight()
        
        # Update hover grid position (for shape preview)
        editor = self.window()
        modifiers = QApplication.keyboardModifiers()

        notify_preview = grid_pos != self.hover_grid_pos
        if not notify_preview and hasattr(editor, 'shape_start_pos') and editor.shape_start_pos is not None:
            tool = getattr(editor.tool_panel, 'get_current_tool', lambda: None)()
            if tool in ["line", "rectangle"]:
                notify_preview = True

        if notify_preview:
            self.hover_grid_pos = grid_pos
            if hasattr(editor, 'on_hover_grid_changed'):
                editor.on_hover_grid_changed(grid_pos, modifiers)
    
    def _render_hover_highlight(self):
        """Render highlight box around hover voxel (snapped to voxel grid)"""
        # Remove old highlight and glow
        if self.hover_highlight is not None:
            self.removeItem(self.hover_highlight)
            self.hover_highlight = None
        if self.hover_glow is not None:
            self.removeItem(self.hover_glow)
            self.hover_glow = None
        
        if self.hover_voxel is None:
            self.material_label.hide()
            return
        
        # Get voxel grid coordinates (integers)
        voxel_x, voxel_y, voxel_z = self.hover_voxel
        
        # Calculate brush area (centered on clicked voxel)
        radius = (self.brush_size - 1) // 2
        
        # CRITICAL: Swap Y and Z to match viewport coordinate system!
        # In set_voxels (lines 100-103), the swap is:
        #   positions[:, 1] = voxel_z  (viewport Y = voxel Z)
        #   positions[:, 2] = voxel_y  (viewport Z = voxel Y)
        # We must apply the SAME transformation here
        
        # Box bounds in WORLD SPACE (converted from voxel grid coordinates)
        # Subtract 0.5 to center the wireframe on the voxel cube, matching how
        # render_voxels places cube meshes centered on the grid point.
        min_x = (voxel_x - radius - 0.5) * self.voxel_size
        max_x = (voxel_x - radius + self.brush_size - 0.5) * self.voxel_size
        min_y = (voxel_z - radius - 0.5) * self.voxel_size  # viewport Y = voxel Z (depth)
        max_y = (voxel_z - radius + self.brush_size - 0.5) * self.voxel_size
        min_z = (voxel_y - radius - 0.5) * self.voxel_size  # viewport Z = voxel Y (up)
        max_z = (voxel_y - radius + self.brush_size - 0.5) * self.voxel_size
        
        # Create wireframe box using line segments (12 edges of a cube)
        edges = np.array([
            # Bottom face (4 edges)
            [min_x, min_y, min_z], [max_x, min_y, min_z],
            [max_x, min_y, min_z], [max_x, max_y, min_z],
            [max_x, max_y, min_z], [min_x, max_y, min_z],
            [min_x, max_y, min_z], [min_x, min_y, min_z],
            # Top face (4 edges)
            [min_x, min_y, max_z], [max_x, min_y, max_z],
            [max_x, min_y, max_z], [max_x, max_y, max_z],
            [max_x, max_y, max_z], [min_x, max_y, max_z],
            [min_x, max_y, max_z], [min_x, min_y, max_z],
            # Vertical edges (4 edges)
            [min_x, min_y, min_z], [min_x, min_y, max_z],
            [max_x, min_y, min_z], [max_x, min_y, max_z],
            [max_x, max_y, min_z], [max_x, max_y, max_z],
            [min_x, max_y, min_z], [min_x, max_y, max_z],
        ])
        
        # Create line plot (yellow wireframe)
        self.hover_highlight = GLLinePlotItem(
            pos=edges,
            color=(1.0, 1.0, 0.0, 1.0),  # Bright yellow
            width=2.0,
            antialias=True,
            mode='lines'
        )
        
        # Add glowing point at voxel center
        from pyqtgraph.opengl import GLScatterPlotItem
        # Convert voxel coordinates to world position (centered on voxel)
        world_x, world_y, world_z = self.voxel_to_world_coords(voxel_x, voxel_y, voxel_z)
        glow_pos = np.array([[world_x, world_y, world_z]])
        
        self.hover_glow = GLScatterPlotItem(
            pos=glow_pos,
            color=(1.0, 1.0, 0.0, 0.8),  # Yellow with transparency
            size=self.voxel_size * 1.2,  # Slightly larger than voxel size for glow
            pxMode=False  # Size in world units, not pixels
        )
        
        self.addItem(self.hover_highlight)
        self.addItem(self.hover_glow)
        self.update()
        
        # Update material label
        if self.voxels is not None and 0 <= voxel_x < self.grid_size[0] and 0 <= voxel_y < self.grid_size[1] and 0 <= voxel_z < self.grid_size[2]:
            material_id = self.voxels[voxel_x, voxel_y, voxel_z]
            material_name = get_material_name(material_id)
            
            # Format label text
            label_text = f"Material {material_id}: {material_name}"
            if material_id == 0:
                label_text = "Air (Empty)"
            
            self.material_label.setText(label_text)
            self.material_label.show()
            
            # Position label near cursor (offset slightly)
            cursor_pos = self.mapFromGlobal(self.cursor().pos())
            self.material_label.move(cursor_pos.x() + 15, cursor_pos.y() + 15)
    
    def _clear_hover_highlight(self):
        """Clear hover highlight"""
        if self.hover_highlight is not None:
            self.removeItem(self.hover_highlight)
            self.hover_highlight = None
        if self.hover_glow is not None:
            self.removeItem(self.hover_glow)
            self.hover_glow = None
        self.hover_voxel = None
        self.material_label.hide()
        self.update()
    
    def keyPressEvent(self, ev):
        """Handle WASD camera panning with continuous movement"""
        key = ev.key()
        
        # Track key state for continuous movement
        if key in [Qt.Key.Key_W, Qt.Key.Key_S, Qt.Key.Key_A, Qt.Key.Key_D, 
                   Qt.Key.Key_Q, Qt.Key.Key_E]:
            self.keys_pressed.add(key)
        elif key == Qt.Key.Key_Space:
            # Debug: Print current hover info
            self._print_debug_info()
        elif key == Qt.Key.Key_F:
            # Toggle camera mode (orbit/free)
            self.camera_mode = 'free' if self.camera_mode == 'orbit' else 'orbit'
            print(f"🎥 Camera mode: {self.camera_mode.upper()}")
        elif key == Qt.Key.Key_Escape:
            # Ignore so Qt propagates to parent (main window handles selection clearing)
            ev.ignore()
        else:
            # Pass other keys to parent
            super().keyPressEvent(ev)
    
    def keyReleaseEvent(self, ev):
        """Handle key release for continuous movement"""
        key = ev.key()
        if key in self.keys_pressed:
            self.keys_pressed.remove(key)
        else:
            super().keyReleaseEvent(ev)
    
    def _update_continuous_movement(self):
        """Update camera position continuously based on pressed keys"""
        if not self.keys_pressed:
            return
        
        # Get camera center
        center_vec = self.opts['center']
        center = np.array([center_vec.x(), center_vec.y(), center_vec.z()])
        
        # Get view matrix and extract camera direction vectors
        view_matrix = np.array(self.viewMatrix().data()).reshape(4, 4)
        view_matrix = np.transpose(view_matrix)  # PyQtGraph matrices are transposed
        
        # Extract direction vectors from view matrix (column-major in OpenGL)
        # View matrix columns: [right, up, -forward, translation]
        right = -view_matrix[0, :3]  # First column (negated for correct direction)
        up = view_matrix[1, :3]      # Second column
        forward = -view_matrix[2, :3] # Third column (negated because view looks down -Z)
        
        # Normalize vectors
        right_norm = np.linalg.norm(right)
        if right_norm > 0:
            right = right / right_norm
        
        up_norm = np.linalg.norm(up)
        if up_norm > 0:
            up = up / up_norm
        
        forward_norm = np.linalg.norm(forward)
        if forward_norm > 0:
            forward = forward / forward_norm
        
        # Calculate movement vector based on pressed keys
        movement = np.array([0.0, 0.0, 0.0])
        
        if Qt.Key.Key_W in self.keys_pressed:
            movement += forward  # Move forward
        if Qt.Key.Key_S in self.keys_pressed:
            movement -= forward  # Move backward
        if Qt.Key.Key_A in self.keys_pressed:
            movement += right  # Strafe left (fixed direction)
        if Qt.Key.Key_D in self.keys_pressed:
            movement -= right  # Strafe right (fixed direction)
        if Qt.Key.Key_Q in self.keys_pressed:
            movement -= up  # Move down (camera-relative)
        if Qt.Key.Key_E in self.keys_pressed:
            movement += up  # Move up (camera-relative)
        
        # Normalize diagonal movement and apply keyboard sensitivity
        if np.linalg.norm(movement) > 0:
            keyboard_mult = self.mouse_config.get('keyboard_sensitivity', 1.0)
            movement = movement / np.linalg.norm(movement) * self.pan_speed * keyboard_mult
        
        # Apply movement based on camera mode
        # For now, both modes move the center point
        # Future: free look mode could have different mouse orbit behavior
        new_center = center + movement
        self.opts['center'] = QVector3D(*new_center)
        
        self.update()
    
    def _print_debug_info(self):
        """Print detailed debug info when Spacebar is pressed"""
        if self.hover_voxel is None:
            print("🔍 DEBUG: No voxel under cursor")
            return
        
        # Get current mouse position (from last hover event)
        cursor_pos = self.mapFromGlobal(self.cursor().pos())
        mouse_x, mouse_y = cursor_pos.x(), cursor_pos.y()
        
        # Get viewport info
        viewport = self.getViewport()
        x0, y0, width, height = viewport
        
        # Adjusted mouse coords
        adj_x = mouse_x - x0
        adj_y = mouse_y - y0
        
        # Camera info
        cam_pos = self.cameraPosition()
        
        print("\n" + "="*60)
        print("🔍 VOXEL PICKING DEBUG INFO")
        print("="*60)
        print(f"📍 Voxel Grid Coords: {self.hover_voxel}")
        print(f"🖱️  Mouse Screen Pos: ({mouse_x:.0f}, {mouse_y:.0f})")
        print(f"📐 Viewport: offset=({x0}, {y0}), size=({width}×{height})")
        print(f"🎯 Adjusted Mouse: ({adj_x:.0f}, {adj_y:.0f})")
        print(f"📷 Camera: pos={cam_pos}, dist={self.opts['distance']:.1f}")
        print(f"🌍 World Coords: voxel_size={self.voxel_size}")
        
        # Calculate world position of voxel using centralized transform
        world_x, world_y, world_z = self.voxel_to_world_coords(*self.hover_voxel)
        print(f"📦 Voxel World Pos: ({world_x:.3f}, {world_y:.3f}, {world_z:.3f})")
        print("="*60 + "\n")
    
    def _add_axis_compass(self):
        """Add RGB axis lines at origin for debugging coordinate system"""
        axis_length = 10.0
        
        # X axis - RED
        x_axis = np.array([[0, 0, 0], [axis_length, 0, 0]])
        x_line = GLLinePlotItem(pos=x_axis, color=(1, 0, 0, 1), width=3, antialias=True)
        self.addItem(x_line)
        
        # Y axis - GREEN
        y_axis = np.array([[0, 0, 0], [0, axis_length, 0]])
        y_line = GLLinePlotItem(pos=y_axis, color=(0, 1, 0, 1), width=3, antialias=True)
        self.addItem(y_line)
        
        # Z axis - BLUE
        z_axis = np.array([[0, 0, 0], [0, 0, axis_length]])
        z_line = GLLinePlotItem(pos=z_axis, color=(0, 0, 1, 1), width=3, antialias=True)
        self.addItem(z_line)
    
    # ========== SELECTION RENDERING ==========
    
    def __init_selection_rendering(self):
        """Initialize selection rendering (called in __init__)"""
        self.selection_box_plot = None  # Wireframe box
        self.selection_highlight_plot = None  # Highlighted voxels
        self.current_selection = None  # SelectionBox reference
    
    def set_selection(self, selection_box):
        """Update selection rendering"""
        self.current_selection = selection_box
        self._render_selection()
    
    def clear_selection(self):
        """Clear selection rendering"""
        self.current_selection = None
        self._render_selection()
    
    # ========== WORKSPACE VOLUME WIREFRAME ==========
    
    def render_workspace_volume(self):
        """Render wireframe showing the editable numpy array bounds"""
        # Remove old wireframe
        if self.workspace_volume_plot is not None:
            self.removeItem(self.workspace_volume_plot)
            self.workspace_volume_plot = None
        
        if self.grid_size is None:
            return
        
        # Get grid dimensions
        max_x, max_y, max_z = self.grid_size
        
        # Convert to world coordinates (full volume from 0 to max)
        # Use -0.5 and max+0.5 to show the outer edges of the voxel grid
        wx1, wy1, wz1 = self.voxel_to_world_coords(-0.5, -0.5, -0.5)
        wx2, wy2, wz2 = self.voxel_to_world_coords(max_x - 0.5, max_y - 0.5, max_z - 0.5)
        
        # Create 12 edges of the box
        edges = []
        
        # Bottom face (4 edges)
        edges.append([[wx1, wy1, wz1], [wx2, wy1, wz1]])
        edges.append([[wx2, wy1, wz1], [wx2, wy2, wz1]])
        edges.append([[wx2, wy2, wz1], [wx1, wy2, wz1]])
        edges.append([[wx1, wy2, wz1], [wx1, wy1, wz1]])
        
        # Top face (4 edges)
        edges.append([[wx1, wy1, wz2], [wx2, wy1, wz2]])
        edges.append([[wx2, wy1, wz2], [wx2, wy2, wz2]])
        edges.append([[wx2, wy2, wz2], [wx1, wy2, wz2]])
        edges.append([[wx1, wy2, wz2], [wx1, wy1, wz2]])
        
        # Vertical edges (4 edges)
        edges.append([[wx1, wy1, wz1], [wx1, wy1, wz2]])
        edges.append([[wx2, wy1, wz1], [wx2, wy1, wz2]])
        edges.append([[wx2, wy2, wz1], [wx2, wy2, wz2]])
        edges.append([[wx1, wy2, wz1], [wx1, wy2, wz2]])
        
        # Combine all edges
        all_points = []
        for edge in edges:
            all_points.extend(edge)
        
        points = np.array(all_points)
        
        # Create line plot (cyan/blue, semi-transparent, thinner than selection)
        self.workspace_volume_plot = GLLinePlotItem(
            pos=points,
            color=(0.3, 0.6, 1.0, 0.5),  # Light blue, 50% opacity
            width=2,
            antialias=True,
            mode='lines'
        )
        self.addItem(self.workspace_volume_plot)
        
        print(f"📦 Workspace volume: {max_x}×{max_y}×{max_z} voxels")
    
    # ========== SELECTION RENDERING ==========
    
    def _render_selection(self):
        """Render selection wireframe box"""
        # Remove old wireframe
        if self.selection_box_plot is not None:
            self.removeItem(self.selection_box_plot)
            self.selection_box_plot = None
        
        # If no selection, we're done
        if self.current_selection is None or not self.current_selection.active:
            return
        
        bounds = self.current_selection.get_bounds()
        if not bounds:
            return
        
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        
        # Render wireframe box only (no voxel highlights)
        self._render_selection_wireframe(min_x, max_x, min_y, max_y, min_z, max_z)
    
    def _render_selection_wireframe(self, min_x, max_x, min_y, max_y, min_z, max_z):
        """Render yellow wireframe box around selection"""
        # Convert voxel bounds to world coordinates (edges of voxels)
        # Add 0.5 offset to center on voxel boundaries
        wx1, wy1, wz1 = self.voxel_to_world_coords(min_x - 0.5, min_y - 0.5, min_z - 0.5)
        wx2, wy2, wz2 = self.voxel_to_world_coords(max_x + 0.5, max_y + 0.5, max_z + 0.5)
        
        # Create 12 edges of the box
        edges = []
        
        # Bottom face (4 edges)
        edges.append([[wx1, wy1, wz1], [wx2, wy1, wz1]])  # Front
        edges.append([[wx2, wy1, wz1], [wx2, wy2, wz1]])  # Right
        edges.append([[wx2, wy2, wz1], [wx1, wy2, wz1]])  # Back
        edges.append([[wx1, wy2, wz1], [wx1, wy1, wz1]])  # Left
        
        # Top face (4 edges)
        edges.append([[wx1, wy1, wz2], [wx2, wy1, wz2]])  # Front
        edges.append([[wx2, wy1, wz2], [wx2, wy2, wz2]])  # Right
        edges.append([[wx2, wy2, wz2], [wx1, wy2, wz2]])  # Back
        edges.append([[wx1, wy2, wz2], [wx1, wy1, wz2]])  # Left
        
        # Vertical edges (4 edges)
        edges.append([[wx1, wy1, wz1], [wx1, wy1, wz2]])  # Front-left
        edges.append([[wx2, wy1, wz1], [wx2, wy1, wz2]])  # Front-right
        edges.append([[wx2, wy2, wz1], [wx2, wy2, wz2]])  # Back-right
        edges.append([[wx1, wy2, wz1], [wx1, wy2, wz2]])  # Back-left
        
        # Combine all edges into single array
        all_points = []
        for edge in edges:
            all_points.extend(edge)
        
        points = np.array(all_points)
        
        # Create line plot (yellow, thick)
        self.selection_box_plot = GLLinePlotItem(
            pos=points,
            color=(1.0, 1.0, 0.0, 1.0),  # Yellow
            width=3,
            antialias=True,
            mode='lines'
        )
        self.addItem(self.selection_box_plot)
    
    def _render_selection_highlight(self, min_x, max_x, min_y, max_y, min_z, max_z):
        """Render semi-transparent highlighted voxels in selection"""
        if self.voxels is None:
            return
        
        # Collect all voxel positions in selection
        positions = []
        colors = []
        
        for x in range(min_x, max_x + 1):
            for y in range(min_y, max_y + 1):
                for z in range(min_z, max_z + 1):
                    # Bounds check
                    if (0 <= x < self.voxels.shape[0] and
                        0 <= y < self.voxels.shape[1] and
                        0 <= z < self.voxels.shape[2]):
                        
                        # Convert to world coords
                        world_x, world_y, world_z = self.voxel_to_world_coords(x, y, z)
                        positions.append([world_x, world_y, world_z])
                        
                        # Yellow highlight with 40% opacity
                        colors.append([1.0, 1.0, 0.0, 0.4])
        
        if not positions:
            return
        
        positions = np.array(positions)
        colors = np.array(colors)
        
        # Create scatter plot for highlighted voxels
        self.selection_highlight_plot = gl.GLScatterPlotItem(
            pos=positions,
            color=colors,
            size=self.voxel_size * 8,  # Same size as regular voxels
            pxMode=False,
            glOptions='translucent'
        )
        self.addItem(self.selection_highlight_plot)
    
    # ========== SHAPE PREVIEW RENDERING ==========
    
    def show_shape_preview(self, positions):
        """Show preview of line/rectangle being drawn"""
        self.shape_preview_positions = positions
        self._render_shape_preview()
    
    def clear_shape_preview(self):
        """Clear shape preview"""
        self.shape_preview_positions = []
        self._render_shape_preview()
        editor = self.window()
        if hasattr(editor, 'shape_preview_target'):
            editor.shape_preview_target = None
    
    def _render_shape_preview(self):
        """Render semi-transparent preview of shape"""
        # Remove old preview
        if self.shape_preview_plot is not None:
            self.removeItem(self.shape_preview_plot)
            self.shape_preview_plot = None
        
        if not self.shape_preview_positions:
            return
        
        # Collect preview voxel positions
        positions = []
        colors = []
        
        for x, y, z in self.shape_preview_positions:
            # Bounds check
            if (0 <= x < self.grid_size[0] and
                0 <= y < self.grid_size[1] and
                0 <= z < self.grid_size[2]):
                
                # Convert to world coords
                world_x, world_y, world_z = self.voxel_to_world_coords(x, y, z)
                positions.append([world_x, world_y, world_z])
                
                # Cyan preview with 60% opacity
                colors.append([0.0, 1.0, 1.0, 0.6])
        
        if not positions:
            return
        
        positions = np.array(positions)
        colors = np.array(colors)
        
        # Create scatter plot for preview voxels
        self.shape_preview_plot = gl.GLScatterPlotItem(
            pos=positions,
            color=colors,
            size=self.voxel_size * 8,
            pxMode=False,
            glOptions='translucent'
        )
        self.addItem(self.shape_preview_plot)
    
    # ========== SKELETON OVERLAY RENDERING ==========
    
    def set_skeleton_overlay(self, skeleton_data):
        """Show or hide skeleton overlay"""
        self.skeleton_data = skeleton_data
        self._render_skeleton_overlay()
    
    def _render_skeleton_overlay(self):
        """Render skeleton bones and joints as overlay"""
        # Clear existing skeleton plots
        for plot in self.skeleton_bone_plots:
            self.removeItem(plot)
        for plot in self.skeleton_joint_plots:
            self.removeItem(plot)
        
        self.skeleton_bone_plots = []
        self.skeleton_joint_plots = []
        
        if self.skeleton_data is None:
            return
        
        # Build bone name -> bone lookup for parent_bone connections
        bone_by_name = {}
        for b in self.skeleton_data['bones']:
            if 'name' in b:
                bone_by_name[b['name']] = b

        # Render bones as lines
        for bone in self.skeleton_data['bones']:
            start = bone['start']
            end = bone['end']
            
            # Convert to world coordinates
            start_world = self.voxel_to_world_coords(*start)
            end_world = self.voxel_to_world_coords(*end)
            
            # Color: yellow for RB bones, cyan for collider-only bones
            is_collider_only = bone.get('collider_only', False)
            bone_color = (0.0, 1.0, 1.0, 1.0) if is_collider_only else (1.0, 1.0, 0.0, 1.0)
            bone_width = 2.0 if is_collider_only else 3.0
            
            # Create line plot
            line_data = np.array([start_world, end_world])
            bone_plot = gl.GLLinePlotItem(
                pos=line_data,
                color=bone_color,
                width=bone_width,
                antialias=True
            )
            self.addItem(bone_plot)
            self.skeleton_bone_plots.append(bone_plot)
            
            # Draw connector line from collider-only bone to its parent bone
            if is_collider_only and bone.get('parent_bone') and bone['parent_bone'] in bone_by_name:
                parent = bone_by_name[bone['parent_bone']]
                parent_center = ((parent['start'][0] + parent['end'][0]) / 2.0,
                                 (parent['start'][1] + parent['end'][1]) / 2.0,
                                 (parent['start'][2] + parent['end'][2]) / 2.0)
                parent_world = self.voxel_to_world_coords(*parent_center)
                bone_center = ((start[0] + end[0]) / 2.0,
                               (start[1] + end[1]) / 2.0,
                               (start[2] + end[2]) / 2.0)
                bone_world = self.voxel_to_world_coords(*bone_center)
                connector = gl.GLLinePlotItem(
                    pos=np.array([parent_world, bone_world]),
                    color=(0.0, 1.0, 1.0, 0.4),  # Faint cyan dashed-like
                    width=1.5,
                    antialias=True
                )
                self.addItem(connector)
                self.skeleton_bone_plots.append(connector)
        
        # Render joints as spheres — one orb per voxel in the joint's bounds
        joint_positions = []
        joint_colors = []

        for joint in self.skeleton_data['joints']:
            # Determine color by joint type
            if joint['type'] == 'ROOT':
                color = [1.0, 0.5, 0.0, 0.3]  # Orange for root
            elif joint['type'] == 'BALL':
                color = [1.0, 0.0, 0.0, 0.3]  # Red for ball joints
            else:  # HINGE
                color = [0.0, 1.0, 0.0, 0.3]  # Green for hinge joints

            # Place an orb at every voxel in the joint's bounds
            if 'voxel_bounds_min' in joint and 'voxel_bounds_max' in joint:
                bmin = joint['voxel_bounds_min']
                bmax = joint['voxel_bounds_max']
                for x in range(bmin[0], bmax[0] + 1):
                    for y in range(bmin[1], bmax[1] + 1):
                        for z in range(bmin[2], bmax[2] + 1):
                            joint_positions.append(self.voxel_to_world_coords(x, y, z))
                            joint_colors.append(color)
            else:
                pos = joint['position']
                joint_positions.append(self.voxel_to_world_coords(*pos))
                joint_colors.append(color)
        
        if joint_positions:
            joint_positions = np.array(joint_positions)
            joint_colors = np.array(joint_colors)
            
            joint_plot = gl.GLScatterPlotItem(
                pos=joint_positions,
                color=joint_colors,
                size=self.voxel_size * 2.5,  # Smaller, clearer markers
                pxMode=False,
                glOptions='translucent'  # Semi-transparent
            )
            self.addItem(joint_plot)
            self.skeleton_joint_plots.append(joint_plot)
