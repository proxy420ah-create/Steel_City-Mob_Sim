# _doc_sweep.py — one-shot stale-scale sweep (2026-11-12)
# Corrects docs/comments that still quote the pre-change city metrics:
#   live truth: voxelSize .05 forced (buildings+terrain), roadWidth 4.5f uniform
#   → groundTile 11.6m (232v), block spacing 16.1m, terrain chunk 322x2x322.
# The editor's per-seam spec values (road 1.6, alley 1.8, mainstreet 4.5, spacing
# 13.2) stay — they're design intent pending pipeline task 1 — but every claim
# that they MATCH the live Unity grid is annotated or fixed.
import codecs, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EDITS = [
    # ---- code comments ------------------------------------------------------
    ('Assets/Scripts/UI/VoxelChunkManager.cs',
     '(buildings=0.1, characters=0.01)',
     '(buildings=0.05, characters=0.01)', True),
    ('Assets/Scripts/Sim/VoxelCharacter.cs',
     'Buildings use 0.1, characters use 0.01',
     'Buildings use 0.05, characters use 0.01', True),
    ('Assets/Scripts/UI/CityMap3D.cs',
     'terrain is 2 voxels thick at voxelSize=0.1 \u2192 top surface at Y=0.2',
     'terrain is 2 voxels thick at voxelSize=0.05 \u2192 top surface at Y=0.1', True),
    # ---- CITY_LAYOUT_PIPELINE.md -------------------------------------------
    ('docs/core/CITY_LAYOUT_PIPELINE.md',
     'same `groundTileSize` 11.6 m / `spacing` 13.2 m as this editor\'s export, so the grid dimensions line up.',
     'same `groundTileSize` 11.6 m as this editor\'s export; `spacing` is **16.1 m** live (`roadWidth` 4.5 \u2014 the editor-declared 1.6/13.2 predates the width increase and matches again only once pending task 1 consumes per-seam widths).', False),
    ('docs/core/CITY_LAYOUT_PIPELINE.md',
     '- **Road** \u2014 standard asphalt, 1.6m wide\n- **Waypoints**: Standard sidewalk corner + mid-edge nodes',
     '- **Road** \u2014 standard asphalt, editor spec 1.6m wide (Unity renders every seam at the uniform `roadWidth` 4.5m \u2014 task 1 below)\n- **Waypoints**: Standard sidewalk corner + mid-edge nodes', False),
    ('docs/core/CITY_LAYOUT_PIPELINE.md',
     '- **Width**: 4.5m (configurable, 2.0-8.0m range)\n- **Visual**: Wide dark asphalt',
     '- **Width**: 4.5m (editor spec \u2014 coincidentally the live uniform `roadWidth`; main streets currently render plain asphalt at this width, no cobble stripe)\n- **Visual**: Wide dark asphalt', False),
    ('docs/core/CITY_LAYOUT_PIPELINE.md',
     '- **Width**: Same as road (1.6m configurable)',
     '- **Width**: Same as road (editor spec 1.6m; live uniform 4.5m)', False),
    ('docs/core/CITY_LAYOUT_PIPELINE.md',
     '**Current state**: `CityMap3D.cs` uses a single `roadWidth` for all seams.',
     '**Current state**: `CityMap3D.cs` uses a single `roadWidth` (4.5f \u2192 16.1m spacing, 322\u00b2 terrain chunks) for all seams.', False),
    # ---- TERRAIN_GENERATION_DESIGN.md --------------------------------------
    ('docs/core/TERRAIN_GENERATION_DESIGN.md',
     '- Main Street seams are ~2.8x wider than standard roads (4.5m vs 1.6m)',
     '- Main Street seams are specified wider than standard roads \u2014 both currently render at the uniform `roadWidth` 4.5m (per-seam widths pending); main streets paint plain asphalt, no cobble stripe', False),
    ('docs/core/TERRAIN_GENERATION_DESIGN.md',
     '- **Road** \u2014 standard asphalt, 1.6m wide, normal pathing',
     '- **Road** \u2014 standard asphalt, editor spec 1.6m wide (live uniform `roadWidth` 4.5m), normal pathing', False),
    # ---- CITY_SCALE_ARCHITECTURE.md ----------------------------------------
    ('docs/systems/CITY_SCALE_ARCHITECTURE.md',
     '| Block spacing | 13.2 m | 13.2 m |',
     '| Block spacing | 16.1 m | 16.1 m |', False),
    ('docs/systems/CITY_SCALE_ARCHITECTURE.md',
     '| City extent | ~132 m | ~422 m (editor reports 420 m) |',
     '| City extent | ~161 m | ~515 m |', False),
    ('docs/systems/CITY_SCALE_ARCHITECTURE.md',
     '| Terrain voxels | 13,939,200 | 143,210,888 |',
     '| Terrain voxels | 20,736,800 | 212,344,832 |', False),
    ('docs/systems/CITY_SCALE_ARCHITECTURE.md',
     'Block spacing = `groundTile (64*3*0.05 + 2*1.0 = 11.6 m) + road (1.6 m)` = 13.2 m. Terrain chunk = 264 x 2 x 264 voxels (139,392).',
     'Block spacing = `groundTile (64*3*0.05 + 2*1.0 = 11.6 m) + road (4.5 m)` = 16.1 m. Terrain chunk = 322 x 2 x 322 voxels (207,368).', False),
    ('docs/systems/CITY_SCALE_ARCHITECTURE.md',
     '`groundTileSize: 11.6`, `spacing: 13.2` \u2014 these already match the Unity numbers above',
     '`groundTileSize: 11.6`, `spacing: 13.2` \u2014 groundTile matches; the editor\'s 13.2 spacing predates the roadWidth increase and does NOT match the live 16.1 grid until task 1 lands', False),
    ('docs/systems/CITY_SCALE_ARCHITECTURE.md',
     'If seam widths vary (alley 1.8, main street 4.5, bridge 1.6)',
     'If seam widths vary (editor spec: alley 1.8, main street 4.5, bridge 1.6 \u2014 today all render at the uniform 4.5)', False),
    # ---- RIVER_AND_BRIDGE_TILES.md ------------------------------------------
    ('docs/systems/RIVER_AND_BRIDGE_TILES.md',
     "a 'river' seam = 1.6 m canal in the road corridor",
     "a 'river' seam = a canal in the road corridor (corridor currently renders at the uniform `roadWidth` 4.5m; editor spec widths pending)", False),
    ('docs/systems/RIVER_AND_BRIDGE_TILES.md',
     '(13.2 m footprint, 264\u00d7264\u00d72 @ 0.05)',
     '(16.1 m footprint, 322\u00d7322\u00d72 @ 0.05)', False),
    ('docs/systems/RIVER_AND_BRIDGE_TILES.md',
     'at the same 13.2 m spacing',
     'at the same 16.1 m spacing', False),
    ('docs/systems/RIVER_AND_BRIDGE_TILES.md',
     '- dims **116 \u00d7 64 \u00d7 116**, `voxelSize 0.1` \u2192 11.6 m \u00d7 11.6 m block interior, 6.4 m total height',
     '- dims **116 \u00d7 64 \u00d7 116**, `voxelSize 0.1` \u2192 11.6 m \u00d7 11.6 m block interior, 6.4 m total height (file-declared .1 is authoring intent \u2014 Unity forces .05 on building renders, so a shipped chunk form must be re-grained to 232v per side)', False),
    # ---- DOCUMENTATION_INDEX.md ---------------------------------------------
    ('docs/core/DOCUMENTATION_INDEX.md',
     '(3\u00d73 slots + sidewalks = 11.6m tile), height free-form',
     '(3\u00d73 slots + sidewalks = 11.6m tile; terrain chunk = 322\u00d72\u00d7322 incl. half-roads), height free-form', False),
    ('docs/core/DOCUMENTATION_INDEX.md',
     'Voxel scale standards: building 0.1m, character 0.01m, item/decor 0.005m',
     'Voxel scale standards: building 0.05m (Unity-forced), character 0.01m, item/decor 0.005m', False),
    ('docs/core/DOCUMENTATION_INDEX.md',
     '- `roadWidth` locked: `private const 3.0f` \u2014 two 1.4 m lanes around the 1.0 m `vehicle_civilian_car_0`',
     '- `roadWidth` = `4.5f` (`[SerializeField]`, uniform across all seams \u2014 per-seam widths pending) \u2014 two ~2.25 m lanes around the 1.0 m `vehicle_civilian_car_0`', False),
    ('docs/core/DOCUMENTATION_INDEX.md',
     '- Cobble center stripe (\u00b10.05 m of corridor center) doubles as the dual-traffic divider; lane offset \u00b1`roadWidth`/4',
     '- Cobble stripes sit at each corridor-half\u2019s center (the quarter positions, \u00b1`roadWidth`/4) \u2014 each tile paints its own half\u2019s stripe so the pair across a seam reads as the dual-traffic divider; lane offset \u00b1`roadWidth`/4', False),
]

fails = 0
for path, old, new, allm in EDITS:
    fp = os.path.join(ROOT, path.replace('/', os.sep))
    txt = codecs.open(fp, 'r', 'utf-8').read()
    n = txt.count(old)
    if n == 0:
        print('MISS  %s :: %r' % (path, old[:70])); fails += 1; continue
    if allm:
        txt = txt.replace(old, new)
        print('OK(%d) %s' % (n, path))
    else:
        if n > 1: print('AMBIG(%d) %s :: %r' % (n, path, old[:70])); fails += 1; continue
        txt = txt.replace(old, new, 1)
        print('OK(1) %s' % path)
    codecs.open(fp, 'w', 'utf-8').write(txt)
print('---')
print('DONE' if fails == 0 else '%d MISSES' % fails)
sys.exit(1 if fails else 0)
