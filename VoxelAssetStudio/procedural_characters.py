"""Deprecated character generators.

The humanoid/mech pipelines caused inconsistent voxel ordering in Unity, so they were
removed in favor of the proven armored cube workflow.  This stub intentionally raises
to make any lingering imports obvious during development.
"""

raise ImportError(
    "procedural_characters has been removed. Use generate_armored_cube() / character block"
)
