# Steel Tide: Voxel Asset Studio
# rigging_panel.py - Selection-based bone/joint rigging (Phase 2)
#
# Workflow (approach A, fully explicit):
#   1. Box-select the pelvis region -> "Set Pelvis (Root)".
#   2. Box-select a bone region -> pick role/side/parent -> "Add Bone".
#      The bone's endpoints come from the selection's solid voxels (principal
#      axis), and its physics mass = sum of its voxels' material masses.
#   3. Joints are auto-derived where bones meet (a bone's distal joint is shared
#      by its children). Select a joint to edit its type + angle limits.
#   4. The pelvis-rooted tree is rebuilt and validated against the same
#      invariants as the T-pose verifier, then pushed to skeleton_data so the
#      existing Save path exports a v2 .stasset.

import numpy as np
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QGroupBox, QLabel, QPushButton,
    QComboBox, QLineEdit, QListWidget, QListWidgetItem, QDoubleSpinBox,
    QFormLayout, QMessageBox,
)
from PyQt6.QtCore import Qt, pyqtSignal

import rig_roles as RR
from material_library import get_material_mass, material_mass_table


class RiggingPanel(QWidget):
    """Right-sidebar panel for authoring a pelvis-rooted rig from selections."""

    # Emitted whenever the rig changes; payload is a skeleton dict or None.
    skeleton_changed = pyqtSignal(object)

    def __init__(self, get_selection, get_voxels, paint_voxels=None, parent=None):
        super().__init__(parent)
        self._get_selection = get_selection      # () -> SelectionBox
        self._get_voxels = get_voxels            # () -> np.ndarray | None
        self._paint_voxels = paint_voxels        # (coords, material_id) -> None  (optional)

        # Rig state (single source of truth; joints are DERIVED from this).
        self.bones = []          # list of dicts (see _make_bone)
        self.root_pos = None     # [x, y, z] pelvis anchor
        self.joint_config = {}   # 'root' | bone_id -> joint config dict
        self._next_id = 0

        # Build cache for the joint list/editor.
        self._last_skeleton = None
        self._joint_key_by_jid = {}   # joint id -> config key ('root' | bone_id)

        self._init_ui()

    # ------------------------------------------------------------------ UI
    def _init_ui(self):
        layout = QVBoxLayout()
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)

        title = QLabel("\U0001f9b4 Rigging")
        title.setStyleSheet("font-size: 15px; font-weight: bold; padding: 4px;")
        layout.addWidget(title)

        # --- Root (pelvis) ---
        root_group = QGroupBox("Root (Pelvis)")
        root_layout = QVBoxLayout()
        self.root_label = QLabel("Not set")
        self.root_label.setStyleSheet("color: #aaa;")
        root_layout.addWidget(self.root_label)
        set_root_btn = QPushButton("Set Pelvis (Root) from Selection")
        set_root_btn.clicked.connect(self._set_root_from_selection)
        root_layout.addWidget(set_root_btn)
        root_group.setLayout(root_layout)
        layout.addWidget(root_group)

        # --- Add bone ---
        add_group = QGroupBox("Add Bone from Selection")
        form = QFormLayout()

        self.name_edit = QLineEdit()
        self.name_edit.setPlaceholderText("auto")
        form.addRow("Name:", self.name_edit)

        self.role_combo = QComboBox()
        self.role_combo.addItems(RR.ROLES)
        self.role_combo.currentTextChanged.connect(self._sync_autoname)
        form.addRow("Role:", self.role_combo)

        self.side_combo = QComboBox()
        self.side_combo.addItems(RR.SIDES)
        self.side_combo.currentTextChanged.connect(self._sync_autoname)
        form.addRow("Side:", self.side_combo)

        self.parent_combo = QComboBox()
        form.addRow("Parent:", self.parent_combo)

        add_group.setLayout(form)
        add_btn = QPushButton("Add Bone from Selection")
        add_btn.clicked.connect(self._add_bone_from_selection)
        form.addRow(add_btn)
        layout.addWidget(add_group)

        # --- Bones list ---
        bones_group = QGroupBox("Bones")
        bones_layout = QVBoxLayout()
        self.bone_list = QListWidget()
        self.bone_list.setMaximumHeight(120)
        bones_layout.addWidget(self.bone_list)
        remove_btn = QPushButton("Remove Selected Bone")
        remove_btn.clicked.connect(self._remove_selected_bone)
        bones_layout.addWidget(remove_btn)
        bones_group.setLayout(bones_layout)
        layout.addWidget(bones_group)

        # --- Joints list + editor ---
        joints_group = QGroupBox("Joints (auto)")
        joints_layout = QVBoxLayout()
        self.joint_list = QListWidget()
        self.joint_list.setMaximumHeight(110)
        self.joint_list.currentRowChanged.connect(self._on_joint_selected)
        joints_layout.addWidget(self.joint_list)

        jform = QFormLayout()
        self.jtype_combo = QComboBox()
        self.jtype_combo.addItems(RR.JOINT_TYPES)
        self.jtype_combo.currentTextChanged.connect(self._sync_joint_fields)
        jform.addRow("Type:", self.jtype_combo)

        self.jrole_edit = QLineEdit()
        self.jrole_edit.setPlaceholderText("e.g. knee, elbow")
        jform.addRow("Label:", self.jrole_edit)

        self.jaxis_combo = QComboBox()
        self.jaxis_combo.addItems(RR.HINGE_AXES)
        self.jaxis_row = self._form_row(jform, "Axis:", self.jaxis_combo)

        self.jmin_spin = self._angle_spin(-180, 180, -90)
        self.jmin_row = self._form_row(jform, "Min \u00b0:", self.jmin_spin)
        self.jmax_spin = self._angle_spin(-180, 180, 90)
        self.jmax_row = self._form_row(jform, "Max \u00b0:", self.jmax_spin)

        self.jx_spin = self._angle_spin(0, 180, 45)
        self.jx_row = self._form_row(jform, "Max X\u00b0:", self.jx_spin)
        self.jy_spin = self._angle_spin(0, 180, 45)
        self.jy_row = self._form_row(jform, "Max Y\u00b0:", self.jy_spin)
        self.jz_spin = self._angle_spin(0, 180, 45)
        self.jz_row = self._form_row(jform, "Max Z\u00b0:", self.jz_spin)

        joints_layout.addLayout(jform)
        apply_joint_btn = QPushButton("Apply to Joint")
        apply_joint_btn.clicked.connect(self._apply_joint)
        joints_layout.addWidget(apply_joint_btn)
        joints_group.setLayout(joints_layout)
        layout.addWidget(joints_group)

        # --- Actions ---
        actions = QHBoxLayout()
        rebuild_btn = QPushButton("Rebuild + Preview")
        rebuild_btn.clicked.connect(self._rebuild)
        actions.addWidget(rebuild_btn)
        validate_btn = QPushButton("Validate")
        validate_btn.clicked.connect(self._validate_clicked)
        actions.addWidget(validate_btn)
        layout.addLayout(actions)

        actions2 = QHBoxLayout()
        recalc_btn = QPushButton("Recalc Masses")
        recalc_btn.clicked.connect(self._recalc_masses)
        actions2.addWidget(recalc_btn)
        clear_btn = QPushButton("Clear Rig")
        clear_btn.clicked.connect(self._clear_rig)
        actions2.addWidget(clear_btn)
        layout.addLayout(actions2)

        self.status_label = QLabel("")
        self.status_label.setWordWrap(True)
        self.status_label.setStyleSheet("color: #9c9; font-size: 11px;")
        layout.addWidget(self.status_label)

        self.setLayout(layout)
        self.setMaximumWidth(290)
        self._refresh_parent_combo()
        self._sync_joint_fields()

    def _form_row(self, form, label, widget):
        """Add a labelled row and return (label_widget, field_widget) for hiding."""
        lbl = QLabel(label)
        form.addRow(lbl, widget)
        return (lbl, widget)

    def _angle_spin(self, lo, hi, val):
        s = QDoubleSpinBox()
        s.setRange(lo, hi)
        s.setDecimals(1)
        s.setSingleStep(5.0)
        s.setValue(val)
        return s

    # -------------------------------------------------------------- helpers
    def _set_status(self, text, ok=True):
        self.status_label.setStyleSheet(
            "font-size: 11px; color: %s;" % ("#9c9" if ok else "#e88")
        )
        self.status_label.setText(text)

    def _sync_autoname(self):
        self.name_edit.setPlaceholderText(
            RR.make_bone_name(self.role_combo.currentText(), self.side_combo.currentText())
        )

    def _selection_geometry(self):
        """Return (start, end, voxels, mass) from the current selection, or None."""
        sel = self._get_selection()
        bounds = sel.get_bounds() if sel else None
        if not bounds:
            self._set_status("Make a box selection first (Select tool).", ok=False)
            return None
        voxels = self._get_voxels()
        if voxels is None:
            self._set_status("No model loaded.", ok=False)
            return None

        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        sx, sy, sz = voxels.shape
        # Use explicit voxel list (magic wand) if available, else scan bounding box
        sel = self._get_selection()
        if sel.voxel_list is not None:
            coords = [(x, y, z) for (x, y, z) in sel.voxel_list
                      if 0 <= x < sx and 0 <= y < sy and 0 <= z < sz
                      and voxels[x, y, z] != 0]
        else:
            coords = []
            for x in range(max(0, min_x), min(sx, max_x + 1)):
                for y in range(max(0, min_y), min(sy, max_y + 1)):
                    for z in range(max(0, min_z), min(sz, max_z + 1)):
                        if voxels[x, y, z] != 0:
                            coords.append((x, y, z))
        if not coords:
            self._set_status("Selection has no solid voxels.", ok=False)
            return None

        arr = np.array(coords, dtype=float)
        mins = arr.min(axis=0)
        maxs = arr.max(axis=0)
        center = arr.mean(axis=0)
        axis = int(np.argmax(maxs - mins))   # principal (longest) axis

        start = center.copy()
        end = center.copy()
        start[axis] = mins[axis]
        end[axis] = maxs[axis]
        start = [int(round(v)) for v in start]
        end = [int(round(v)) for v in end]

        mass = float(sum(get_material_mass(int(voxels[x, y, z])) for (x, y, z) in coords))
        bounds_min = [int(v) for v in mins]
        bounds_max = [int(v) for v in maxs]
        return start, end, coords, mass, bounds_min, bounds_max

    # ----------------------------------------------------------- rig edits
    def _paint_coords(self, coords, material_id):
        """Paint the given voxel coords with material_id in the live voxel array."""
        if self._paint_voxels is None:
            return
        voxels = self._get_voxels()
        if voxels is None:
            return
        for (x, y, z) in coords:
            if 0 <= x < voxels.shape[0] and 0 <= y < voxels.shape[1] and 0 <= z < voxels.shape[2]:
                voxels[x, y, z] = material_id
        self._paint_voxels(coords, material_id)

    JOINT_MATERIAL = 21
    BONE_MATERIAL = 12

    def _set_root_from_selection(self):
        geo = self._selection_geometry()
        if geo is None:
            return
        _, _, coords, _, bmin, bmax = geo
        arr = np.array(coords, dtype=float)
        center = [int(round(v)) for v in arr.mean(axis=0)]
        self.root_pos = center
        self.root_bounds = (bmin, bmax)
        self.joint_config.setdefault("root", RR.default_joint_config("pelvis"))
        self._paint_coords(coords, self.JOINT_MATERIAL)
        self.root_label.setText(f"@ ({center[0]}, {center[1]}, {center[2]})")
        self.root_label.setStyleSheet("color: #9c9;")
        self._set_status("Pelvis root set.")
        self._rebuild()

    def _add_bone_from_selection(self):
        if self.root_pos is None:
            self._set_status("Set the pelvis root first.", ok=False)
            return
        geo = self._selection_geometry()
        if geo is None:
            return
        start, end, coords, mass, bmin, bmax = geo

        role = self.role_combo.currentText()
        side = self.side_combo.currentText()
        name = self.name_edit.text().strip() or RR.make_bone_name(role, side)
        parent_id = self.parent_combo.currentData()  # None for root

        bone = {
            "id": self._next_id,
            "name": name,
            "role": role,
            "side": side,
            "start": start,
            "end": end,
            "parent": parent_id,
            "mass": mass,
            "voxels": coords,
            "voxel_bounds_min": bmin,
            "voxel_bounds_max": bmax,
        }
        self.bones.append(bone)
        # Paint the selection's voxels with Bone material so the visual matches the rig.
        self._paint_coords(coords, self.BONE_MATERIAL)
        # Pre-seed the distal joint config so future children get sane defaults.
        self.joint_config.setdefault(self._next_id, RR.default_joint_config(role))
        self._next_id += 1

        self.name_edit.clear()
        self._set_status(f"Added '{name}' ({len(coords)} vox, mass {mass:.1f}).")
        self._refresh_parent_combo()
        self._rebuild()

    def _remove_selected_bone(self):
        row = self.bone_list.currentRow()
        if row < 0 or row >= len(self.bones):
            return
        removed = self.bones[row]
        rid = removed["id"]
        # Re-parent orphans to the removed bone's parent (keep the tree intact).
        for b in self.bones:
            if b["parent"] == rid:
                b["parent"] = removed["parent"]
        self.bones.pop(row)
        self.joint_config.pop(rid, None)
        self._set_status(f"Removed '{removed['name']}'.")
        self._refresh_parent_combo()
        self._rebuild()

    def _recalc_masses(self):
        voxels = self._get_voxels()
        if voxels is None:
            self._set_status("No model loaded.", ok=False)
            return
        for b in self.bones:
            b["mass"] = float(sum(get_material_mass(int(voxels[x, y, z]))
                                  for (x, y, z) in b["voxels"]
                                  if voxels[x, y, z] != 0))
        self._set_status("Recalculated masses from current material densities.")
        self._rebuild()

    def _clear_rig(self):
        self.bones = []
        self.root_pos = None
        self.root_bounds = None
        self.joint_config = {}
        self._next_id = 0
        self._last_skeleton = None
        self.root_label.setText("Not set")
        self.root_label.setStyleSheet("color: #aaa;")
        self.bone_list.clear()
        self.joint_list.clear()
        self._refresh_parent_combo()
        self.skeleton_changed.emit(None)
        self._set_status("Rig cleared.")

    # ------------------------------------------------------------- build
    def build_skeleton(self):
        """Derive joints + the pelvis-rooted tree from bones. Returns (dict, issues)."""
        if self.root_pos is None or not self.bones:
            return None, ["Set a pelvis root and add at least one bone."]

        children = {}
        for b in self.bones:
            children.setdefault(b["parent"], []).append(b["id"])

        joints = []
        self._joint_key_by_jid = {}
        jid = 0

        root_cfg = self.joint_config.get("root", RR.default_joint_config("pelvis"))
        root_joint = {
            "id": jid, "name": root_cfg.get("role") or "pelvis",
            "type": "ROOT", "position": list(self.root_pos),
        }
        if self.root_bounds:
            root_joint["voxel_bounds_min"] = list(self.root_bounds[0])
            root_joint["voxel_bounds_max"] = list(self.root_bounds[1])
        joints.append(root_joint)
        self._joint_key_by_jid[jid] = "root"
        root_joint_id = jid
        jid += 1

        # Distal joint for each bone that has children.
        end_joint_id = {}
        for b in self.bones:
            if children.get(b["id"]):
                cfg = self.joint_config.get(b["id"], RR.default_joint_config(b["role"]))
                j = {"id": jid, "name": cfg.get("role") or f"{b['name']}_joint",
                     "position": list(b["end"])}
                if "voxel_bounds_min" in b:
                    j["voxel_bounds_min"] = list(b["voxel_bounds_min"])
                    j["voxel_bounds_max"] = list(b["voxel_bounds_max"])
                j.update(_joint_payload(cfg))
                joints.append(j)
                end_joint_id[b["id"]] = jid
                self._joint_key_by_jid[jid] = b["id"]
                jid += 1

        index_of = {b["id"]: i for i, b in enumerate(self.bones)}
        out_bones = []
        for b in self.bones:
            parent_joint = root_joint_id if b["parent"] is None else end_joint_id.get(b["parent"])
            child_joint = end_joint_id.get(b["id"])
            out_bone = {
                "id": index_of[b["id"]],
                "name": b["name"],
                "role": b["role"],
                "side": b["side"],
                "start": list(b["start"]),
                "end": list(b["end"]),
                "length": float(np.linalg.norm(np.array(b["end"], float) - np.array(b["start"], float))),
                "parent_joint": parent_joint,
                "child_joint": child_joint,
                "mass": float(b["mass"]),
            }
            if "voxel_bounds_min" in b:
                out_bone["voxel_bounds_min"] = list(b["voxel_bounds_min"])
                out_bone["voxel_bounds_max"] = list(b["voxel_bounds_max"])
            out_bones.append(out_bone)

        influence = {}
        for b in self.bones:
            for (x, y, z) in b["voxels"]:
                influence[f"{x},{y},{z}"] = index_of[b["id"]]

        skeleton = {
            "root_joint": root_joint_id,
            "bones": out_bones,
            "joints": joints,
            "influence_map": influence,
            "attachments": [],
            "materials": material_mass_table(),
        }
        return skeleton, self._validate(skeleton)

    def _rebuild(self):
        skeleton, issues = self.build_skeleton()
        self._last_skeleton = skeleton
        self._refresh_bone_list()
        self._refresh_joint_list()
        self.skeleton_changed.emit(skeleton)
        if skeleton is None:
            return
        if issues:
            self._set_status("\u26a0 " + "; ".join(issues), ok=False)
        else:
            self._set_status(
                f"\u2713 Rig OK: {len(skeleton['bones'])} bones, {len(skeleton['joints'])} joints."
            )

    def _validate(self, skeleton):
        """Trimmed copy of the T-pose roundtrip invariants. Returns list of issues."""
        issues = []
        bones = skeleton["bones"]
        joints = skeleton["joints"]
        root_joint = skeleton["root_joint"]
        joint_ids = {j["id"] for j in joints}
        if root_joint not in joint_ids:
            issues.append("root_joint missing")

        child_to_bone = {}
        for b in bones:
            cj = b["child_joint"]
            if cj is None:
                continue
            if cj in child_to_bone:
                issues.append(f"joint {cj} is child of multiple bones")
            child_to_bone[cj] = b

        root_bones = [b for b in bones if b["parent_joint"] == root_joint]
        if not root_bones:
            issues.append("no root-attached bone")
        for b in bones:
            if b["parent_joint"] == root_joint:
                continue
            if b["parent_joint"] not in child_to_bone:
                issues.append(f"'{b['name']}' has no parent bone")

        # Connectivity from the root.
        by_parent_joint = {}
        for b in bones:
            by_parent_joint.setdefault(b["parent_joint"], []).append(b)
        # Also build parent_bone connectivity for collider-only bones
        bone_by_name = {b["name"]: b for b in bones}
        reached = set(b["id"] for b in root_bones)
        frontier = list(reached)
        while frontier:
            nxt = []
            for bid in frontier:
                bone = bones[bid]
                # Traverse via child_joint (legacy)
                cj = bone["child_joint"]
                if cj is not None:
                    for child in by_parent_joint.get(cj, []):
                        if child["id"] not in reached:
                            reached.add(child["id"])
                            nxt.append(child["id"])
                # Traverse via parent_bone (collider-only children)
                for child in bones:
                    if child["id"] not in reached and child.get("parent_bone") == bone["name"]:
                        reached.add(child["id"])
                        nxt.append(child["id"])
            frontier = nxt
        # Collider-only bones with valid parent_bone are OK even if unreached by joint traversal
        unreached = [b for b in bones if b["id"] not in reached]
        truly_orphaned = []
        for b in unreached:
            if b.get("collider_only") and b.get("parent_bone") and b["parent_bone"] in bone_by_name:
                continue  # Valid — connected via parent_bone
            truly_orphaned.append(b["name"])
        if truly_orphaned:
            issues.append("unreached: " + ", ".join(truly_orphaned))
        return issues

    def _validate_clicked(self):
        skeleton, issues = self.build_skeleton()
        if skeleton is None:
            self._set_status("; ".join(issues), ok=False)
        elif issues:
            QMessageBox.warning(self, "Rig Validation", "Issues found:\n- " + "\n- ".join(issues))
            self._set_status("\u26a0 validation failed", ok=False)
        else:
            QMessageBox.information(self, "Rig Validation",
                                    f"Valid pelvis-rooted tree.\n"
                                    f"{len(skeleton['bones'])} bones, {len(skeleton['joints'])} joints.")
            self._set_status("\u2713 validation passed")

    # ------------------------------------------------------------- lists
    def _refresh_parent_combo(self):
        current = self.parent_combo.currentData() if self.parent_combo.count() else None
        self.parent_combo.blockSignals(True)
        self.parent_combo.clear()
        self.parent_combo.addItem("(root / pelvis)", None)
        for b in self.bones:
            self.parent_combo.addItem(b["name"], b["id"])
        # Try to keep the previous selection.
        idx = self.parent_combo.findData(current)
        self.parent_combo.setCurrentIndex(idx if idx >= 0 else 0)
        self.parent_combo.blockSignals(False)

    def _refresh_bone_list(self):
        self.bone_list.clear()
        for b in self.bones:
            parent_name = "root"
            if b["parent"] is not None:
                pj = next((x for x in self.bones if x["id"] == b["parent"]), None)
                parent_name = pj["name"] if pj else "?"
            item = QListWidgetItem(f"{b['name']}  [{b['role']}]  m={b['mass']:.1f}  \u2190 {parent_name}" +
                                   ("  \U0001f535collider-only" if b.get("collider_only") else ""))
            self.bone_list.addItem(item)

    def _refresh_joint_list(self):
        self.joint_list.blockSignals(True)
        self.joint_list.clear()
        if self._last_skeleton:
            for j in self._last_skeleton["joints"]:
                p = j["position"]
                item = QListWidgetItem(f"{j['name']}  [{j['type']}]  @({p[0]},{p[1]},{p[2]})")
                item.setData(Qt.ItemDataRole.UserRole, j["id"])
                self.joint_list.addItem(item)
        self.joint_list.blockSignals(False)

    # ------------------------------------------------------------- joints
    def _sync_joint_fields(self):
        t = self.jtype_combo.currentText()
        is_hinge = (t == "HINGE")
        is_ball = (t == "BALL")
        for lbl, w in (self.jaxis_row, self.jmin_row, self.jmax_row):
            lbl.setVisible(is_hinge)
            w.setVisible(is_hinge)
        for lbl, w in (self.jx_row, self.jy_row, self.jz_row):
            lbl.setVisible(is_ball)
            w.setVisible(is_ball)

    def _on_joint_selected(self, row):
        if row < 0:
            return
        item = self.joint_list.item(row)
        if item is None:
            return
        jid = item.data(Qt.ItemDataRole.UserRole)
        key = self._joint_key_by_jid.get(jid)
        if key is None:
            return
        cfg = self.joint_config.get(key, {"type": "BALL"})
        self.jtype_combo.setCurrentText(cfg.get("type", "BALL"))
        self.jrole_edit.setText(cfg.get("role", ""))
        self.jaxis_combo.setCurrentText(cfg.get("axis", "X"))
        self.jmin_spin.setValue(cfg.get("min_angle", -90.0))
        self.jmax_spin.setValue(cfg.get("max_angle", 90.0))
        self.jx_spin.setValue(cfg.get("max_angle_x", 45.0))
        self.jy_spin.setValue(cfg.get("max_angle_y", 45.0))
        self.jz_spin.setValue(cfg.get("max_angle_z", 45.0))
        self._sync_joint_fields()

    def _apply_joint(self):
        row = self.joint_list.currentRow()
        if row < 0:
            self._set_status("Select a joint first.", ok=False)
            return
        item = self.joint_list.item(row)
        jid = item.data(Qt.ItemDataRole.UserRole)
        key = self._joint_key_by_jid.get(jid)
        if key is None:
            return
        t = self.jtype_combo.currentText()
        cfg = {"type": t, "role": self.jrole_edit.text().strip()}
        if t == "HINGE":
            cfg["axis"] = self.jaxis_combo.currentText()
            cfg["min_angle"] = self.jmin_spin.value()
            cfg["max_angle"] = self.jmax_spin.value()
        elif t == "BALL":
            cfg["max_angle_x"] = self.jx_spin.value()
            cfg["max_angle_y"] = self.jy_spin.value()
            cfg["max_angle_z"] = self.jz_spin.value()
        self.joint_config[key] = cfg
        self._set_status("Joint updated.")
        self._rebuild()


def _joint_payload(cfg):
    """Type-specific joint fields matching the .stasset generator format."""
    t = cfg.get("type", "BALL")
    if t == "HINGE":
        return {"type": "HINGE", "axis": cfg.get("axis", "X"),
                "min_angle": float(cfg.get("min_angle", -90.0)),
                "max_angle": float(cfg.get("max_angle", 90.0))}
    if t == "BALL":
        return {"type": "BALL",
                "max_angle_x": float(cfg.get("max_angle_x", 45.0)),
                "max_angle_y": float(cfg.get("max_angle_y", 45.0)),
                "max_angle_z": float(cfg.get("max_angle_z", 45.0))}
    if t == "ROOT":
        return {"type": "ROOT"}
    return {"type": "FIXED"}
