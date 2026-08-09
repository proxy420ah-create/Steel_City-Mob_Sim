# Steel Tide: Voxel Asset Studio
# material_editor_dialog.py - Edit per-material physics properties (mass/density)
#
# Phase 1 of the rigging/mass pipeline: lets the user assign a relative mass
# (density per voxel) to each material. A rigged bone's physics mass is the sum
# of its voxels' material masses, so tuning these values changes how heavy a
# limb made of steel vs bone feels in the Unity ragdoll. Values persist to
# material_properties.json via material_library.save_material_properties().

from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QPushButton, QScrollArea,
    QWidget, QGridLayout, QDoubleSpinBox, QFrame, QMessageBox,
)
from PyQt6.QtGui import QColor
from PyQt6.QtCore import Qt

import material_library as ML


class MaterialEditorDialog(QDialog):
    """Dialog for editing per-material mass/density used by the physics pipeline."""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Material Properties")
        self.setModal(True)
        self.resize(460, 620)

        self._spins = {}  # material_id -> QDoubleSpinBox
        self._init_ui()

    def _init_ui(self):
        layout = QVBoxLayout()

        title = QLabel("\U0001f9ea Material Properties")
        title.setStyleSheet("font-size: 16px; font-weight: bold; padding: 6px;")
        layout.addWidget(title)

        hint = QLabel(
            "<i>Mass = relative density per voxel. A rigged bone's physics mass "
            "is the sum of its voxels' material masses.</i>"
        )
        hint.setWordWrap(True)
        hint.setStyleSheet("color: #999; padding: 0 6px 6px 6px;")
        layout.addWidget(hint)

        # Column headers
        header = QHBoxLayout()
        h_color = QLabel("")
        h_color.setFixedWidth(26)
        header.addWidget(h_color)
        h_name = QLabel("Material")
        h_name.setStyleSheet("font-weight: bold;")
        header.addWidget(h_name, stretch=1)
        h_mass = QLabel("Mass / voxel")
        h_mass.setStyleSheet("font-weight: bold;")
        h_mass.setFixedWidth(120)
        header.addWidget(h_mass)
        layout.addLayout(header)

        # Scrollable grid of materials
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        container = QWidget()
        grid = QGridLayout(container)
        grid.setContentsMargins(4, 4, 4, 4)
        grid.setVerticalSpacing(4)

        for row, mid in enumerate(sorted(ML.MATERIALS.keys())):
            # Color swatch
            swatch = QFrame()
            swatch.setFixedSize(20, 20)
            r, g, b, a = ML.get_material_color_255(mid)
            swatch.setStyleSheet(
                f"background-color: rgba({r},{g},{b},{a}); border: 1px solid #555;"
            )
            grid.addWidget(swatch, row, 0)

            # Name + id
            name = QLabel(f"{ML.get_material_name(mid)}  ({mid})")
            grid.addWidget(name, row, 1)

            # Mass spinner
            spin = QDoubleSpinBox()
            spin.setDecimals(2)
            spin.setRange(0.0, 1000.0)
            spin.setSingleStep(0.1)
            spin.setValue(ML.get_material_mass(mid))
            spin.setFixedWidth(110)
            self._spins[mid] = spin
            grid.addWidget(spin, row, 2)

        scroll.setWidget(container)
        layout.addWidget(scroll, stretch=1)

        # Buttons
        buttons = QHBoxLayout()
        reset_btn = QPushButton("Reset to Defaults")
        reset_btn.clicked.connect(self._reset_defaults)
        buttons.addWidget(reset_btn)

        buttons.addStretch()

        save_btn = QPushButton("Save")
        save_btn.setDefault(True)
        save_btn.clicked.connect(self._save)
        buttons.addWidget(save_btn)

        cancel_btn = QPushButton("Cancel")
        cancel_btn.clicked.connect(self.reject)
        buttons.addWidget(cancel_btn)

        layout.addLayout(buttons)
        self.setLayout(layout)

    def _reset_defaults(self):
        """Reset the spinners to the built-in default masses (not yet saved)."""
        for mid, spin in self._spins.items():
            spin.setValue(ML.DEFAULT_MATERIAL_MASS.get(mid, 1.0))

    def _save(self):
        """Apply the spinner values to the material library and persist them."""
        for mid, spin in self._spins.items():
            ML.set_material_mass(mid, spin.value())
        if ML.save_material_properties():
            self.accept()
        else:
            QMessageBox.critical(
                self, "Error",
                "Failed to save material properties. See console for details.",
            )
