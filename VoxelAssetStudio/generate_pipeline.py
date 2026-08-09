#!/usr/bin/env python3
"""Generate character_pipeline.html — guided 3-phase character creation wizard."""
import json, os

# Load materials from existing editor
editor_path = os.path.join(os.path.dirname(__file__), "voxel_editor.html")
with open(editor_path, "r", encoding="utf-8") as f:
    src = f.read()
start = src.index("const allMaterials = ") + len("const allMaterials = ")
end = src.index(";", start)
materials_json = src[start:end].strip()

# We'll build the HTML in parts and write them to the output file
output_path = os.path.join(os.path.dirname(__file__), "character_pipeline.html")

# ─── PART 1: HEAD + CSS + BODY ───
part1 = r'''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Steel City — Character Pipeline</title>
<style>
* { margin:0; padding:0; box-sizing:border-box; }
body { background:#0a0a14; color:#eee; font-family:'Segoe UI',monospace; overflow:hidden; user-select:none; }
#canvas-container { position:absolute; top:0; left:0; width:100vw; height:100vh; }
#wizard-bar { position:fixed; top:0; left:0; right:0; height:52px; background:rgba(0,0,0,0.95); display:flex; align-items:center; padding:0 16px; z-index:200; border-bottom:2px solid #333; }
.wizard-step { display:flex; align-items:center; gap:8px; padding:6px 16px; border-radius:6px; font-size:13px; color:#666; transition:all 0.3s; margin:0 4px; }
.wizard-step.active { color:#fff; background:rgba(0,170,68,0.2); }
.wizard-step.done { color:#0c6; }
.wizard-step .step-num { width:24px; height:24px; border-radius:50%; border:2px solid currentColor; display:flex; align-items:center; justify-content:center; font-size:12px; font-weight:bold; }
.wizard-arrow { color:#444; font-size:14px; }
#wizard-back, #wizard-next { margin-left:auto; padding:8px 20px; border-radius:6px; border:1px solid #555; background:#2a2a3e; color:#eee; cursor:pointer; font-size:13px; transition:all 0.15s; }
#wizard-next { background:#0a4; border-color:#0c6; margin-left:8px; }
#wizard-back:hover, #wizard-next:hover { filter:brightness(1.3); }
#wizard-back:disabled, #wizard-next:disabled { opacity:0.3; cursor:not-allowed; }
.panel { position:fixed; top:60px; background:rgba(0,0,0,0.88); border-radius:8px; padding:12px; z-index:90; max-height:calc(100vh - 120px); overflow-y:auto; }
#left-panel { left:8px; width:220px; } #right-panel { right:8px; width:300px; }
.panel h3 { font-size:12px; color:#0ff; margin-bottom:8px; }
.panel label { display:block; font-size:11px; margin:6px 0 2px; color:#aaa; }
.panel select, .panel input[type=text], .panel input[type=number] { width:100%; background:#111; color:#0f0; border:1px solid #333; border-radius:4px; padding:4px 6px; font-size:11px; font-family:monospace; }
.panel input[type=range] { width:100%; }
.phase-content { display:none; } .phase-content.active { display:block; }
.tool-btn { width:34px; height:34px; border:1px solid #444; border-radius:6px; background:#2a2a3e; color:#aaa; cursor:pointer; font-size:15px; display:flex; align-items:center; justify-content:center; transition:all 0.15s; }
.tool-btn:hover { background:#3a3a5e; color:#fff; } .tool-btn.active { background:#0a4; color:#fff; border-color:#0c6; }
.tool-row { display:flex; gap:3px; flex-wrap:wrap; margin-bottom:6px; }
.group-item { display:flex; align-items:center; gap:6px; padding:5px 6px; border-radius:4px; cursor:pointer; margin:2px 0; font-size:11px; transition:all 0.15s; border:1px solid transparent; }
.group-item:hover { background:rgba(255,255,255,0.08); } .group-item.selected { background:rgba(0,170,68,0.25); border-color:#0c6; }
.group-item.isolated { box-shadow:0 0 0 2px #fa0; }
.group-swatch { width:16px; height:16px; border-radius:3px; border:1px solid #555; flex-shrink:0; }
.group-name { flex:1; } .group-count { color:#888; font-size:10px; }
.mat-grid { display:grid; grid-template-columns:repeat(6,1fr); gap:2px; max-height:200px; overflow-y:auto; }
.mat-cell { width:28px; height:28px; border-radius:3px; cursor:pointer; border:2px solid transparent; }
.mat-cell.selected { border-color:#fff; box-shadow:0 0 4px #fff; }
.state-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:3px; margin-bottom:6px; }
.state-btn { padding:5px 3px; border:1px solid #444; border-radius:4px; background:#2a2a3e; color:#aaa; cursor:pointer; font-size:10px; text-align:center; transition:all 0.15s; }
.state-btn:hover { background:#3a3a5e; color:#fff; } .state-btn.active { background:#0a4; color:#fff; border-color:#0c6; }
.playback-row { display:flex; gap:4px; margin:6px 0; align-items:center; }
.playback-btn { width:30px; height:30px; border:1px solid #444; border-radius:6px; background:#2a2a3e; color:#aaa; cursor:pointer; font-size:13px; display:flex; align-items:center; justify-content:center; }
.playback-btn:hover { background:#3a3a5e; color:#fff; } .playback-btn.active { background:#0a4; color:#fff; border-color:#0c6; }
.view-row { display:flex; gap:3px; margin:6px 0; }
.view-btn { flex:1; padding:5px; border:1px solid #444; border-radius:4px; background:#2a2a3e; color:#aaa; cursor:pointer; font-size:10px; text-align:center; transition:all 0.15s; }
.view-btn:hover { background:#3a3a5e; } .view-btn.active { background:#06a; color:#fff; border-color:#08c; }
.pivot-row { display:flex; gap:4px; align-items:center; margin:2px 0; }
.pivot-row label { width:50px; font-size:10px; color:#888; margin:0; }
.pivot-row input { width:50px; background:#111; color:#0f0; border:1px solid #333; border-radius:3px; padding:2px 4px; font-size:10px; }
.param-row { display:flex; align-items:center; gap:6px; margin:2px 0; font-size:10px; }
.param-row label { width:70px; color:#888; } .param-row input[type=range] { flex:1; } .param-row .param-val { width:36px; color:#0f0; text-align:right; font-size:10px; }
.feature-row { margin:4px 0; } .feature-row label { font-size:10px; color:#888; display:block; margin-bottom:2px; } .feature-row select { width:100%; }
#status-bar { position:fixed; bottom:0; left:0; right:0; height:26px; background:rgba(0,0,0,0.9); display:flex; align-items:center; padding:0 12px; font-size:11px; color:#888; z-index:100; border-top:1px solid #333; gap:16px; }
#status-fps { margin-left:auto; color:#666; }
#export-modal { position:fixed; top:0; left:0; width:100vw; height:100vh; background:rgba(0,0,0,0.7); display:none; align-items:center; justify-content:center; z-index:300; }
#export-box { background:#1e1e2e; border:1px solid #444; border-radius:12px; padding:24px; max-width:480px; width:90%; }
#export-box h2 { color:#0ff; margin-bottom:16px; font-size:16px; } #export-box h3 { font-size:13px; color:#0ff; margin-bottom:8px; }
#export-box p { font-size:11px; color:#aaa; margin-bottom:8px; }
.btn-row { display:flex; gap:8px; margin-top:8px; flex-wrap:wrap; }
.export-btn { background:#2a2a3e; color:#eee; border:1px solid #555; padding:8px 14px; border-radius:6px; cursor:pointer; font-size:12px; }
.export-btn:hover { background:#3a3a5e; } .export-btn.primary { background:#0a4; border-color:#0c6; }
#isolate-banner { position:fixed; top:60px; left:50%; transform:translateX(-50%); background:rgba(250,160,0,0.2); border:1px solid #fa0; border-radius:6px; padding:4px 16px; font-size:11px; color:#fa0; z-index:95; display:none; }
</style>
</head>
<body>
<div id="canvas-container"></div>
<div id="wizard-bar">
  <div class="wizard-step active" data-phase="1"><span class="step-num">1</span> Create</div>
  <span class="wizard-arrow">&rarr;</span>
  <div class="wizard-step" data-phase="2"><span class="step-num">2</span> Sculpt &amp; Rig</div>
  <span class="wizard-arrow">&rarr;</span>
  <div class="wizard-step" data-phase="3"><span class="step-num">3</span> Animate</div>
  <span class="wizard-arrow">&rarr;</span>
  <div class="wizard-step" data-phase="4"><span class="step-num">4</span> Export</div>
  <button id="wizard-back" disabled>&larr; Back</button>
  <button id="wizard-next">Next: Sculpt &amp; Rig &rarr;</button>
</div>
<div id="isolate-banner">Isolated: <span id="isolate-name"></span> &mdash; click same group again to exit</div>
<div class="panel" id="left-panel">
  <div class="phase-content active" id="phase1-left">
    <h3>&#128295; Character Creator</h3>
    <div class="feature-row"><label>Seed</label><div style="display:flex;gap:4px;"><input type="number" id="seed-input" value="42" style="flex:1;"><button class="tool-btn" onclick="randomizeSeed()" title="Random" style="width:30px;height:28px;">&#127922;</button></div></div>
    <div class="feature-row"><label>Body Type</label><select id="feat-body"><option value="hoodlum">Hoodlum</option><option value="civilian">Civilian</option><option value="police">Police</option><option value="overcoat">Overcoat</option></select></div>
    <div class="feature-row"><label>Head Type (0-13)</label><input type="range" id="feat-head" min="0" max="13" value="0"></div>
    <div class="feature-row"><label>Hair Style</label><select id="feat-hair"><option value="0">Bald</option><option value="1">Short Crop</option><option value="2">Buzz Cut</option><option value="3">Side-Parted</option><option value="4">Long</option><option value="5">Mohawk</option><option value="6">Curly</option><option value="7">Slicked Back</option></select></div>
    <div class="feature-row"><label>Eyes</label><select id="feat-eyes"><option value="0">Small</option><option value="1">Medium</option><option value="2">Large</option><option value="3">Narrow</option><option value="4">Round</option><option value="5">Hooded</option></select></div>
    <div class="feature-row"><label>Nose</label><select id="feat-nose"><option value="0">Small</option><option value="1">Medium</option><option value="2">Large</option><option value="3">Button</option><option value="4">Hooked</option></select></div>
    <div class="feature-row"><label>Mouth</label><select id="feat-mouth"><option value="0">Small</option><option value="1">Wide</option><option value="2">Frown</option><option value="3">Smile</option><option value="4">Neutral</option></select></div>
    <div class="feature-row"><label>Skin Tone (0-63)</label><input type="range" id="feat-skin" min="0" max="63" value="20"></div>
    <div style="margin-top:8px;font-size:10px;color:#888;">Based on Gangsters (1998) 5-layer portrait system.<br>Seed-based: same seed = same character.</div>
  </div>
  <div class="phase-content" id="phase2-left">
    <h3>&#128295; Sculpt Tools</h3>
    <div class="tool-row">
      <button class="tool-btn active" data-tool="place" title="Place (P)">&#129825;</button>
      <button class="tool-btn" data-tool="erase" title="Erase (E)">&#10006;</button>
      <button class="tool-btn" data-tool="paint" title="Paint (M)">&#127912;</button>
      <button class="tool-btn" data-tool="eyedropper" title="Pick (I)">&#128167;</button>
      <button class="tool-btn" data-tool="fill" title="Flood Fill (F)">&#129701;</button>
      <button class="tool-btn" data-tool="line" title="Line (L)">&#128207;</button>
      <button class="tool-btn" data-tool="box" title="Box (B)">&#128230;</button>
    </div>
    <h3>&#127912; Materials</h3><div class="mat-grid" id="mat-grid"></div>
    <div style="margin-top:8px;"><label style="font-size:10px;color:#888;">Mirror:</label>
      <div class="tool-row"><button class="tool-btn" id="mirror-x" title="Mirror X" style="width:30px;height:28px;font-size:10px;">X</button><button class="tool-btn" id="mirror-y" title="Mirror Y" style="width:30px;height:28px;font-size:10px;">Y</button><button class="tool-btn" id="mirror-z" title="Mirror Z" style="width:30px;height:28px;font-size:10px;">Z</button></div>
    </div>
    <div style="margin-top:6px;"><button class="tool-btn" id="undo-btn" title="Undo" style="width:30px;height:28px;">&#8617;</button><button class="tool-btn" id="redo-btn" title="Redo" style="width:30px;height:28px;">&#8618;</button></div>
  </div>
  <div class="phase-content" id="phase3-left">
    <h3>&#127916; Animation States</h3>
    <div class="state-grid" id="state-grid"></div>
    <div class="playback-row"><button class="playback-btn active" id="play-btn" title="Play/Pause">&#9654;</button><button class="playback-btn" id="stop-btn" title="Stop">&#9209;</button><span style="font-size:10px;color:#888;">Speed:</span><input type="range" id="speed-slider" min="0.1" max="3" step="0.1" value="1" style="flex:1;"><span id="speed-val" style="font-size:10px;color:#0f0;width:28px;">1.0x</span></div>
    <div style="display:flex;align-items:center;gap:6px;margin:6px 0;"><span style="font-size:10px;color:#888;">Time:</span><input type="range" id="timeline-slider" min="0" max="10" step="0.01" value="0" style="flex:1;"><span id="timeline-time" style="font-size:10px;color:#0f0;width:50px;">0.00s</span></div>
    <div class="view-row"><button class="view-btn active" data-view="material">Material View</button><button class="view-btn" data-view="group">Group View</button></div>
  </div>
  <div class="phase-content" id="phase4-left">
    <h3>&#128230; Export</h3>
    <p style="font-size:11px;color:#aaa;margin-bottom:8px;">Your character is ready. Export the files for Unity.</p>
    <div class="btn-row" style="flex-direction:column;">
      <button class="export-btn primary" onclick="exportStassetJSON()" style="width:100%;margin-bottom:4px;">&#11015; Export .stasset JSON</button>
      <button class="export-btn primary" onclick="exportGroupsJSON()" style="width:100%;margin-bottom:4px;">&#11015; Export .groups JSON</button>
      <button class="export-btn" onclick="exportAnimParams()" style="width:100%;margin-bottom:4px;">&#11015; Export .anim.json</button>
      <button class="export-btn" onclick="saveProject()" style="width:100%;margin-bottom:4px;">&#128190; Save Project</button>
    </div>
  </div>
</div>
<div class="panel" id="right-panel">
  <div class="phase-content active" id="phase1-right">
    <h3>&#127913; Accessories</h3>
    <div class="feature-row"><label>Glasses (Feature A)</label><select id="feat-glasses"><option value="0">None</option><option value="1">Round</option><option value="2">Square</option><option value="3">Shades</option></select></div>
    <div class="feature-row"><label>Hat (Feature B)</label><select id="feat-hat"><option value="0">None</option><option value="1">Cap</option><option value="2">Fedora</option><option value="3">Beanie</option><option value="4">Bowler</option><option value="5">Bandana</option><option value="6">Top Hat</option><option value="7">Helmet</option></select></div>
    <div class="feature-row"><label>Facial Hair (Flag)</label><select id="feat-beard"><option value="0">Clean</option><option value="1">Stubble</option><option value="2">Full Beard</option></select></div>
    <div class="feature-row"><label>Scar / Accessory (Feature C)</label><select id="feat-scar"><option value="0">None</option><option value="1">Scar (Left)</option><option value="2">Scar (Right)</option><option value="3">Earring (Left)</option><option value="4">Earring (Right)</option><option value="5">Gold Tooth</option><option value="6">Tattoo</option><option value="7">Cigar</option></select></div>
    <div style="margin-top:8px;"><button class="export-btn primary" onclick="generateFromFeatures()" style="width:100%;">&#128260; Regenerate from Features</button></div>
    <div style="margin-top:8px;"><button class="export-btn" onclick="generateFromSeed()" style="width:100%;">&#127922; Generate from Seed Only</button></div>
    <div style="margin-top:12px;font-size:10px;color:#888;"><b>Appearance Bitfield:</b><br>Bits 0-5: Skin tone (64)<br>Bits 6-7: Glasses (4)<br>Bits 8-10: Hat (8)<br>Bit 11: Facial hair<br>Bits 12-14: Scar (8)<br>Bit 15: Portrait generated</div>
  </div>
  <div class="phase-content" id="phase2-right">
    <h3>&#129514; Animation Groups</h3>
    <div id="group-list"></div>
    <div style="margin-top:8px;font-size:10px;color:#888;">Click a group to <b>isolate</b> it.<br>Voxels you place get auto-assigned to the selected group.<br>Mirror X links L&#8596;R arm/leg pairs.</div>
    <div style="margin-top:6px;"><button class="export-btn" onclick="autoAssignGroups()" style="font-size:10px;padding:4px 8px;">Auto-Assign</button><button class="export-btn" onclick="clearAllGroups()" style="font-size:10px;padding:4px 8px;margin-left:4px;">Clear All</button></div>
  </div>
  <div class="phase-content" id="phase3-right">
    <h3>&#127919; Pivot Points</h3><div id="pivot-controls"></div>
    <div style="margin-top:6px;"><button class="export-btn" onclick="resetPivots()" style="font-size:10px;padding:4px 8px;">Reset</button><button class="export-btn" onclick="autoDetectPivots()" style="font-size:10px;padding:4px 8px;margin-left:4px;">Auto-Detect</button></div>
    <h3 style="margin-top:12px;">&#9881; Animation Parameters</h3><div id="param-controls"></div>
  </div>
  <div class="phase-content" id="phase4-right">
    <h3>&#128203; Character Summary</h3><div id="char-summary" style="font-size:11px;color:#aaa;"></div>
  </div>
</div>
<div id="status-bar"><span id="status-info">Ready &mdash; Phase 1: Create your character</span><span id="status-fps">0 FPS</span></div>
<div id="export-modal"><div id="export-box"><h2>&#128193; Load Existing Character</h2><p>Import a .stasset JSON or project file to edit an existing character.</p><div class="btn-row"><button class="export-btn primary" onclick="document.getElementById('import-stasset-input').click()">&#128229; Import .stasset JSON</button><input type="file" id="import-stasset-input" accept=".json" style="display:none" onchange="importStasset(event)"><button class="export-btn" onclick="document.getElementById('import-project-input').click()">&#128193; Load Project</button><input type="file" id="import-project-input" accept=".json" style="display:none" onchange="loadProject(event)"></div><div class="btn-row" style="justify-content:flex-end;margin-top:16px;"><button class="export-btn" onclick="document.getElementById('export-modal').style.display='none'">Cancel</button></div></div></div>
'''

# Write part 1
with open(output_path, 'w', encoding='utf-8') as f:
    f.write(part1)
    f.write('\n<script type="importmap">\n{"imports":{"three":"https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.module.js","three/addons/":"https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/"}}\n</script>\n<script type="module">\n')

print(f"Part 1 written: {os.path.getsize(output_path)} bytes")
