"""
Workspace Volume Panel - Controls for resizing the editable numpy array
"""
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel, 
                             QSpinBox, QPushButton, QGroupBox)
from PyQt6.QtCore import pyqtSignal

class WorkspacePanel(QWidget):
    """Panel for controlling workspace volume size"""
    
    # Signals
    volume_resized = pyqtSignal(int, int, int)  # (x, y, z) new dimensions
    
    def __init__(self):
        super().__init__()
        self.init_ui()
        
    def init_ui(self):
        """Build the panel UI"""
        layout = QVBoxLayout()
        layout.setContentsMargins(8, 8, 8, 8)
        
        # Group box
        group = QGroupBox("📦 Workspace Volume")
        group_layout = QVBoxLayout()
        
        # Title label
        info_label = QLabel("Editable numpy array size:")
        info_label.setStyleSheet("color: #888; font-size: 11px;")
        group_layout.addWidget(info_label)
        
        # X dimension
        x_layout = QHBoxLayout()
        x_layout.addWidget(QLabel("X:"))
        self.x_spin = QSpinBox()
        self.x_spin.setRange(1, 256)
        self.x_spin.setValue(32)
        self.x_spin.setSuffix(" voxels")
        self.x_spin.valueChanged.connect(self._on_dimension_changed)
        x_layout.addWidget(self.x_spin)
        group_layout.addLayout(x_layout)
        
        # Y dimension (height)
        y_layout = QHBoxLayout()
        y_layout.addWidget(QLabel("Y:"))
        self.y_spin = QSpinBox()
        self.y_spin.setRange(1, 256)
        self.y_spin.setValue(32)
        self.y_spin.setSuffix(" voxels (height)")
        self.y_spin.valueChanged.connect(self._on_dimension_changed)
        y_layout.addWidget(self.y_spin)
        group_layout.addLayout(y_layout)
        
        # Z dimension (depth)
        z_layout = QHBoxLayout()
        z_layout.addWidget(QLabel("Z:"))
        self.z_spin = QSpinBox()
        self.z_spin.setRange(1, 256)
        self.z_spin.setValue(32)
        self.z_spin.setSuffix(" voxels (depth)")
        self.z_spin.valueChanged.connect(self._on_dimension_changed)
        z_layout.addWidget(self.z_spin)
        group_layout.addLayout(z_layout)
        
        # Quick size buttons
        quick_layout = QHBoxLayout()
        quick_layout.addWidget(QLabel("Quick:"))
        
        btn_16 = QPushButton("16³")
        btn_16.clicked.connect(lambda: self.set_cubic_size(16))
        quick_layout.addWidget(btn_16)
        
        btn_32 = QPushButton("32³")
        btn_32.clicked.connect(lambda: self.set_cubic_size(32))
        quick_layout.addWidget(btn_32)
        
        btn_64 = QPushButton("64³")
        btn_64.clicked.connect(lambda: self.set_cubic_size(64))
        quick_layout.addWidget(btn_64)
        
        group_layout.addLayout(quick_layout)
        
        # Warning label
        warning = QLabel("⚠️ Resizing preserves existing voxels")
        warning.setStyleSheet("color: #FF9800; font-size: 10px;")
        group_layout.addWidget(warning)
        
        group.setLayout(group_layout)
        layout.addWidget(group)
        layout.addStretch()
        
        self.setLayout(layout)
    
    def set_cubic_size(self, size):
        """Set all dimensions to the same size"""
        self.x_spin.setValue(size)
        self.y_spin.setValue(size)
        self.z_spin.setValue(size)
    
    def set_dimensions(self, x, y, z):
        """Update spinboxes to match current workspace size"""
        self.x_spin.blockSignals(True)
        self.y_spin.blockSignals(True)
        self.z_spin.blockSignals(True)
        
        self.x_spin.setValue(x)
        self.y_spin.setValue(y)
        self.z_spin.setValue(z)
        
        self.x_spin.blockSignals(False)
        self.y_spin.blockSignals(False)
        self.z_spin.blockSignals(False)
    
    def _on_dimension_changed(self):
        """Dimension spinbox changed - emit resize signal instantly"""
        x = self.x_spin.value()
        y = self.y_spin.value()
        z = self.z_spin.value()
        self.volume_resized.emit(x, y, z)
