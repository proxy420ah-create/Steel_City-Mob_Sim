# Steel Tide: Voxel Asset Studio
# transform_panel.py - Model transform controls for repositioning loaded models

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel, 
                             QPushButton, QSpinBox, QGroupBox)
from PyQt6.QtCore import pyqtSignal

class TransformPanel(QWidget):
    """Panel for transforming (moving) the loaded model in viewport"""
    
    # Signals
    offset_changed = pyqtSignal(int, int, int)  # x, y, z offset
    reset_transform = pyqtSignal()
    snap_to_reference = pyqtSignal(str)  # reference model name
    
    def __init__(self, reference_library, parent=None):
        super().__init__(parent)
        
        self.reference_library = reference_library
        self.init_ui()
    
    def init_ui(self):
        """Build the transform panel UI"""
        layout = QVBoxLayout()
        layout.setContentsMargins(5, 5, 5, 5)
        
        # Title
        title = QLabel("📐 Model Transform")
        title.setStyleSheet("font-weight: bold; font-size: 14px; padding: 5px;")
        layout.addWidget(title)
        
        # Info card
        info = QLabel("Move loaded model to compare with reference models")
        info.setStyleSheet("""
            QLabel {
                background-color: #2a2a2a;
                color: #aaa;
                padding: 8px;
                border-radius: 4px;
                font-size: 11px;
            }
        """)
        info.setWordWrap(True)
        layout.addWidget(info)
        
        # Offset controls
        offset_group = QGroupBox("Position Offset (voxels)")
        offset_layout = QVBoxLayout()
        
        # X offset
        x_layout = QHBoxLayout()
        x_layout.addWidget(QLabel("X:"))
        self.x_offset = QSpinBox()
        self.x_offset.setRange(-256, 256)
        self.x_offset.setValue(0)
        self.x_offset.setSingleStep(8)  # Snap to 8-voxel grid
        self.x_offset.setToolTip("Horizontal offset (Red axis)")
        self.x_offset.valueChanged.connect(self._emit_offset_changed)
        x_layout.addWidget(self.x_offset)
        offset_layout.addLayout(x_layout)
        
        # Y offset
        y_layout = QHBoxLayout()
        y_layout.addWidget(QLabel("Y:"))
        self.y_offset = QSpinBox()
        self.y_offset.setRange(-256, 256)
        self.y_offset.setValue(0)
        self.y_offset.setSingleStep(8)
        self.y_offset.setToolTip("Vertical offset (Blue axis)")
        self.y_offset.valueChanged.connect(self._emit_offset_changed)
        y_layout.addWidget(self.y_offset)
        offset_layout.addLayout(y_layout)
        
        # Z offset
        z_layout = QHBoxLayout()
        z_layout.addWidget(QLabel("Z:"))
        self.z_offset = QSpinBox()
        self.z_offset.setRange(-256, 256)
        self.z_offset.setValue(0)
        self.z_offset.setSingleStep(8)
        self.z_offset.setToolTip("Depth offset (Green axis)")
        self.z_offset.valueChanged.connect(self._emit_offset_changed)
        z_layout.addWidget(self.z_offset)
        offset_layout.addLayout(z_layout)
        
        offset_group.setLayout(offset_layout)
        layout.addWidget(offset_group)
        
        # Quick snap buttons
        snap_group = QGroupBox("Quick Snap to Reference")
        snap_layout = QVBoxLayout()
        
        # Create snap button for each reference model
        for model in self.reference_library.models:
            if model.visible:
                btn = QPushButton(f"{model.icon} {model.name}")
                btn.setToolTip(f"Snap next to {model.name} for size comparison")
                btn.clicked.connect(lambda checked, name=model.name: self._snap_to_reference(name))
                snap_layout.addWidget(btn)
        
        snap_group.setLayout(snap_layout)
        layout.addWidget(snap_group)
        
        # Reset button
        reset_btn = QPushButton("🔄 Reset to Origin")
        reset_btn.setToolTip("Move model back to (0, 0, 0)")
        reset_btn.clicked.connect(self._reset_transform)
        layout.addWidget(reset_btn)
        
        layout.addStretch()
        self.setLayout(layout)
    
    def _emit_offset_changed(self):
        """Emit signal when offset values change"""
        x = self.x_offset.value()
        y = self.y_offset.value()
        z = self.z_offset.value()
        self.offset_changed.emit(x, y, z)
    
    def _reset_transform(self):
        """Reset all offsets to zero"""
        self.x_offset.setValue(0)
        self.y_offset.setValue(0)
        self.z_offset.setValue(0)
        self.reset_transform.emit()
    
    def _snap_to_reference(self, name):
        """Snap model next to specified reference model"""
        self.snap_to_reference.emit(name)
    
    def set_offset(self, x, y, z):
        """Set offset values programmatically"""
        self.x_offset.blockSignals(True)
        self.y_offset.blockSignals(True)
        self.z_offset.blockSignals(True)
        
        self.x_offset.setValue(x)
        self.y_offset.setValue(y)
        self.z_offset.setValue(z)
        
        self.x_offset.blockSignals(False)
        self.y_offset.blockSignals(False)
        self.z_offset.blockSignals(False)
