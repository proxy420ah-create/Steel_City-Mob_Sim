# Steel Tide: Voxel Asset Studio
# rig_roles.py - Shared bone/joint role taxonomy
#
# Roles are SEMANTIC tags carried in skeleton metadata (not materials). The
# ragdoll ignores them; the future procedural animator drives bones BY role
# (e.g. "swing every 'thigh' forward"). Keep this list the single source of
# truth so the editor and the animator agree.

# Bone roles, covering biped and quadruped rigs. 'side' (below) disambiguates
# left/right/front/back, so roles themselves stay side-agnostic.
BIPED_ROLES = [
    "pelvis", "spine", "chest", "neck", "head",
    "clavicle", "upper_arm", "forearm", "hand",
    "hip", "thigh", "shin", "foot",
]
QUAD_ROLES = [
    "pelvis", "spine", "chest", "neck", "head", "tail",
    "hip", "thigh", "shin", "foot",
]

# Ordered, de-duplicated union + a free-text escape hatch.
ROLES = [
    "pelvis", "spine", "chest", "neck", "head", "tail",
    "clavicle", "upper_arm", "forearm", "hand",
    "hip", "thigh", "shin", "foot",
    "custom",
]

# Side qualifiers. 'center' bones (spine/head/tail) generate names without a
# side prefix; limbs use left/right (biped) or front_*/back_* (quad).
SIDES = [
    "center", "left", "right",
    "front_left", "front_right", "back_left", "back_right",
]

# Joint TYPES understood by the Unity skeleton/ragdoll pipeline.
JOINT_TYPES = ["BALL", "HINGE", "FIXED", "ROOT"]
HINGE_AXES = ["X", "Y", "Z"]

# Default articulation for the DISTAL joint owned by a bone (the joint at the
# bone's far end that articulates its children). Keyed by the OWNING bone's
# role. Example: a 'thigh' bone owns the knee (HINGE) at its end.
DEFAULT_JOINT_TYPE_BY_ROLE = {
    "pelvis": "ROOT",
    "spine": "BALL",
    "chest": "BALL",
    "neck": "BALL",
    "clavicle": "BALL",
    "hip": "BALL",
    "upper_arm": "HINGE",
    "forearm": "HINGE",
    "thigh": "HINGE",
    "shin": "HINGE",
}


def default_joint_config(owner_role):
    """Return a fresh joint config dict for the distal joint owned by a bone."""
    jtype = DEFAULT_JOINT_TYPE_BY_ROLE.get(owner_role, "BALL")
    return _joint_config_for_type(jtype)


def _joint_config_for_type(jtype):
    """Build a joint config dict with sensible defaults for the given type."""
    if jtype == "HINGE":
        return {"type": "HINGE", "axis": "X", "min_angle": -90.0, "max_angle": 90.0, "role": ""}
    if jtype == "BALL":
        return {"type": "BALL", "max_angle_x": 45.0, "max_angle_y": 45.0, "max_angle_z": 45.0, "role": ""}
    if jtype == "ROOT":
        return {"type": "ROOT", "role": "pelvis"}
    return {"type": "FIXED", "role": ""}


def make_bone_name(role, side):
    """Auto-generate a bone name from role + side (e.g. left_thigh, spine)."""
    if side and side != "center":
        return f"{side}_{role}"
    return role
