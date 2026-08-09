# Steel Tide: Voxel Asset Studio
# settings_dialog.py - Settings dialog

from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel, 
                             QGroupBox, QCheckBox, QPushButton, QTabWidget, QWidget)
from PyQt6.QtCore import Qt, pyqtSignal

class SettingsDialog(QDialog):
    """Settings dialog for application preferences"""
    
    settings_changed = pyqtSignal(dict)  # Emits settings dict
    
    def __init__(self, parent=None, current_settings=None):
        super().__init__(parent)
        self.current_settings = current_settings or {}
        self.init_ui()
        
    def init_ui(self):
        """Build the UI"""
        self.setWindowTitle("Settings")
        self.resize(500, 400)
        
        layout = QVBoxLayout()
        
        # Tab widget for different settings categories
        tabs = QTabWidget()
        
        # Viewport tab
        viewport_tab = self.create_viewport_tab()
        tabs.addTab(viewport_tab, "Viewport")
        
        # Controls tab
        controls_tab = self.create_controls_tab()
        tabs.addTab(controls_tab, "Controls")
        
        layout.addWidget(tabs)
        
        # Buttons
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        ok_button = QPushButton("OK")
        ok_button.clicked.connect(self.accept)
        button_layout.addWidget(ok_button)
        
        cancel_button = QPushButton("Cancel")
        cancel_button.clicked.connect(self.reject)
        button_layout.addWidget(cancel_button)
        
        layout.addLayout(button_layout)
        
        self.setLayout(layout)
        
    def create_viewport_tab(self):
        """Create viewport settings tab"""
        tab = QWidget()
        layout = QVBoxLayout()
        
        # Hover highlight group
        hover_group = QGroupBox("Voxel Highlighting")
        hover_layout = QVBoxLayout()
        
        self.highlight_hover_checkbox = QCheckBox("Highlight voxel under mouse cursor")
        self.highlight_hover_checkbox.setChecked(self.current_settings.get('highlight_hover', True))
        hover_layout.addWidget(self.highlight_hover_checkbox)
        
        hint = QLabel("<i>Shows which voxel you're about to edit</i>")
        hint.setStyleSheet("font-size: 10px; color: #888; padding-left: 20px;")
        hover_layout.addWidget(hint)
        
        hover_group.setLayout(hover_layout)
        layout.addWidget(hover_group)
        
        layout.addStretch()
        tab.setLayout(layout)
        return tab
        
    def create_controls_tab(self):
        """Create controls reference tab"""
        tab = QWidget()
        layout = QVBoxLayout()
        
        # Mouse controls group
        mouse_group = QGroupBox("🖱️ Mouse Controls")
        mouse_layout = QVBoxLayout()
        
        left_label = QLabel("<b>Left-Click:</b> Paint/Interact")
        left_label.setStyleSheet("padding: 3px;")
        mouse_layout.addWidget(left_label)
        
        middle_label = QLabel("<b>Middle-Click:</b> Pan Camera")
        middle_label.setStyleSheet("padding: 3px;")
        mouse_layout.addWidget(middle_label)
        
        right_label = QLabel("<b>Right-Click:</b> Orbit Camera")
        right_label.setStyleSheet("padding: 3px;")
        mouse_layout.addWidget(right_label)
        
        wheel_label = QLabel("<b>Wheel:</b> Zoom In/Out")
        wheel_label.setStyleSheet("padding: 3px;")
        mouse_layout.addWidget(wheel_label)
        
        mouse_group.setLayout(mouse_layout)
        layout.addWidget(mouse_group)
        
        # Keyboard shortcuts group
        keyboard_group = QGroupBox("⌨️ Keyboard Shortcuts")
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
        
        layout.addStretch()
        tab.setLayout(layout)
        return tab
        
    def get_settings(self):
        """Get current settings"""
        return {
            'highlight_hover': self.highlight_hover_checkbox.isChecked()
        }
