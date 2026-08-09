# Steel Tide: Voxel Asset Studio
# controls_panel.py - Mouse controls reference panel

from PyQt6.QtWidgets import QWidget, QVBoxLayout, QLabel, QGroupBox
from PyQt6.QtCore import Qt

class ControlsPanel(QWidget):
    """Display mouse controls reference"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.init_ui()
        
    def init_ui(self):
        """Build the UI"""
        layout = QVBoxLayout()
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        
        # Title
        title = QLabel("🖱️ Mouse Controls")
        title.setStyleSheet("font-size: 14px; font-weight: bold; padding: 5px;")
        layout.addWidget(title)
        
        # Hint
        hint = QLabel("<i>Options → Mouse Controls to customize</i>")
        hint.setStyleSheet("font-size: 10px; color: #888; padding: 2px;")
        layout.addWidget(hint)
        
        # Controls group
        controls_group = QGroupBox()
        controls_layout = QVBoxLayout()
        
        # Left-click
        left_label = QLabel("🖱️ <b>Left-Click:</b> Paint/Interact")
        left_label.setStyleSheet("padding: 3px;")
        controls_layout.addWidget(left_label)
        
        # Middle-click
        middle_label = QLabel("🖱️ <b>Middle-Click:</b> Pan Camera")
        middle_label.setStyleSheet("padding: 3px;")
        controls_layout.addWidget(middle_label)
        
        # Right-click
        right_label = QLabel("🖱️ <b>Right-Click:</b> Orbit Camera")
        right_label.setStyleSheet("padding: 3px;")
        controls_layout.addWidget(right_label)
        
        # Wheel
        wheel_label = QLabel("🖱️ <b>Wheel:</b> Zoom In/Out")
        wheel_label.setStyleSheet("padding: 3px;")
        controls_layout.addWidget(wheel_label)
        
        controls_group.setLayout(controls_layout)
        layout.addWidget(controls_group)
        
        # Keyboard shortcuts
        keyboard_group = QGroupBox("⌨️ Keyboard")
        keyboard_layout = QVBoxLayout()
        
        wasd_label = QLabel("<b>WASD:</b> Pan Camera")
        wasd_label.setStyleSheet("padding: 3px;")
        keyboard_layout.addWidget(wasd_label)
        
        qe_label = QLabel("<b>Q/E:</b> Pan Up/Down")
        qe_label.setStyleSheet("padding: 3px;")
        keyboard_layout.addWidget(qe_label)
        
        home_label = QLabel("<b>Home:</b> Reset Camera")
        home_label.setStyleSheet("padding: 3px;")
        keyboard_layout.addWidget(home_label)
        
        ctrl_s_label = QLabel("<b>Ctrl+S:</b> Save")
        ctrl_s_label.setStyleSheet("padding: 3px;")
        keyboard_layout.addWidget(ctrl_s_label)
        
        keyboard_group.setLayout(keyboard_layout)
        layout.addWidget(keyboard_group)
        
        # Spacer
        layout.addStretch()
        
        self.setLayout(layout)
        self.setMaximumWidth(250)
