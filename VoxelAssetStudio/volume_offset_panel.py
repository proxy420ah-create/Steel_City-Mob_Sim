# Steel Tide: Voxel Asset Studio
# volume_offset_panel.py - Controls for moving asset within the numpy volume

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel, 
                             QPushButton, QSpinBox, QGroupBox)
from PyQt6.QtCore import pyqtSignal

class VolumeOffsetPanel(QWidget):
    """Panel for shifting the asset within the numpy volume (recentering)"""
    
    # Signals
    offset_changed = pyqtSignal(int, int, int)  # x, y, z shift within volume
    reset_offset = pyqtSignal()
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.prev_x = 0
        self.prev_y = 0
        self.prev_z = 0
        self.init_ui()
    
    def init_ui(self):
        """Build the volume offset panel UI"""
        layout = QVBoxLayout()
        layout.setContentsMargins(5, 5, 5, 5)
        
        # Title
        title = QLabel("📦 Volume Offset")
        title.setStyleSheet("font-weight: bold; font-size: 14px; padding: 5px;")
        layout.addWidget(title)
        
        # Info card
        info = QLabel("Shift asset within the numpy volume to recenter or reposition")
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
        offset_group = QGroupBox("Position Shift (voxels)")
        offset_layout = QVBoxLayout()
        
        # X offset
        x_layout = QHBoxLayout()
        x_layout.addWidget(QLabel("X:"))
        self.x_offset = QSpinBox()
        self.x_offset.setRange(-128, 128)
        self.x_offset.setValue(0)
        self.x_offset.setSingleStep(1)
        self.x_offset.setToolTip("Shift asset left/right within volume")
        self.x_offset.valueChanged.connect(self._emit_offset_changed)
        x_layout.addWidget(self.x_offset)
        offset_layout.addLayout(x_layout)
        
        # Y offset
        y_layout = QHBoxLayout()
        y_layout.addWidget(QLabel("Y:"))
        self.y_offset = QSpinBox()
        self.y_offset.setRange(-128, 128)
        self.y_offset.setValue(0)
        self.y_offset.setSingleStep(1)
        self.y_offset.setToolTip("Shift asset up/down within volume")
        self.y_offset.valueChanged.connect(self._emit_offset_changed)
        y_layout.addWidget(self.y_offset)
        offset_layout.addLayout(y_layout)
        
        # Z offset
        z_layout = QHBoxLayout()
        z_layout.addWidget(QLabel("Z:"))
        self.z_offset = QSpinBox()
        self.z_offset.setRange(-128, 128)
        self.z_offset.setValue(0)
        self.z_offset.setSingleStep(1)
        self.z_offset.setToolTip("Shift asset forward/back within volume")
        self.z_offset.valueChanged.connect(self._emit_offset_changed)
        z_layout.addWidget(self.z_offset)
        offset_layout.addLayout(z_layout)
        
        offset_group.setLayout(offset_layout)
        layout.addWidget(offset_group)
        
        # Reset button
        reset_btn = QPushButton("🔄 Reset to Center")
        reset_btn.setToolTip("Move asset back to center of volume")
        reset_btn.clicked.connect(self._reset_offset)
        layout.addWidget(reset_btn)
        
        layout.addStretch()
        self.setLayout(layout)
    
    def _emit_offset_changed(self):
        """Emit signal when offset values change (emit delta from previous)"""
        x = self.x_offset.value()
        y = self.y_offset.value()
        z = self.z_offset.value()
        
        # Calculate delta (change from previous value)
        delta_x = x - self.prev_x
        delta_y = y - self.prev_y
        delta_z = z - self.prev_z
        
        # Update previous values
        self.prev_x = x
        self.prev_y = y
        self.prev_z = z
        
        # Emit the delta shift
        self.offset_changed.emit(delta_x, delta_y, delta_z)
    
    def _reset_offset(self):
        """Reset all offsets to zero"""
        self.x_offset.setValue(0)
        self.y_offset.setValue(0)
        self.z_offset.setValue(0)
        self.reset_offset.emit()
    
    def set_offset(self, x, y, z):
        """Set offset values programmatically"""
        self.x_offset.blockSignals(True)
        self.y_offset.blockSignals(True)
        self.z_offset.blockSignals(True)
        
        self.x_offset.setValue(x)
        self.y_offset.setValue(y)
        self.z_offset.setValue(z)
        
        # Update previous values to match
        self.prev_x = x
        self.prev_y = y
        self.prev_z = z
        
        self.x_offset.blockSignals(False)
        self.y_offset.blockSignals(False)
        self.z_offset.blockSignals(False)
