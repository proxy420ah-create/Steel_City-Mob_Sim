# ARCHITECT — side table diagnosis: DVM-2252 model residuals (kind: aside, no floor)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — model residuals, file-verified. Three defects, one block.</b><br><br>'
    '<b>1. Black render</b> — L1994: <code>MeshLambertMaterial({vertexColors:true, color:0xffffff})</code>. '
    '<code>vertexColors:true</code> makes the shader multiply by the geometry <code>color</code> attribute — '
    '<code>BoxGeometry</code> has none, so it reads (0,0,0) and blacks every instance no matter the base or '
    '<code>instanceColor</code>. The four furniture loaders (L1386/1411/1432/1456) use bare '
    '<code>MeshLambertMaterial()</code> + <code>setColorAt</code> and render fine. <b>Fix: delete the flag.</b><br><br>'
    '<b>2. T-pose idle</b> — L2009: idle gets <code>p = {}</code> (empty recipe), so <code>pose(\'idle\',0)</code> '
    'zeroes every group rotation → bind T-pose. The rig ships no idle recipe in <code>animParams</code>; '
    'a rest pose (arms down ~&plusmn;1.57 rad armSwing) has to be authored or assumed.<br><br>'
    '<b>3. No gait — two compounding.</b> (a) Recipes are <b>radians</b> (<code>armSwingL:-0.62</code>, '
    'crouch <code>legStrideL:1.34</code>) but L2024 multiplies <code>deg*&pi;/180</code> → motion ~57&times; too '
    'small, invisible. (b) <code>piv</code> is fetched L2023 but <b>never applied</b> — <code>m.rotation</code> '
    'turns around the mesh origin (floor-center), not the joint; even radian-correct, limbs orbit instead of '
    'articulating. Fix: offset instance positions by <code>-pivot&middot;dims&middot;VS</code> so mesh rotation '
    'hinges at the joint. Secondary: L2023 looks up <code>pivots[findIndex]</code> — array index vs id-keyed '
    'pivots swaps Forearm/Shin pairs (ids 6–9).<br><br>'
    'Also unmapped: <code>modelLower</code>/<code>bodyLower</code>/<code>bodyForward</code> — body-translation '
    'params the A-map ignores; harmless for normal walk.<br><br>'
    'Root pattern: same class as the schema drift — the loader guesses shapes instead of reading authored ones. '
    'All fixes live inside the char block; no schema or route changes needed.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'ARCHITECT') + 1
d['messages'].append({
    'id': f'ARCHITECT-{n}',
    'speaker': 'ARCHITECT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS', 'BUTTON'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote ARCHITECT-{n} (aside), turn stays {d["turn"]}')
