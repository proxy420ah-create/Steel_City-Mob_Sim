# Steel Tide: Voxel Asset Studio
# tool_panel.py - Left sidebar with tools and material selector

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QPushButton, QLabel, 
                             QComboBox, QGroupBox, QButtonGroup, QRadioButton)
from PyQt6.QtCore import pyqtSignal, Qt
from material_library import MATERIALS, DEFAULT_MATERIALS, get_material_name

class ToolPanel(QWidget):
    """Left sidebar with painting tools and material selector"""
    
    material_changed = pyqtSignal(int)  # Material ID
    tool_changed = pyqtSignal(str)      # Tool name
    clear_selection = pyqtSignal()     # Clear selection
    edit_materials = pyqtSignal()      # Open the material properties editor
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
        self.current_material = 3  # Default: Concrete
        self.current_tool = "paint"
        
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
        
        # Material selection
        material_group = QGroupBox("Material")
        material_layout = QVBoxLayout()
        
        self.material_combo = QComboBox()
        for mat_id in DEFAULT_MATERIALS:
            name = get_material_name(mat_id)
            self.material_combo.addItem(f"{name} ({mat_id})", mat_id)
        
        # Set default to Concrete (index 1)
        self.material_combo.setCurrentIndex(1)
        self.material_combo.currentIndexChanged.connect(self._on_material_changed)
        
        material_layout.addWidget(QLabel("Select:"))
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
        
    def set_tool(self, tool_name):
        """Change active tool"""
        self.current_tool = tool_name
        self.tool_changed.emit(tool_name)
        print(f"🔧 Tool: {tool_name}")
        
    def _on_material_changed(self, index):
        """Material combo box changed"""
        material_id = self.material_combo.currentData()
        self.current_material = material_id
        self.material_changed.emit(material_id)
        print(f"🎨 Material: {get_material_name(material_id)} ({material_id})")
        
    def get_current_material(self):
        """Get currently selected material ID"""
        return self.current_material
        
    def get_current_tool(self):
        """Get currently selected tool"""
        return self.current_tool
