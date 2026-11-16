# _split_construct.py — split docs/construct.html's inline module body into ordered
# sibling classic scripts under docs/construct/, preserving top-down eval order.
# The body becomes a tiny module prelude (window.THREE) + <script defer src> tags —
# deferred classic scripts and module scripts share one ordered queue in document order,
# and top-level let/const/function share the page-wide lexical environment, so
# concatenating the files reproduces the original semantics verbatim.
import codecs, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'construct.html')
OUTDIR = os.path.join(HERE, 'construct')

# (filename, title, [(start,end) inclusive 1-based line ranges in construct.html])
FILES = [
  ('01_shell.js',       'renderer/scene/camera, lights + inspection torch, MANIFEST tiles, walk law, floor + bounds, MAT_LIB, voxEmit', [(27, 197)]),
  ('02_placed.js',      'PLACED registry, emitAsset, surfaceAt — plus the canonical exhibits (door, rotator, desk)', [(199, 287)]),
  ('03_tables.js',      'drafting tables: emitTable, piston lift, stasset/block/zone emit, emitVox, parked cars, blockTrf cruise, mini sync/slice/spin, mountModel + table boot', [(289, 772)]),
  ('04_pad.js',         'debug pad — pages, regions, canvas screen, cursor focus, wall mount', [(774, 968)]),
  ('05_pose_engine.js', 'PARTY handoff + OUTFIT, poseEngine — the animator\'s rig math, ported flat-matrix', [(969, 1117)]),
  ('06_followers.js',   'followers — emitChar rig instancing + followStep nav/gait/gaze', [(1121, 1210)]),
  ('07_pov_aim.js',     'follower POV eyes + PiP (__eyes), laser/tag/aim machinery (rayPruned, tagAt, aimStep)', [(1212, 1320)]),
  ('08_panels.js',      'in-scene panels — door console, spec plaque, mannequin kiosk (OUTFITS, BUTTONS)', [(1322, 1459)]),
  ('09_live_ctl.js',    'agent live link (/peek, /shot, cmd dispatch) + follower control strip', [(1461, 1539)]),
  ('10_self_solids.js', 'self body (Vinny at your feet), roster persistence, ready gate, SOLIDS', [(1541, 1626)]),
  ('11_input.js',       'FPS walk, INTERACT registry, key/mouse handlers', [(1628, 1723)]),
  ('12_carry.js',       'held-item grip/drop (takeIt/placeIt/worldSurfaceAt), drop ghost, goHome', [(1725, 1792)]),
  ('13_place.js',       'place mode — selection, carry, settleOrphans, undo, paste, __place', [(1794, 2009)]),
  ('14_loop.js',        'platform step, render loop, compass HUD', [(2011, 2089)]),
  ('15_boot.js',        'warm-up: ready.then tombstones/overrides/lifts/mounts/extras/fixpoint, rAF kick, resize', [(2091, 2133)]),
  ('16_probe.js',       'probe surface — camLock, __cam/__snap, __construct agent API, __envShot/__uiClean', [(2135, 2176)]),
]

with codecs.open(SRC, 'r', 'utf-8') as f:
    lines = f.read().split('\n')
n = len(lines)
print('source lines:', n)

# coverage check — every line 27..2176 either covered by a range or whitespace
covered = set()
for fn, title, ranges in FILES:
    for a, b in ranges:
        for i in range(a, b + 1):
            assert i not in covered, 'line %d covered twice' % i
            covered.add(i)
for i in range(27, 2177):
    if i not in covered:
        assert lines[i - 1].strip() == '', 'uncovered non-blank line %d: %r' % (i, lines[i - 1])
print('coverage: all non-blank lines 27-2176 accounted for')

os.makedirs(OUTDIR, exist_ok=True)
script_tags = []
for fn, title, ranges in FILES:
    body = []
    for a, b in ranges:
        body.extend(lines[a - 1:b])
    hdr = ("'use strict';\n/* construct/%s — %s.\n"
           "   Split from construct.html (was lines %s); classic <script defer> siblings share one\n"
           "   page-wide scope, load order = the old top-down eval order. */\n" %
           (fn, title, ' + '.join('%d-%d' % r for r in ranges)))
    path = os.path.join(OUTDIR, fn)
    with codecs.open(path, 'w', 'utf-8') as f:
        f.write(hdr + '\n' + '\n'.join(body) + '\n')
    script_tags.append('<script defer src="construct/%s"></script>' % fn)
    print('  wrote %-22s %4d lines' % (fn, len(body)))

prelude = '''<script type="module">
import * as THREE from 'three';
window.THREE = window.__THREE = THREE;                 // bare THREE resolves for the classic files below
</script>
<!-- construct.html — the page body lives in docs/construct/*.js (split for navigation).
     Semantics: a module prelude + classic deferred siblings. Module scripts and <script defer>
     share ONE deferred queue in document order, so load order here = the old top-down eval
     order. Top-level let/const/function in classic scripts share a page-wide lexical
     environment — cross-file calls work exactly like the old single module body.
     File map:
       01_shell        scene/renderer/lights/torch, floor+bounds, voxEmit
       02_placed       PLACED registry, emitAsset/surfaceAt, canonical exhibits
       03_tables       tables, piston lift, stasset/block/zone emitVox, parked cars, blockTrf
       04_pad          debug pad pages/regions/screen/wall-mount
       05_pose_engine  PARTY handoff + poseEngine rig math
       06_followers    emitChar + followStep
       07_pov_aim      follower POV eyes, laser/tag/aim
       08_panels       door console, spec plaque, mannequin kiosk
       09_live_ctl     agent live link + follower control strip
       10_self_solids  self body, roster persist, ready gate, SOLIDS
       11_input        FPS walk + INTERACT + key/mouse handlers
       12_carry        held-item grip/drop, ghost, goHome
       13_place        place mode
       14_loop         walk step + render loop + compass
       15_boot         warm-up boot sequence, rAF kick, resize
       16_probe        __cam/__snap/__construct probe surface -->
'''
html = '\n'.join(lines[:23]) + '\n' + prelude + '\n'.join(script_tags) + '\n' + '\n'.join(lines[2177:]) + '\n'
with codecs.open(SRC, 'w', 'utf-8') as f:
    f.write(html)
print('construct.html rewritten:', len(html.split(chr(10))), 'lines')
