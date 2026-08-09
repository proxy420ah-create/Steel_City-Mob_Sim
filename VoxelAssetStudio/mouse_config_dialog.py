# Steel Tide: Voxel Asset Studio
# mouse_config_dialog.py - Mouse controls configuration dialog

from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel, 
                             QComboBox, QPushButton, QGroupBox, QMessageBox, QSlider)
from PyQt6.QtCore import Qt, pyqtSignal

class MouseConfigDialog(QDialog):
    """Dialog for configuring mouse button assignments"""
    
    config_changed = pyqtSignal(dict)  # Emit new config when applied
    
    def __init__(self, current_config, parent=None):
        super().__init__(parent)
        
        self.setWindowTitle("Mouse Controls Configuration")
        self.setModal(True)
        self.resize(400, 300)
        
        self.current_config = current_config.copy()
        
        self.init_ui()
        
    def init_ui(self):
        """Build the dialog UI"""
        layout = QVBoxLayout()
        
        # Title
        title = QLabel("🖱️ Configure Mouse Controls")
        title.setStyleSheet("font-size: 16px; font-weight: bold; padding: 10px;")
        layout.addWidget(title)
        
        # Instructions
        instructions = QLabel("Assign mouse buttons to camera actions:")
        instructions.setStyleSheet("padding: 5px; color: #aaa;")
        layout.addWidget(instructions)
        
        # Configuration group
        config_group = QGroupBox("Button Assignments")
        config_layout = QVBoxLayout()
        
        # Orbit control
        orbit_layout = QHBoxLayout()
        orbit_layout.addWidget(QLabel("Orbit Camera:"))
        self.orbit_combo = QComboBox()
        self.orbit_combo.addItems(["Left Button", "Middle Button", "Right Button", "Disabled"])
        self.orbit_combo.setCurrentText(self.current_config.get("orbit", "Right Button"))
        orbit_layout.addWidget(self.orbit_combo)
        config_layout.addLayout(orbit_layout)
        
        # Pan control
        pan_layout = QHBoxLayout()
        pan_layout.addWidget(QLabel("Pan Camera:"))
        self.pan_combo = QComboBox()
        self.pan_combo.addItems(["Left Button", "Middle Button", "Right Button", "Disabled"])
        self.pan_combo.setCurrentText(self.current_config.get("pan", "Middle Button"))
        pan_layout.addWidget(self.pan_combo)
        config_layout.addLayout(pan_layout)
        
        # Paint/Interact control
        paint_layout = QHBoxLayout()
        paint_layout.addWidget(QLabel("Paint/Interact:"))
        self.paint_combo = QComboBox()
        self.paint_combo.addItems(["Left Button", "Middle Button", "Right Button", "Disabled"])
        self.paint_combo.setCurrentText(self.current_config.get("paint", "Left Button"))
        paint_layout.addWidget(self.paint_combo)
        config_layout.addLayout(paint_layout)
        
        config_group.setLayout(config_layout)
        layout.addWidget(config_group)
        
        # Sensitivity controls
        sensitivity_group = QGroupBox("Mouse Sensitivity")
        sensitivity_layout = QVBoxLayout()
        
        # Orbit sensitivity
        orbit_sens_layout = QHBoxLayout()
        orbit_sens_layout.addWidget(QLabel("Orbit Speed:"))
        self.orbit_sensitivity = QSlider(Qt.Orientation.Horizontal)
        self.orbit_sensitivity.setMinimum(1)
        self.orbit_sensitivity.setMaximum(20)
        self.orbit_sensitivity.setValue(int(self.current_config.get("orbit_sensitivity", 1.0) * 10))
        self.orbit_sensitivity.setTickPosition(QSlider.TickPosition.TicksBelow)
        self.orbit_sensitivity.setTickInterval(5)
        orbit_sens_layout.addWidget(self.orbit_sensitivity)
        self.orbit_sens_label = QLabel(f"{self.orbit_sensitivity.value() / 10:.1f}x")
        self.orbit_sens_label.setMinimumWidth(40)
        orbit_sens_layout.addWidget(self.orbit_sens_label)
        self.orbit_sensitivity.valueChanged.connect(
            lambda v: self.orbit_sens_label.setText(f"{v / 10:.1f}x")
        )
        sensitivity_layout.addLayout(orbit_sens_layout)
        
        # Pan sensitivity
        pan_sens_layout = QHBoxLayout()
        pan_sens_layout.addWidget(QLabel("Pan Speed:"))
        self.pan_sensitivity = QSlider(Qt.Orientation.Horizontal)
        self.pan_sensitivity.setMinimum(1)
        self.pan_sensitivity.setMaximum(20)
        self.pan_sensitivity.setValue(int(self.current_config.get("pan_sensitivity", 1.0) * 10))
        self.pan_sensitivity.setTickPosition(QSlider.TickPosition.TicksBelow)
        self.pan_sensitivity.setTickInterval(5)
        pan_sens_layout.addWidget(self.pan_sensitivity)
        self.pan_sens_label = QLabel(f"{self.pan_sensitivity.value() / 10:.1f}x")
        self.pan_sens_label.setMinimumWidth(40)
        pan_sens_layout.addWidget(self.pan_sens_label)
        self.pan_sensitivity.valueChanged.connect(
            lambda v: self.pan_sens_label.setText(f"{v / 10:.1f}x")
        )
        sensitivity_layout.addLayout(pan_sens_layout)
        
        # Zoom sensitivity
        zoom_sens_layout = QHBoxLayout()
        zoom_sens_layout.addWidget(QLabel("Zoom Speed:"))
        self.zoom_sensitivity = QSlider(Qt.Orientation.Horizontal)
        self.zoom_sensitivity.setMinimum(1)
        self.zoom_sensitivity.setMaximum(20)
        self.zoom_sensitivity.setValue(int(self.current_config.get("zoom_sensitivity", 1.0) * 10))
        self.zoom_sensitivity.setTickPosition(QSlider.TickPosition.TicksBelow)
        self.zoom_sensitivity.setTickInterval(5)
        zoom_sens_layout.addWidget(self.zoom_sensitivity)
        self.zoom_sens_label = QLabel(f"{self.zoom_sensitivity.value() / 10:.1f}x")
        self.zoom_sens_label.setMinimumWidth(40)
        zoom_sens_layout.addWidget(self.zoom_sens_label)
        self.zoom_sensitivity.valueChanged.connect(
            lambda v: self.zoom_sens_label.setText(f"{v / 10:.1f}x")
        )
        sensitivity_layout.addLayout(zoom_sens_layout)
        
        # Keyboard pan sensitivity
        keyboard_sens_layout = QHBoxLayout()
        keyboard_sens_layout.addWidget(QLabel("Keyboard Pan (WASD):"))
        self.keyboard_sensitivity = QSlider(Qt.Orientation.Horizontal)
        self.keyboard_sensitivity.setMinimum(1)
        self.keyboard_sensitivity.setMaximum(20)
        self.keyboard_sensitivity.setValue(int(self.current_config.get("keyboard_sensitivity", 1.0) * 10))
        self.keyboard_sensitivity.setTickPosition(QSlider.TickPosition.TicksBelow)
        self.keyboard_sensitivity.setTickInterval(5)
        keyboard_sens_layout.addWidget(self.keyboard_sensitivity)
        self.keyboard_sens_label = QLabel(f"{self.keyboard_sensitivity.value() / 10:.1f}x")
        self.keyboard_sens_label.setMinimumWidth(40)
        keyboard_sens_layout.addWidget(self.keyboard_sens_label)
        self.keyboard_sensitivity.valueChanged.connect(
            lambda v: self.keyboard_sens_label.setText(f"{v / 10:.1f}x")
        )
        sensitivity_layout.addLayout(keyboard_sens_layout)
        
        sensitivity_group.setLayout(sensitivity_layout)
        layout.addWidget(sensitivity_group)
        
        # Presets
        preset_group = QGroupBox("Presets")
        preset_layout = QHBoxLayout()
        
        default_btn = QPushButton("Default (L:Paint, M:Pan, R:Orbit)")
        default_btn.clicked.connect(self.apply_default_preset)
        preset_layout.addWidget(default_btn)
        
        blender_btn = QPushButton("Blender-style (M:Orbit, Shift+M:Pan)")
        blender_btn.setEnabled(False)  # Not implemented yet
        blender_btn.setToolTip("Coming soon!")
        preset_layout.addWidget(blender_btn)
        
        preset_group.setLayout(preset_layout)
        layout.addWidget(preset_group)
        
        # Buttons
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        apply_btn = QPushButton("Apply")
        apply_btn.clicked.connect(self.apply_config)
        button_layout.addWidget(apply_btn)
        
        cancel_btn = QPushButton("Cancel")
        cancel_btn.clicked.connect(self.reject)
        button_layout.addWidget(cancel_btn)
        
        layout.addLayout(button_layout)
        
        self.setLayout(layout)
        
    def apply_default_preset(self):
        """Apply default mouse configuration"""
        self.orbit_combo.setCurrentText("Right Button")
        self.pan_combo.setCurrentText("Middle Button")
        self.paint_combo.setCurrentText("Left Button")
        self.orbit_sensitivity.setValue(10)    # 1.0x
        self.pan_sensitivity.setValue(10)      # 1.0x
        self.zoom_sensitivity.setValue(10)     # 1.0x
        self.keyboard_sensitivity.setValue(10) # 1.0x
        
    def apply_config(self):
        """Validate and apply configuration"""
        new_config = {
            "orbit": self.orbit_combo.currentText(),
            "pan": self.pan_combo.currentText(),
            "paint": self.paint_combo.currentText(),
            "orbit_sensitivity": self.orbit_sensitivity.value() / 10.0,
            "pan_sensitivity": self.pan_sensitivity.value() / 10.0,
            "zoom_sensitivity": self.zoom_sensitivity.value() / 10.0,
            "keyboard_sensitivity": self.keyboard_sensitivity.value() / 10.0
        }
        
        # Check for conflicts (same button assigned to multiple actions)
        # Only validate button assignments, not sensitivity values
        button_actions = ["orbit", "pan", "paint"]
        assignments = {}
        for action in button_actions:
            button = new_config[action]
            if button != "Disabled":
                if button in assignments:
                    QMessageBox.warning(
                        self,
                        "Conflict Detected",
                        f"{button} is assigned to both '{assignments[button]}' and '{action}'.\n"
                        "Each button can only have one action."
                    )
                    return
                assignments[button] = action
        
        # Emit config and close
        self.config_changed.emit(new_config)
        self.accept()
