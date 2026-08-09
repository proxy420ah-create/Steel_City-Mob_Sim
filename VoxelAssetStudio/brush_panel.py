# Steel Tide: Voxel Asset Studio
# brush_panel.py - Brush settings panel

from PyQt6.QtWidgets import QWidget, QVBoxLayout, QLabel, QGroupBox, QSlider
from PyQt6.QtCore import Qt, pyqtSignal

class BrushPanel(QWidget):
    """Brush settings panel"""
    
    brush_size_changed = pyqtSignal(int)  # Emits new brush size
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.init_ui()
        
    def init_ui(self):
        """Build the UI"""
        layout = QVBoxLayout()
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        
        # Title
        title = QLabel("🖌️ Brush Settings")
        title.setStyleSheet("font-size: 14px; font-weight: bold; padding: 5px;")
        layout.addWidget(title)
        
        # Brush size group
        brush_group = QGroupBox("Brush Size")
        brush_layout = QVBoxLayout()
        
        # Size label
        self.size_label = QLabel("Size: 1 voxel")
        self.size_label.setStyleSheet("font-size: 12px; padding: 3px;")
        self.size_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        brush_layout.addWidget(self.size_label)
        
        # Size slider
        self.size_slider = QSlider(Qt.Orientation.Horizontal)
        self.size_slider.setMinimum(1)
        self.size_slider.setMaximum(10)
        self.size_slider.setValue(1)
        self.size_slider.setTickPosition(QSlider.TickPosition.TicksBelow)
        self.size_slider.setTickInterval(1)
        self.size_slider.valueChanged.connect(self.on_size_changed)
        brush_layout.addWidget(self.size_slider)
        
        # Size hint
        hint = QLabel("<i>1 = Single voxel<br>10 = Large brush</i>")
        hint.setStyleSheet("font-size: 10px; color: #888; padding: 2px;")
        hint.setAlignment(Qt.AlignmentFlag.AlignCenter)
        brush_layout.addWidget(hint)
        
        brush_group.setLayout(brush_layout)
        layout.addWidget(brush_group)
        
        # Spacer
        layout.addStretch()
        
        self.setLayout(layout)
        self.setMaximumWidth(250)
        
    def on_size_changed(self, value):
        """Handle brush size change"""
        if value == 1:
            self.size_label.setText("Size: 1 voxel")
        else:
            self.size_label.setText(f"Size: {value} voxels")
        
        self.brush_size_changed.emit(value)
        
    def get_brush_size(self):
        """Get current brush size"""
        return self.size_slider.value()
