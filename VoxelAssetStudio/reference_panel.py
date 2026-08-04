# Steel Tide: Voxel Asset Studio
# reference_panel.py - UI panel for scale reference controls

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QLabel, QCheckBox, 
                             QGroupBox, QScrollArea)
from PyQt6.QtCore import pyqtSignal, Qt
from reference_models import get_scale_info_text


class ReferenceModelPanel(QWidget):
    """Panel to control reference model visibility"""
    
    reference_toggled = pyqtSignal(str, bool)  # (model_name, visible)
    
    def __init__(self, library, parent=None):
        super().__init__(parent)
        self.library = library
        self.checkboxes = {}  # name -> QCheckBox
        self.init_ui()
    
    def init_ui(self):
        """Build the UI"""
        layout = QVBoxLayout()
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        
        # Title
        title = QLabel("📏 Scale References")
        title.setStyleSheet("""
            QLabel {
                font-size: 16px;
                font-weight: bold;
                padding: 10px;
                background-color: #2a2a2a;
                border-radius: 4px;
            }
        """)
        layout.addWidget(title)
        
        # Scale info card
        info_label = QLabel(get_scale_info_text())
        info_label.setStyleSheet("""
            QLabel {
                background-color: #1a1a1a;
                color: #00ff00;
                padding: 12px;
                border-radius: 4px;
                border: 1px solid #333;
                font-family: 'Consolas', 'Courier New', monospace;
                font-size: 11px;
            }
        """)
        info_label.setWordWrap(True)
        layout.addWidget(info_label)
        
        # Reference models group
        models_group = QGroupBox("Reference Models")
        models_layout = QVBoxLayout()
        
        # Create checkbox for each model
        for model in self.library.models:
            checkbox = QCheckBox(f"{model.icon} {model.name}")
            checkbox.setChecked(model.visible)
            checkbox.setStyleSheet("""
                QCheckBox {
                    font-size: 13px;
                    padding: 6px;
                }
                QCheckBox:hover {
                    background-color: #2a2a2a;
                    border-radius: 4px;
                }
            """)
            
            # Add tooltip with dimensions
            checkbox.setToolTip(model.get_info_text())
            
            # Connect signal
            checkbox.stateChanged.connect(
                lambda state, name=model.name: 
                self.reference_toggled.emit(name, state == Qt.CheckState.Checked.value)
            )
            
            models_layout.addWidget(checkbox)
            self.checkboxes[model.name] = checkbox
        
        models_group.setLayout(models_layout)
        layout.addWidget(models_group)
        
        # Instructions
        instructions = QLabel(
            "💡 Tip: Reference models appear on the left side of the viewport. "
            "They are semi-transparent and won't interfere with your work."
        )
        instructions.setWordWrap(True)
        instructions.setStyleSheet("""
            QLabel {
                background-color: #1a3a4a;
                color: #aaddff;
                padding: 10px;
                border-radius: 4px;
                font-size: 11px;
                margin-top: 10px;
            }
        """)
        layout.addWidget(instructions)
        
        # Add stretch to push everything to top
        layout.addStretch()
        
        self.setLayout(layout)
        self.setMaximumWidth(280)
    
    def update_checkbox(self, name, checked):
        """Update checkbox state (called when model visibility changes externally)"""
        if name in self.checkboxes:
            self.checkboxes[name].setChecked(checked)


if __name__ == "__main__":
    # Test the panel
    from PyQt6.QtWidgets import QApplication
    from reference_models import ReferenceModelLibrary
    import sys
    
    app = QApplication(sys.argv)
    
    # Create library
    library = ReferenceModelLibrary()
    
    # Create panel
    panel = ReferenceModelPanel(library)
    panel.setWindowTitle("Reference Model Panel Test")
    panel.show()
    
    # Connect signal for testing
    def on_toggle(name, visible):
        print(f"{'✅' if visible else '❌'} {name}: {'Visible' if visible else 'Hidden'}")
    
    panel.reference_toggled.connect(on_toggle)
    
    sys.exit(app.exec())
