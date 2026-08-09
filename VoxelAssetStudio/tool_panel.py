# Steel Tide: Voxel Asset Studio
# tool_panel.py - Left sidebar with tools and material palette

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLabel, 
                             QComboBox, QGroupBox, QButtonGroup, QRadioButton,
                             QScrollArea, QGridLayout, QFrame, QSizePolicy)
from PyQt6.QtCore import pyqtSignal, Qt
from material_library import MATERIALS, DEFAULT_MATERIALS, get_material_name, get_material_color_255

# --- Game-specific material sets ---
STEEL_CITY_CATEGORIES = [
    ("Masonry", [100, 101, 102, 103, 104, 105]),
    ("Wood", [106, 107, 108]),
    ("Metal", [109, 110, 111]),
    ("Glass", [112, 113, 114]),
    ("Neon", [115, 116, 117]),
    ("Roofing", [118, 119]),
    ("Painted", [120, 121, 122, 129]),
    ("Decorative", [123, 124]),
    ("Character", [125, 126, 127, 128]),
]

STEEL_TIDE_CATEGORIES = [
    ("Sci-Fi", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21]),
]

# Default to Steel City (Mob Sim) since that's the active project
ACTIVE_GAME = "steel_city"
MATERIAL_CATEGORIES = STEEL_CITY_CATEGORIES if ACTIVE_GAME == "steel_city" else STEEL_TIDE_CATEGORIES


class MaterialSwatch(QFrame):
    """Clickable color swatch for a material."""
    
    def __init__(self, mat_id, parent_panel):
        super().__init__()
        self.mat_id = mat_id
        self.parent_panel = parent_panel
        self.setFixedSize(22, 22)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._update_color()
        self.setToolTip(f"{get_material_name(mat_id)} ({mat_id})")
    
    def _update_color(self):
        r, g, b, a = get_material_color_255(self.mat_id)
        self.setStyleSheet(
            f"background-color: rgba({r},{g},{b},{a}); border: 1px solid #666;"
        )
    
    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.parent_panel.select_material(self.mat_id)
            self.parent_panel._highlight_selected(self.mat_id)


class ToolPanel(QWidget):
    """Left sidebar with painting tools and material palette"""
    
    material_changed = pyqtSignal(int)  # Material ID
    tool_changed = pyqtSignal(str)      # Tool name
    clear_selection = pyqtSignal()     # Clear selection
    edit_materials = pyqtSignal()      # Open the material properties editor
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
        self.current_material = 109  # Default: Dark Iron (good for fire escapes)
        self.current_tool = "paint"
        self._swatches = {}  # mat_id -> MaterialSwatch
        self._selected_border = "border: 2px solid #FFD700;"  # gold highlight
        
        self.init_ui()
        
    def init_ui(self):
        """Build the UI"""
        layout = QVBoxLayout()
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        
        # Title
        title = QLabel("🎨 Tools")
        title.setStyleSheet("font-size: 16px; font-weight: bold; padding: 10px;")
        layout.addWidget(title)
        
        # Tool selection
        tool_group = QGroupBox("Tool")
        tool_layout = QVBoxLayout()
        
        self.tool_buttons = QButtonGroup()
        
        paint_btn = QRadioButton("🖌️ Paint")
        paint_btn.setChecked(True)
        paint_btn.toggled.connect(lambda: self.set_tool("paint"))
        self.tool_buttons.addButton(paint_btn)
        tool_layout.addWidget(paint_btn)
        
        erase_btn = QRadioButton("🗑️ Erase")
        erase_btn.toggled.connect(lambda: self.set_tool("erase"))
        self.tool_buttons.addButton(erase_btn)
        tool_layout.addWidget(erase_btn)
        
        fill_btn = QRadioButton("🪣 Fill")
        fill_btn.toggled.connect(lambda: self.set_tool("fill"))
        self.tool_buttons.addButton(fill_btn)
        tool_layout.addWidget(fill_btn)
        
        select_btn = QRadioButton("📦 Select")
        select_btn.toggled.connect(lambda: self.set_tool("select"))
        self.tool_buttons.addButton(select_btn)
        tool_layout.addWidget(select_btn)
        
        wand_btn = QRadioButton("🪄 Magic Wand")
        wand_btn.setToolTip("Click a voxel to select all connected voxels of the same material")
        wand_btn.toggled.connect(lambda: self.set_tool("magic_wand"))
        self.tool_buttons.addButton(wand_btn)
        tool_layout.addWidget(wand_btn)
        
        # Clear Selection button (next to Select tool)
        clear_sel_btn = QPushButton("❌ Clear")
        clear_sel_btn.setToolTip("Clear current selection (Esc)")
        clear_sel_btn.clicked.connect(self.clear_selection.emit)
        tool_layout.addWidget(clear_sel_btn)
        
        line_btn = QRadioButton("📏 Line")
        line_btn.toggled.connect(lambda: self.set_tool("line"))
        self.tool_buttons.addButton(line_btn)
        tool_layout.addWidget(line_btn)
        
        rect_btn = QRadioButton("▭ Rectangle")
        rect_btn.toggled.connect(lambda: self.set_tool("rectangle"))
        self.tool_buttons.addButton(rect_btn)
        tool_layout.addWidget(rect_btn)
        
        tool_group.setLayout(tool_layout)
        layout.addWidget(tool_group)
        
        # Material palette
        material_group = QGroupBox("Material Palette")
        material_layout = QVBoxLayout()
        
        # Game selector toggle
        game_row = QHBoxLayout()
        game_row.addWidget(QLabel("Game:"))
        self.game_combo = QComboBox()
        self.game_combo.addItem("Steel City (Mob Sim)", "steel_city")
        self.game_combo.addItem("Steel Tide (Sci-Fi)", "steel_tide")
        self.game_combo.setCurrentIndex(0)
        self.game_combo.currentIndexChanged.connect(self._on_game_changed)
        game_row.addWidget(self.game_combo, stretch=1)
        material_layout.addLayout(game_row)

        # Current material label
        self.current_mat_label = QLabel(f"Selected: {get_material_name(self.current_material)} ({self.current_material})")
        self.current_mat_label.setStyleSheet("font-weight: bold; padding: 2px; font-size: 11px;")
        material_layout.addWidget(self.current_mat_label)
        
        # Scrollable palette grid (rebuilt on game change)
        self.scroll = QScrollArea()
        self.scroll.setWidgetResizable(True)
        self.scroll.setFixedHeight(300)
        self._palette_container = QWidget()
        self._palette_layout = QVBoxLayout(self._palette_container)
        self._palette_layout.setContentsMargins(2, 2, 2, 2)
        self._palette_layout.setSpacing(4)
        self._build_palette()
        self.scroll.setWidget(self._palette_container)
        material_layout.addWidget(self.scroll)
        
        # Dropdown as fallback (filtered by game)
        self.material_combo = QComboBox()
        self._rebuild_dropdown()
        
        # Set default to Dark Iron
        idx = self.material_combo.findData(self.current_material)
        if idx >= 0:
            self.material_combo.setCurrentIndex(idx)
        self.material_combo.currentIndexChanged.connect(self._on_combo_changed)
        
        material_layout.addWidget(QLabel("Quick select:"))
        material_layout.addWidget(self.material_combo)

        # Open the per-material physics property editor (mass/density).
        edit_mat_btn = QPushButton("\u2699 Edit Materials\u2026")
        edit_mat_btn.setToolTip("Edit per-material mass/density used by the physics pipeline")
        edit_mat_btn.clicked.connect(self.edit_materials.emit)
        material_layout.addWidget(edit_mat_btn)

        material_group.setLayout(material_layout)
        layout.addWidget(material_group)
        
        self.setLayout(layout)
        self.setMaximumWidth(250)
        
        # Highlight initial selection
        self._highlight_selected(self.current_material)
        
    def _get_active_categories(self):
        game = self.game_combo.currentData() if hasattr(self, 'game_combo') else ACTIVE_GAME
        return STEEL_CITY_CATEGORIES if game == "steel_city" else STEEL_TIDE_CATEGORIES
    
    def _get_active_material_ids(self):
        ids = [0]  # Air always available
        for _, mat_ids in self._get_active_categories():
            ids.extend(mid for mid in mat_ids if mid in MATERIALS)
        return ids
    
    def _build_palette(self):
        """Rebuild the swatch grid for the active game."""
        # Clear existing
        self._swatches.clear()
        # Remove all widgets from palette layout
        while self._palette_layout.count():
            item = self._palette_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
            elif item.layout():
                self._clear_layout(item.layout())
        
        for cat_name, mat_ids in self._get_active_categories():
            if not any(mid in MATERIALS for mid in mat_ids):
                continue
            cat_label = QLabel(f"  {cat_name}")
            cat_label.setStyleSheet("font-size: 10px; color: #aaa; font-weight: bold; padding-top: 4px;")
            self._palette_layout.addWidget(cat_label)
            
            row = QGridLayout()
            row.setSpacing(2)
            col = 0
            for mid in mat_ids:
                if mid not in MATERIALS:
                    continue
                swatch = MaterialSwatch(mid, self)
                self._swatches[mid] = swatch
                row.addWidget(swatch, 0, col)
                col += 1
            self._palette_layout.addLayout(row)
        
        self._palette_layout.addStretch()
    
    def _clear_layout(self, layout):
        while layout.count():
            item = layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
            elif item.layout():
                self._clear_layout(item.layout())
    
    def _rebuild_dropdown(self):
        """Rebuild the dropdown for the active game."""
        self.material_combo.blockSignals(True)
        self.material_combo.clear()
        for mat_id in self._get_active_material_ids():
            name = get_material_name(mat_id)
            self.material_combo.addItem(f"{name} ({mat_id})", mat_id)
        self.material_combo.blockSignals(False)
    
    def _on_game_changed(self):
        """Game selector changed — rebuild palette and dropdown."""
        game = self.game_combo.currentData()
        print(f"🎮 Game: {self.game_combo.currentText()}")
        self._build_palette()
        self._rebuild_dropdown()
        # Pick a sensible default for the game
        if game == "steel_city":
            self.select_material(109)  # Dark Iron
        else:
            self.select_material(3)   # Concrete
        idx = self.material_combo.findData(self.current_material)
        if idx >= 0:
            self.material_combo.blockSignals(True)
            self.material_combo.setCurrentIndex(idx)
            self.material_combo.blockSignals(False)
        self._highlight_selected(self.current_material)
    
    def select_material(self, mat_id):
        """Select a material from the palette."""
        self.current_material = mat_id
        self.current_mat_label.setText(f"Selected: {get_material_name(mat_id)} ({mat_id})")
        self.material_changed.emit(mat_id)
        # Sync dropdown
        idx = self.material_combo.findData(mat_id)
        if idx >= 0:
            self.material_combo.blockSignals(True)
            self.material_combo.setCurrentIndex(idx)
            self.material_combo.blockSignals(False)
        print(f"🎨 Material: {get_material_name(mat_id)} ({mat_id})")
    
    def _highlight_selected(self, mat_id):
        """Highlight the selected swatch with a gold border."""
        for mid, swatch in self._swatches.items():
            r, g, b, a = get_material_color_255(mid)
            if mid == mat_id:
                swatch.setStyleSheet(
                    f"background-color: rgba({r},{g},{b},{a}); {self._selected_border}"
                )
            else:
                swatch.setStyleSheet(
                    f"background-color: rgba({r},{g},{b},{a}); border: 1px solid #666;"
                )
    
    def _on_combo_changed(self, index):
        """Dropdown material changed — sync palette."""
        material_id = self.material_combo.currentData()
        self.select_material(material_id)
        self._highlight_selected(material_id)
        
    def set_tool(self, tool_name):
        """Change active tool"""
        self.current_tool = tool_name
        self.tool_changed.emit(tool_name)
        print(f"🔧 Tool: {tool_name}")
        
    def _on_material_changed(self, index):
        """Material combo box changed (legacy compat)"""
        self._on_combo_changed(index)
        
    def get_current_material(self):
        """Get currently selected material ID"""
        return self.current_material
        
    def get_current_tool(self):
        """Get currently selected tool"""
        return self.current_tool
