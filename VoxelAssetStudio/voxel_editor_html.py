"""Generate a full-featured web voxel editor from a .stasset file.
Three.js + InstancedMesh + raycasting + paint/erase/add tools.
Usage: python voxel_editor_html.py <file.stasset> [output.html]
"""
import sys
import json
import numpy as np
from stasset_io import load_stasset_full, save_stasset
from material_library import get_material_color, get_material_name, MATERIALS

def get_all_materials():
    """Get all materials from the library as a list of dicts."""
    mats = []
    for mid, info in MATERIALS.items():
        if mid < 100:
            continue
        r, g, b, a = info.get("color", (0.5, 0.5, 0.5, 1.0))
        mats.append({
            "id": mid,
            "name": info.get("name", f"Material_{mid}"),
            "r": int(r * 255),
            "g": int(g * 255),
            "b": int(b * 255),
            "hex": f"#{int(r*255):02x}{int(g*255):02x}{int(b*255):02x}"
        })
    return sorted(mats, key=lambda m: m["id"])

def voxel_to_editor(voxels, dims, filepath, title="Voxel Editor"):
    w, h, d = dims
    
    # Build full voxel array (not just surface — editor needs all)
    voxel_data = []
    for y in range(h):
        for z in range(d):
            for x in range(w):
                mid = int(voxels[x, y, z])
                if mid != 0:
                    voxel_data.append([x, y, z, mid])
    
    # Get materials present in the model
    present_mids = sorted(set(v[3] for v in voxel_data))
    present_materials = []
    for mid in present_mids:
        r, g, b, a = get_material_color(mid)
        present_materials.append({
            "id": mid,
            "name": get_material_name(mid),
            "r": int(r*255), "g": int(g*255), "b": int(b*255),
            "hex": f"#{int(r*255):02x}{int(g*255):02x}{int(b*255):02x}",
            "count": sum(1 for v in voxel_data if v[3] == mid)
        })
    
    # All available materials from library
    all_materials = get_all_materials()
    
    voxel_json = json.dumps(voxel_data)
    present_json = json.dumps(present_materials)
    all_mat_json = json.dumps(all_materials)
    
    html = f'''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>{title}</title>
<style>
* {{ margin:0; padding:0; box-sizing:border-box; }}
body {{ background:#1a1a2e; color:#eee; font-family:'Segoe UI',monospace; overflow:hidden; user-select:none; }}
#canvas-container {{ position:absolute; top:0; left:0; width:100vw; height:100vh; }}

/* Top toolbar */
#toolbar {{
  position:fixed; top:0; left:0; right:0; height:48px;
  background:rgba(0,0,0,0.85); display:flex; align-items:center;
  padding:0 12px; gap:4px; z-index:100; border-bottom:1px solid #333;
}}
.tool-btn {{
  width:36px; height:36px; border:1px solid #444; border-radius:6px;
  background:#2a2a3e; color:#aaa; cursor:pointer; font-size:16px;
  display:flex; align-items:center; justify-content:center;
  transition:all 0.15s;
}}
.tool-btn:hover {{ background:#3a3a5e; color:#fff; }}
.tool-btn.active {{ background:#0a4; color:#fff; border-color:#0c6; }}
.tool-sep {{ width:1px; height:32px; background:#333; margin:0 4px; }}
.tool-label {{ font-size:11px; color:#888; margin-right:4px; }}

/* Left panel - materials */
#left-panel {{
  position:fixed; top:56px; left:8px; width:220px;
  background:rgba(0,0,0,0.8); border-radius:8px; padding:12px;
  z-index:90; max-height:calc(100vh - 120px); overflow-y:auto;
}}
#left-panel h3 {{ font-size:12px; color:#0ff; margin-bottom:8px; }}
.mat-item {{
  display:flex; align-items:center; gap:8px; padding:4px 6px;
  border-radius:4px; cursor:pointer; margin:2px 0; font-size:11px;
  transition:background 0.15s;
}}
.mat-item:hover {{ background:rgba(255,255,255,0.08); }}
.mat-item.selected {{ background:rgba(0,170,68,0.25); border:1px solid #0c6; }}
.mat-item .mat-swatch {{
  width:18px; height:18px; border-radius:3px; border:1px solid #555; flex-shrink:0;
}}
.mat-item .mat-name {{ flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }}
.mat-item .mat-count {{ color:#888; font-size:10px; }}
.mat-item .mat-vis {{
  width:16px; height:16px; border:1px solid #555; border-radius:3px;
  display:flex; align-items:center; justify-content:center; font-size:10px; cursor:pointer;
}}
.mat-item .mat-vis.hidden {{ background:#333; color:#666; }}

/* Right panel - layers & info */
#right-panel {{
  position:fixed; top:56px; right:8px; width:240px;
  background:rgba(0,0,0,0.8); border-radius:8px; padding:12px;
  z-index:90; max-height:calc(100vh - 120px); overflow-y:auto;
}}
#right-panel h3 {{ font-size:12px; color:#0ff; margin-bottom:8px; }}
#right-panel label {{ display:block; font-size:11px; margin:6px 0 2px; color:#aaa; }}
#right-panel input[type=range] {{ width:100%; }}
.layer-info {{ font-size:10px; color:#0f0; margin:4px 0; }}

/* Bottom status bar */
#status-bar {{
  position:fixed; bottom:0; left:0; right:0; height:28px;
  background:rgba(0,0,0,0.85); display:flex; align-items:center;
  padding:0 12px; font-size:11px; color:#888; z-index:100;
  border-top:1px solid #333; gap:16px;
}}
#status-coords {{ color:#0f0; min-width:180px; }}
#status-tool {{ color:#0ff; min-width:100px; }}
#status-info {{ color:#888; }}
#status-fps {{ margin-left:auto; color:#666; }}

/* Compass */
#compass-info {{ font-size:10px; margin-top:8px; color:#888; }}
#compass-info span {{ font-weight:bold; }}
.cx {{ color:#e33; }} .cy {{ color:#3e3; }} .cz {{ color:#33e; }}

/* Export modal */
#export-modal {{
  position:fixed; top:0; left:0; width:100vw; height:100vh;
  background:rgba(0,0,0,0.7); display:none; align-items:center; justify-content:center;
  z-index:200;
}}
#export-box {{
  background:#1e1e2e; border:1px solid #444; border-radius:12px;
  padding:24px; max-width:500px; width:90%;
}}
#export-box h2 {{ color:#0ff; margin-bottom:16px; font-size:16px; }}
#export-box textarea {{
  width:100%; height:200px; background:#111; color:#0f0;
  border:1px solid #333; border-radius:6px; padding:8px; font-family:monospace;
  font-size:11px; resize:vertical;
}}
#export-box .btn-row {{ display:flex; gap:8px; margin-top:12px; }}
.export-btn {{
  background:#2a2a3e; color:#eee; border:1px solid #555; padding:8px 16px;
  border-radius:6px; cursor:pointer; font-size:12px;
}}
.export-btn:hover {{ background:#3a3a5e; }}
.export-btn.primary {{ background:#0a4; border-color:#0c6; }}

/* Tooltip */
#tooltip {{
  position:fixed; background:rgba(0,0,0,0.9); border:1px solid #444;
  border-radius:4px; padding:4px 8px; font-size:11px; color:#ddd;
  pointer-events:none; z-index:300; display:none;
}}

/* Advanced tools panel */
#adv-panel {{
  position:fixed; top:56px; left:240px; width:200px;
  background:rgba(0,0,0,0.8); border-radius:8px; padding:12px;
  z-index:88; display:none;
}}
#adv-panel h3 {{ font-size:12px; color:#0ff; margin-bottom:8px; }}
.adv-row {{ display:flex; align-items:center; gap:6px; margin:4px 0; font-size:11px; }}
.adv-row label {{ color:#aaa; }}
.mirror-btn {{
  width:28px; height:28px; border:1px solid #444; border-radius:4px;
  background:#2a2a3e; color:#aaa; cursor:pointer; font-size:12px;
  display:flex; align-items:center; justify-content:center;
}}
.mirror-btn:hover {{ background:#3a3a5e; }}
.mirror-btn.active {{ background:#0a4; color:#fff; border-color:#0c6; }}

/* Script console */
#console-modal {{
  position:fixed; top:0; left:0; width:100vw; height:100vh;
  background:rgba(0,0,0,0.7); display:none; align-items:center; justify-content:center;
  z-index:200;
}}
#console-box {{
  background:#1e1e2e; border:1px solid #444; border-radius:12px;
  padding:24px; max-width:600px; width:90%;
}}
#console-box h2 {{ color:#0ff; margin-bottom:8px; font-size:16px; }}
#console-box p {{ font-size:11px; color:#888; margin-bottom:8px; }}
#console-input {{
  width:100%; height:120px; background:#111; color:#0f0;
  border:1px solid #333; border-radius:6px; padding:8px; font-family:monospace;
  font-size:11px; resize:vertical;
}}
#console-output {{
  width:100%; height:80px; background:#0a0a14; color:#0f0;
  border:1px solid #333; border-radius:6px; padding:8px; font-family:monospace;
  font-size:11px; margin-top:8px; overflow-y:auto; white-space:pre-wrap;
}}
.console-btn {{
  background:#2a2a3e; color:#eee; border:1px solid #555; padding:6px 14px;
  border-radius:6px; cursor:pointer; font-size:12px; margin-right:4px;
}}
.console-btn:hover {{ background:#3a3a5e; }}
.console-btn.run {{ background:#0a4; border-color:#0c6; }}

/* Selection box */
.sel-info {{ font-size:10px; color:#fa0; margin:4px 0; }}
</style>
</head>
<body>
<div id="canvas-container"></div>

<!-- Toolbar -->
<div id="toolbar">
  <span class="tool-label">Tools:</span>
  <button class="tool-btn active" data-tool="place" title="Place (P)">⬛</button>
  <button class="tool-btn" data-tool="erase" title="Erase (E)">🗑</button>
  <button class="tool-btn" data-tool="paint" title="Paint (B)">🎨</button>
  <button class="tool-btn" data-tool="eyedropper" title="Eyedropper (I)">💧</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" data-tool="fill" title="Fill (F)">🪣</button>
  <select id="fill-mode" style="background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;padding:3px 6px;font-size:10px;margin-left:2px;">
    <option value="cavity">Cavity</option>
    <option value="air">Air (all)</option>
    <option value="replace">Replace</option>
  </select>
  <button class="tool-btn" data-tool="line" title="Line (L)">📏</button>
  <button class="tool-btn" data-tool="ruler" title="Ruler (R)">📐</button>
  <button class="tool-btn" data-tool="box" title="Box (X)">📦</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" id="undo-btn" title="Undo (Ctrl+Z)">↩</button>
  <button class="tool-btn" id="redo-btn" title="Redo (Ctrl+Y)">↪</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" data-tool="select" title="Select (S)">🔲</button>
  <button class="tool-btn" data-tool="extrude" title="Extrude (J)">⬆</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" data-tool="camera" title="Camera Mode (Esc)">🎥</button>
  <button class="tool-btn" onclick="copySelection()" title="Copy Selection">📋</button>
  <button class="tool-btn" onclick="pasteSelection()" title="Paste (preview + move)">📌</button>
  <button class="tool-btn" onclick="deleteSelection()" title="Delete Selection">🗑</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" id="mirror-btn" title="Mirror Settings">🪞</button>
  <button class="tool-btn" id="replace-btn" title="Replace Material">🔄</button>
  <button class="tool-btn" id="console-btn" title="Script Console">⚙</button>
  <button class="tool-btn" id="expand-btn" title="Expand Volume">📐+</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" id="grid-btn" title="Toggle Grid Lines" class="active">▦</button>
  <button class="tool-btn" id="reset-btn" title="Reset View">🎯</button>
  <div class="tool-sep"></div>
  <button class="tool-btn" id="export-btn" title="Save / Load / Export" style="background:#0a4;color:#fff;">💾</button>
</div>

<!-- Left panel: Materials -->
<div id="left-panel">
  <h3>Materials</h3>
  <div id="mat-list"></div>
  <div id="compass-info">
    <span class="cx">X</span>=red
    <span class="cy">Y</span>=green(up)
    <span class="cz">Z</span>=blue
  </div>
</div>

<!-- Right panel: Layers -->
<div id="right-panel">
  <h3>Layers & View</h3>
  <label>Y Slice (max): <span id="ymax-val"></span></label>
  <input type="range" id="ySliceMax" min="0" max="{h-1}" value="{h-1}">
  <label>Y Slice (min): <span id="ymin-val"></span></label>
  <input type="range" id="ySliceMin" min="0" max="{h-1}" value="0">
  <label>X Slice (max): <span id="xmax-val"></span></label>
  <input type="range" id="xSliceMax" min="0" max="{w-1}" value="{w-1}">
  <label>X Slice (min): <span id="xmin-val"></span></label>
  <input type="range" id="xSliceMin" min="0" max="{w-1}" value="0">
  <label>Z Slice (max): <span id="zmax-val"></span></label>
  <input type="range" id="zSliceMax" min="0" max="{d-1}" value="{d-1}">
  <label>Z Slice (min): <span id="zmin-val"></span></label>
  <input type="range" id="zSliceMin" min="0" max="{d-1}" value="0">
  <div class="layer-info" id="layer-info">All layers visible</div>
  <label>Model: {w}x{h}x{d}</label>
  <label>Voxels: <span id="voxel-count">{len(voxel_data)}</span></label>
  <label>Visible: <span id="visible-count">{len(voxel_data)}</span></label>
  <h3 style="margin-top:12px;font-size:12px;color:#0ff;">Shortcuts</h3>
  <div style="font-size:10px;color:#888;line-height:1.6;">
    <b>P</b>=Place &nbsp; <b>E</b>=Erase &nbsp; <b>B</b>=Paint<br>
    <b>I</b>=Eyedropper &nbsp; <b>F</b>=Fill<br>
    <b>L</b>=Line &nbsp; <b>X</b>=Box<br>
    <b>S</b>=Select &nbsp; <b>J</b>=Extrude<br>
    <b>Ctrl+Z</b>=Undo &nbsp; <b>Ctrl+Y</b>=Redo<br>
    <b>Esc</b>=Cancel &nbsp; <b>Drag</b>=Orbit<br>
    <b>Scroll</b>=Zoom &nbsp; <b>Right-drag</b>=Pan
  </div>
  <h3 style="margin-top:12px;font-size:12px;color:#0ff;">Tool Colors</h3>
  <div style="font-size:10px;color:#888;line-height:1.6;">
    <span style="color:#0f0;">■</span> Place &nbsp;
    <span style="color:#f00;">■</span> Erase &nbsp;
    <span style="color:#fa0;">■</span> Paint<br>
    <span style="color:#0af;">■</span> Eyedropper &nbsp;
    <span style="color:#0ff;">■</span> Fill<br>
    <span style="color:#ff0;">■</span> Line &nbsp;
    <span style="color:#f0f;">■</span> Box<br>
    <span style="color:#f80;">■</span> Select &nbsp;
    <span style="color:#8f0;">■</span> Extrude
  </div>
  <h3 style="margin-top:12px;font-size:12px;color:#0ff;">Advanced</h3>
  <div style="font-size:10px;color:#888;line-height:1.6;">
    🪞 Mirror — toggle X/Y/Z<br>
    🔄 Replace — swap materials<br>
    ⚙ Console — run JS scripts<br>
    Use panel for Copy/Paste/Delete
  </div>
</div>

<!-- Status bar -->
<div id="status-bar">
  <span id="status-tool">Tool: Place</span>
  <span id="status-coords">---, ---, ---</span>
  <span id="status-info">Ready</span>
  <span id="status-fps">0 FPS</span>
</div>

<!-- Save/Load/Export modal -->
<div id="export-modal">
  <div id="export-box">
    <h2>💾 Save / Load / Export</h2>
    
    <div style="margin-bottom:16px;">
      <h3 style="font-size:13px;color:#0ff;margin-bottom:8px;">Save / Load Project</h3>
      <p style="font-size:11px;color:#aaa;margin-bottom:8px;">Save your work to a JSON file and load it back later to continue editing.</p>
      <div class="btn-row">
        <button class="export-btn primary" onclick="saveProject()">💾 Save Project (.json)</button>
        <button class="export-btn" onclick="document.getElementById('load-file-input').click()">📂 Load Project</button>
        <input type="file" id="load-file-input" accept=".json" style="display:none" onchange="loadProject(event)">
      </div>
    </div>
    
    <div style="border-top:1px solid #333;padding-top:16px;margin-bottom:16px;">
      <h3 style="font-size:13px;color:#0ff;margin-bottom:8px;">Import .stasset</h3>
      <p style="font-size:11px;color:#aaa;margin-bottom:8px;">Load a .stasset JSON exported from the Python tool to edit an existing model.</p>
      <div class="btn-row">
        <button class="export-btn" onclick="document.getElementById('import-stasset-input').click()">📥 Import .stasset JSON</button>
        <input type="file" id="import-stasset-input" accept=".json" style="display:none" onchange="importStasset(event)">
      </div>
    </div>
    
    <div style="border-top:1px solid #333;padding-top:16px;margin-bottom:16px;">
      <h3 style="font-size:13px;color:#0ff;margin-bottom:8px;">Export Final Model</h3>
      <p style="font-size:11px;color:#aaa;margin-bottom:8px;">Export voxel data as JSON for conversion to .stasset via <code style="color:#0f0;">python json_to_stasset.py</code></p>
      <div class="btn-row">
        <button class="export-btn primary" onclick="exportStassetJSON()">⬇ Export .stasset JSON</button>
        <button class="export-btn" onclick="exportRawJSON()">📋 View Raw JSON</button>
      </div>
      <textarea id="export-json" readonly style="display:none;margin-top:8px;"></textarea>
    </div>
    
    <div class="btn-row" style="justify-content:flex-end;">
      <button class="export-btn" onclick="closeExport()">Close</button>
    </div>
  </div>
</div>

<!-- Tooltip -->
<div id="tooltip"></div>

<!-- Advanced tools panel (mirror) -->
<div id="adv-panel">
  <h3>🪞 Mirror</h3>
  <div class="adv-row">
    <button class="mirror-btn" id="mirror-x" title="Mirror X">X</button>
    <button class="mirror-btn" id="mirror-z" title="Mirror Z">Z</button>
    <button class="mirror-btn" id="mirror-y" title="Mirror Y">Y</button>
  </div>
  <div class="adv-row" style="color:#888;font-size:10px;">
    Edits mirror across selected axes
  </div>
  <h3 style="margin-top:12px;">Selection</h3>
  <div class="sel-info" id="sel-info">No selection</div>
  <div class="adv-row">
    <button class="console-btn" onclick="copySelection()">Copy</button>
    <button class="console-btn" onclick="pasteSelection()">Paste</button>
    <button class="console-btn" onclick="deleteSelection()">Delete</button>
  </div>
  <div class="adv-row">
    <button class="console-btn" onclick="clearSelection()">Clear Sel</button>
  </div>
  <div id="paste-controls" style="display:none;margin-top:8px;border:1px solid #444;border-radius:6px;padding:8px;">
    <div style="font-size:10px;color:#0ff;margin-bottom:6px;">📋 Paste Preview — Move & Confirm</div>
    <div class="adv-row" style="gap:4px;">
      <button class="console-btn" onclick="movePaste(-1,0,0)" style="padding:4px 8px;">X-</button>
      <button class="console-btn" onclick="movePaste(1,0,0)" style="padding:4px 8px;">X+</button>
      <button class="console-btn" onclick="movePaste(0,1,0)" style="padding:4px 8px;">Y+</button>
      <button class="console-btn" onclick="movePaste(0,-1,0)" style="padding:4px 8px;">Y-</button>
      <button class="console-btn" onclick="movePaste(0,0,-1)" style="padding:4px 8px;">Z-</button>
      <button class="console-btn" onclick="movePaste(0,0,1)" style="padding:4px 8px;">Z+</button>
    </div>
    <div class="adv-row" style="gap:4px;margin-top:4px;">
      <button class="console-btn" onclick="movePaste(-5,0,0)" style="padding:4px 8px;">X-5</button>
      <button class="console-btn" onclick="movePaste(5,0,0)" style="padding:4px 8px;">X+5</button>
      <button class="console-btn" onclick="movePaste(0,5,0)" style="padding:4px 8px;">Y+5</button>
      <button class="console-btn" onclick="movePaste(0,-5,0)" style="padding:4px 8px;">Y-5</button>
      <button class="console-btn" onclick="movePaste(0,0,-5)" style="padding:4px 8px;">Z-5</button>
      <button class="console-btn" onclick="movePaste(0,0,5)" style="padding:4px 8px;">Z+5</button>
    </div>
    <div class="adv-row" style="margin-top:6px;">
      <button class="console-btn run" onclick="confirmPaste()">✅ Confirm</button>
      <button class="console-btn" onclick="cancelPaste()">❌ Cancel</button>
    </div>
    <div style="font-size:9px;color:#888;margin-top:4px;">Arrow keys move | Enter=confirm | Esc=cancel</div>
  </div>
</div>

<!-- Replace material modal -->
<div id="replace-modal" style="position:fixed;top:0;left:0;width:100vw;height:100vh;background:rgba(0,0,0,0.7);display:none;align-items:center;justify-content:center;z-index:200;">
  <div style="background:#1e1e2e;border:1px solid #444;border-radius:12px;padding:24px;max-width:400px;width:90%;">
    <h2 style="color:#0ff;margin-bottom:12px;font-size:16px;">🔄 Replace Material</h2>
    <p style="font-size:12px;color:#aaa;margin-bottom:8px;">Replace all voxels of one material with another</p>
    <div style="display:flex;gap:12px;align-items:center;margin-bottom:12px;">
      <label style="font-size:11px;color:#aaa;">From:</label>
      <select id="replace-from" style="background:#222;color:#eee;border:1px solid #444;padding:4px;border-radius:4px;font-size:11px;"></select>
    </div>
    <div style="display:flex;gap:12px;align-items:center;margin-bottom:16px;">
      <label style="font-size:11px;color:#aaa;">To:</label>
      <select id="replace-to" style="background:#222;color:#eee;border:1px solid #444;padding:4px;border-radius:4px;font-size:11px;"></select>
    </div>
    <div style="display:flex;gap:8px;">
      <button class="console-btn run" onclick="doReplace()">Replace</button>
      <button class="console-btn" onclick="document.getElementById('replace-modal').style.display='none'">Cancel</button>
    </div>
  </div>
</div>

<!-- Script console modal -->
<div id="console-modal">
  <div id="console-box">
    <h2>⚙ Script Console</h2>
    <p>Run JavaScript to modify voxels. Available: W, H, D, voxelMap, getVoxelAt(x,y,z), setVoxel(x,y,z,mid), rebuildMesh()</p>
    <textarea id="console-input" placeholder="// Example: Fill layer Y=10 with material 109&#10;for (let x = 0; x < W; x++)&#10;  for (let z = 0; z < D; z++)&#10;    setVoxel(x, 10, z, 109);&#10;rebuildMesh();"></textarea>
    <div id="console-output"></div>
    <div style="display:flex;gap:8px;margin-top:8px;">
      <button class="console-btn run" onclick="runConsole()">Run</button>
      <button class="console-btn" onclick="document.getElementById('console-modal').style.display='none'">Close</button>
    </div>
  </div>
</div>

<!-- Expand volume modal -->
<div id="expand-modal" style="position:fixed;top:0;left:0;width:100vw;height:100vh;background:rgba(0,0,0,0.7);display:none;align-items:center;justify-content:center;z-index:200;">
  <div style="background:#2a2a3e;padding:24px;border-radius:12px;border:1px solid #444;width:400px;">
    <h2 style="margin:0 0 12px 0;">📐 Expand Volume</h2>
    <p style="color:#aaa;font-size:13px;margin:0 0 16px 0;">Add empty space to the grid. Use negative values to shift existing voxels (expand in that direction). Positive values add space on the far side.</p>
    <div style="display:grid;grid-template-columns:auto 1fr 1fr;gap:8px 12px;align-items:center;">
      <label style="color:#ccc;">Axis</label>
      <label style="color:#888;font-size:12px;">−Side (shift)</label>
      <label style="color:#888;font-size:12px;">+Side (add)</label>
      <label style="color:#ff6;">X (Width)</label>
      <input id="exp-x-neg" type="number" value="0" min="0" style="width:80px;background:#1a1a2e;color:#fff;border:1px solid #444;padding:4px;border-radius:4px;">
      <input id="exp-x-pos" type="number" value="0" min="0" style="width:80px;background:#1a1a2e;color:#fff;border:1px solid #444;padding:4px;border-radius:4px;">
      <label style="color:#6f6;">Y (Height)</label>
      <input id="exp-y-neg" type="number" value="0" min="0" style="width:80px;background:#1a1a2e;color:#fff;border:1px solid #444;padding:4px;border-radius:4px;">
      <input id="exp-y-pos" type="number" value="0" min="0" style="width:80px;background:#1a1a2e;color:#fff;border:1px solid #444;padding:4px;border-radius:4px;">
      <label style="color:#6af;">Z (Depth)</label>
      <input id="exp-z-neg" type="number" value="0" min="0" style="width:80px;background:#1a1a2e;color:#fff;border:1px solid #444;padding:4px;border-radius:4px;">
      <input id="exp-z-pos" type="number" value="0" min="0" style="width:80px;background:#1a1a2e;color:#fff;border:1px solid #444;padding:4px;border-radius:4px;">
    </div>
    <div id="exp-preview" style="color:#aaa;font-size:12px;margin:12px 0;"></div>
    <div style="display:flex;gap:8px;margin-top:8px;">
      <button onclick="doExpand()" style="background:#0a4;color:#fff;border:none;padding:8px 16px;border-radius:6px;cursor:pointer;">Apply</button>
      <button onclick="document.getElementById('expand-modal').style.display='none'" style="background:#444;color:#fff;border:none;padding:8px 16px;border-radius:6px;cursor:pointer;">Cancel</button>
    </div>
  </div>
</div>

<script type="importmap">
{{
  "imports": {{
    "three": "https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.module.js",
    "three/addons/": "https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/"
  }}
}}
</script>

<script type="module">
import * as THREE from 'three';
import {{ OrbitControls }} from 'three/addons/controls/OrbitControls.js';

// === DATA ===
const initialVoxels = {voxel_json};
const allMaterials = {all_mat_json};
let W = {w}, H = {h}, D = {d};

// Voxel store: Map of "x,y,z" -> materialId
const voxelMap = new Map();
for (const v of initialVoxels) {{
  voxelMap.set(`${{v[0]}},${{v[1]}},${{v[2]}}`, v[3]);
}}

// === SCENE ===
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1a1a2e);

const camera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.1, 5000);
const initialCamPos = new THREE.Vector3(W * 2, H * 1.5, D * 2);
camera.position.copy(initialCamPos);

const renderer = new THREE.WebGLRenderer({{ antialias: true }});
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
document.getElementById('canvas-container').appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.1;
controls.target.set(0, H / 2, 0);

// Lighting
scene.add(new THREE.AmbientLight(0x666688, 0.6));
const dirLight = new THREE.DirectionalLight(0xffffff, 1.0);
dirLight.position.set(W, H * 2, D);
scene.add(dirLight);
const dirLight2 = new THREE.DirectionalLight(0x88aaff, 0.4);
dirLight2.position.set(-W, H, -D);
scene.add(dirLight2);

// === GRID FLOOR ===
let gridHelper = new THREE.GridHelper(Math.max(W, D) * 2, Math.max(W, D), 0x444466, 0x222244);
gridHelper.position.set(0, 0, 0);
scene.add(gridHelper);

// === COMPASS ===
function buildCompass() {{
  const grp = new THREE.Group();
  grp.position.set(-W / 2 - 4, 0, -D / 2 - 4);
  const L = Math.max(W, H, D) * 0.25;
  const mkAxis = (color, dir) => {{
    const geo = new THREE.BufferGeometry().setFromPoints([
      new THREE.Vector3(0, 0, 0), dir.clone().multiplyScalar(L)
    ]);
    return new THREE.Line(geo, new THREE.LineBasicMaterial({{ color }}));
  }};
  grp.add(mkAxis(0xff3333, new THREE.Vector3(1, 0, 0)));
  grp.add(mkAxis(0x33ff33, new THREE.Vector3(0, 1, 0)));
  grp.add(mkAxis(0x3333ff, new THREE.Vector3(0, 0, 1)));
  scene.add(grp);
}}
buildCompass();

// === RAYCASTING ===
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
const dummyMatrix = new THREE.Matrix4();

// === HIGHLIGHT SYSTEM ===
// Multi-voxel highlight using a separate InstancedMesh
const hlGeo = new THREE.BoxGeometry(1.05, 1.05, 1.05);
let hlMesh = null;
let hlEdgesMesh = null;
let hlEdgesMesh2 = null; // for selection overlay when using other tools

const TOOL_COLORS = {{
  place: 0x00ff00,    // green
  erase: 0xff0000,    // red
  paint: 0xffaa00,    // orange
  eyedropper: 0x00aaff, // blue
  fill: 0x00ffff,     // cyan
  line: 0xffff00,     // yellow
  box: 0xff00ff,      // magenta
  select: 0xff8800,   // dark orange
  extrude: 0x88ff00,  // lime
  ruler: 0xffffff,    // white
  camera: 0x888888,   // gray
}};

function clearHighlight() {{
  if (hlMesh) {{ scene.remove(hlMesh); hlMesh.dispose(); hlMesh = null; }}
  if (hlEdgesMesh) {{ scene.remove(hlEdgesMesh); hlEdgesMesh.geometry.dispose(); hlEdgesMesh.material.dispose(); hlEdgesMesh = null; }}
  if (hlEdgesMesh2) {{ scene.remove(hlEdgesMesh2); hlEdgesMesh2.geometry.dispose(); hlEdgesMesh2.material.dispose(); hlEdgesMesh2 = null; }}
}}

function showHighlight(positions, color) {{
  clearHighlight();
  if (positions.length === 0) return;
  const count = positions.length;
  const mat = new THREE.MeshBasicMaterial({{ color, opacity: 0.35, transparent: true, depthWrite: false }});
  hlMesh = new THREE.InstancedMesh(hlGeo, mat, count);
  const edgesGeo = new THREE.EdgesGeometry(new THREE.BoxGeometry(1, 1, 1));
  const edgesMat = new THREE.LineBasicMaterial({{ color, transparent: true, opacity: 0.8 }});
  hlEdgesMesh = new THREE.InstancedMesh(edgesGeo, edgesMat, count);
  for (let i = 0; i < count; i++) {{
    const [x, y, z] = positions[i];
    dummyMatrix.setPosition(x - W / 2, y, z - D / 2);
    hlMesh.setMatrixAt(i, dummyMatrix);
    hlEdgesMesh.setMatrixAt(i, dummyMatrix);
  }}
  hlMesh.instanceMatrix.needsUpdate = true;
  hlEdgesMesh.instanceMatrix.needsUpdate = true;
  scene.add(hlMesh);
  scene.add(hlEdgesMesh);
}}

// Tool descriptions for status bar
const TOOL_DESC = {{
  place: 'Click a face to place a new voxel with selected material',
  erase: 'Click a voxel to remove it',
  paint: 'Click a voxel to recolor it with selected material',
  eyedropper: 'Click a voxel to pick its material (switches to paint)',
  fill: 'Click to fill — Cavity/Air/Replace modes | Shift=fill below clickY | Ctrl=fill above clickY',
  line: 'Click two points to draw a straight line of voxels',
  box: 'Click and drag to fill a rectangular volume',
  select: 'Click to flood-fill select same material | Shift+click for single voxel | Ctrl+click two points for box select',
  extrude: 'Click a face to extrude 1 layer | Shift+click+drag to extrude multiple layers',
  ruler: 'Click two points to measure distance between them',
  camera: 'Camera mode — orbit, pan, zoom. No editing. (Esc)',
}};

// Line/Box tool state
let lineStart = null;
let boxStart = null;
let boxDragging = false;

// Extrude drag state
let extrudeDragActive = false;
let extrudeDragFace = []; // face voxel positions
let extrudeDragNormal = [0, 0, 0];
let extrudeDragMid = 0;
let extrudeDragLayers = 1;

// Ruler state
let rulerStart = null;

// Mirror state
let mirrorX = false, mirrorY = false, mirrorZ = false;

// Selection state — stores list of selected voxel positions
let selVoxels = []; // [[x, y, z, mid], ...]
let selectionActive = false;
let clipboard = []; // [{x, y, z, mid}, ...]
let pasteOffset = {{ x: 0, y: 0, z: 0 }};
let pasteMode = false; // when true, showing paste preview
let pasteOrigin = {{ x: 0, y: 0, z: 0 }}; // base position for paste
let copyOrigin = {{ x: 0, y: 0, z: 0 }}; // where the original selection was

// Invisible plane for raycasting empty space (ground plane at y=0)
const planeGeo = new THREE.PlaneGeometry(W * 4, D * 4);
planeGeo.rotateX(-Math.PI / 2);
const groundPlane = new THREE.Mesh(planeGeo, new THREE.MeshBasicMaterial({{ visible: false }}));
groundPlane.position.set(0, 0, 0);
scene.add(groundPlane);

// === INSTANCED MESH ===
const boxGeo = new THREE.BoxGeometry(1, 1, 1);
const lambertMat = new THREE.MeshLambertMaterial();
let instancedMesh = null;
let edgesMesh = null;
let showEdges = true;

// Material visibility
const matVisible = new Map(); // materialId -> bool
for (const m of allMaterials) matVisible.set(m.id, true);

let selectedMaterial = allMaterials.length > 0 ? allMaterials[0].id : 109;
let currentTool = 'place';
let yMin = 0, yMax = H - 1;
let xMin = 0, xMax = W - 1;
let zMin = 0, zMax = D - 1;

// === HISTORY ===
const history = [];
let historyIndex = -1;
const MAX_HISTORY = 50;

function snapshot() {{
  return new Map(voxelMap);
}}

function pushHistory() {{
  // Truncate any redo states
  history.splice(historyIndex + 1);
  history.push(snapshot());
  if (history.length > MAX_HISTORY) history.shift();
  else historyIndex++;
  updateUndoRedo();
}}

function undo() {{
  if (historyIndex <= 0) return;
  historyIndex--;
  voxelMap.clear();
  for (const [k, v] of history[historyIndex]) voxelMap.set(k, v);
  rebuildMesh();
  updateUndoRedo();
}}

function redo() {{
  if (historyIndex >= history.length - 1) return;
  historyIndex++;
  voxelMap.clear();
  for (const [k, v] of history[historyIndex]) voxelMap.set(k, v);
  rebuildMesh();
  updateUndoRedo();
}}

function updateUndoRedo() {{
  document.getElementById('undo-btn').style.opacity = historyIndex > 0 ? '1' : '0.3';
  document.getElementById('redo-btn').style.opacity = historyIndex < history.length - 1 ? '1' : '0.3';
}}

// Initialize history
pushHistory();

// === MESH BUILDING ===
function getVisibleVoxels() {{
  const result = [];
  for (const [key, mid] of voxelMap) {{
    const [x, y, z] = key.split(',').map(Number);
    if (y < yMin || y > yMax) continue;
    if (x < xMin || x > xMax) continue;
    if (z < zMin || z > zMax) continue;
    if (!matVisible.get(mid)) continue;
    result.push({{ x, y, z, mid }});
  }}
  return result;
}}

function getMatColor(mid) {{
  const m = allMaterials.find(m => m.id === mid);
  if (m) return new THREE.Color(m.r / 255, m.g / 255, m.b / 255);
  return new THREE.Color(0.8, 0.8, 0.8);
}}

function rebuildMesh() {{
  invalidateExteriorCache();
  const visible = getVisibleVoxels();
  const count = visible.length;
  
  if (instancedMesh) {{
    scene.remove(instancedMesh);
    instancedMesh.dispose();
  }}
  if (edgesMesh) {{
    scene.remove(edgesMesh);
    edgesMesh.geometry.dispose();
    edgesMesh.material.dispose();
  }}
  
  if (count === 0) {{
    document.getElementById('visible-count').textContent = '0';
    return;
  }}
  
  instancedMesh = new THREE.InstancedMesh(boxGeo, lambertMat, count);
  instancedMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count * 3), 3);
  
  const color = new THREE.Color();
  for (let i = 0; i < count; i++) {{
    const v = visible[i];
    dummyMatrix.setPosition(v.x - W / 2, v.y, v.z - D / 2);
    instancedMesh.setMatrixAt(i, dummyMatrix);
    color.copy(getMatColor(v.mid));
    instancedMesh.setColorAt(i, color);
  }}
  instancedMesh.instanceMatrix.needsUpdate = true;
  if (instancedMesh.instanceColor) instancedMesh.instanceColor.needsUpdate = true;
  scene.add(instancedMesh);
  
  // Edges overlay
  if (showEdges) {{
    const edgesGeo = new THREE.EdgesGeometry(boxGeo);
    const edgesMat = new THREE.LineBasicMaterial({{ color: 0x000000, transparent: true, opacity: 0.25 }});
    edgesMesh = new THREE.InstancedMesh(edgesGeo, edgesMat, count);
    for (let i = 0; i < count; i++) {{
      const v = visible[i];
      dummyMatrix.setPosition(v.x - W / 2, v.y, v.z - D / 2);
      edgesMesh.setMatrixAt(i, dummyMatrix);
    }}
    edgesMesh.instanceMatrix.needsUpdate = true;
    scene.add(edgesMesh);
  }}
  
  document.getElementById('visible-count').textContent = count;
  document.getElementById('voxel-count').textContent = voxelMap.size;
}}

// === RAYCAST HELPERS ===
function getVoxelAt(x, y, z) {{
  return voxelMap.get(`${{x}},${{y}},${{z}}`) || 0;
}}

function setVoxel(x, y, z, mid) {{
  const positions = [[x, y, z]];
  // Mirror
  if (mirrorX) positions.push([W - 1 - x, y, z]);
  if (mirrorZ) positions.push([x, y, D - 1 - z]);
  if (mirrorY) positions.push([x, H - 1 - y, z]);
  if (mirrorX && mirrorZ) positions.push([W - 1 - x, y, D - 1 - z]);
  if (mirrorX && mirrorY) positions.push([W - 1 - x, H - 1 - y, z]);
  if (mirrorZ && mirrorY) positions.push([x, H - 1 - y, D - 1 - z]);
  if (mirrorX && mirrorY && mirrorZ) positions.push([W - 1 - x, H - 1 - y, D - 1 - z]);
  for (const [px, py, pz] of positions) {{
    if (px < 0 || px >= W || py < 0 || py >= H || pz < 0 || pz >= D) continue;
    if (mid === 0) {{
      voxelMap.delete(`${{px}},${{py}},${{pz}}`);
    }} else {{
      voxelMap.set(`${{px}},${{py}},${{pz}}`, mid);
    }}
  }}
}}

function raycastVoxel(event) {{
  pointer.set(
    (event.clientX / window.innerWidth) * 2 - 1,
    -(event.clientY / window.innerHeight) * 2 + 1
  );
  raycaster.setFromCamera(pointer, camera);
  
  const objects = [];
  if (instancedMesh) objects.push(instancedMesh);
  objects.push(groundPlane);
  
  const intersects = raycaster.intersectObjects(objects, false);
  if (intersects.length === 0) return null;
  
  const hit = intersects[0];
  
  if (hit.object === groundPlane) {{
    // Hit ground plane — return position for placement
    const p = hit.point;
    const x = Math.floor(p.x + W / 2);
    const z = Math.floor(p.z + D / 2);
    return {{ x, y: 0, z, normal: new THREE.Vector3(0, 1, 0), placeY: 0 }};
  }}
  
  // Hit instanced mesh — get instance ID
  const instanceId = hit.instanceId;
  const visible = getVisibleVoxels();
  if (instanceId >= visible.length) return null;
  
  const v = visible[instanceId];
  const normal = hit.face.normal.clone();
  
  // Position to place new voxel (adjacent in normal direction)
  const placeX = v.x + Math.round(normal.x);
  const placeY = v.y + Math.round(normal.y);
  const placeZ = v.z + Math.round(normal.z);
  
  return {{ x: v.x, y: v.y, z: v.z, normal, placeX, placeY, placeZ, mid: v.mid }};
}}

// === TOOL ACTIONS ===
function performTool(event) {{
  if (currentTool === 'camera') return;
  const hit = raycastVoxel(event);
  if (!hit) return;
  
  switch (currentTool) {{
    case 'place':
      if (hit.placeX !== undefined) {{
        const px = hit.placeX, py = hit.placeY, pz = hit.placeZ;
        if (px < 0 || px >= W || py < 0 || py >= H || pz < 0 || pz >= D) return;
        if (getVoxelAt(px, py, pz) !== 0) return;
        setVoxel(px, py, pz, selectedMaterial);
        pushHistory();
        rebuildMesh();
        setStatus(`Placed voxel at (${{px}},${{py}},${{pz}})`);
      }}
      break;
    
    case 'erase':
      if (selectionActive && selVoxels.length > 0) {{
        // Erase entire selection
        for (const v of selVoxels) {{
          voxelMap.delete(`${{v[0]}},${{v[1]}},${{v[2]}}`);
        }}
        pushHistory();
        rebuildMesh();
        setStatus(`Erased ${{selVoxels.length}} selected voxels`);
        clearSelection();
      }} else if (hit.mid !== undefined) {{
        setVoxel(hit.x, hit.y, hit.z, 0);
        pushHistory();
        rebuildMesh();
        setStatus(`Erased voxel at (${{hit.x}},${{hit.y}},${{hit.z}})`);
      }}
      break;
    
    case 'paint':
      if (selectionActive && selVoxels.length > 0) {{
        // Paint entire selection
        for (const v of selVoxels) {{
          voxelMap.set(`${{v[0]}},${{v[1]}},${{v[2]}}`, selectedMaterial);
        }}
        pushHistory();
        rebuildMesh();
        const m = allMaterials.find(m => m.id === selectedMaterial);
        setStatus(`Painted ${{selVoxels.length}} selected voxels with ${{m ? m.name : selectedMaterial}}`);
        clearSelection();
      }} else if (hit.mid !== undefined) {{
        setVoxel(hit.x, hit.y, hit.z, selectedMaterial);
        pushHistory();
        rebuildMesh();
        setStatus(`Painted voxel at (${{hit.x}},${{hit.y}},${{hit.z}})`);
      }}
      break;
    
    case 'eyedropper':
      if (hit.mid !== undefined) {{
        selectedMaterial = hit.mid;
        updateMaterialSelection();
        setStatus(`Picked material ${{hit.mid}}`);
        // Switch to paint after picking
        setTool('paint');
      }}
      break;
    
    case 'fill':
      if (hit.mid !== undefined || hit.placeX !== undefined) {{
        const mode = document.getElementById('fill-mode').value;
        // Y-clamp modifiers: Shift=fill below clickY, Ctrl=fill above clickY
        const clickY = hit.placeX !== undefined ? hit.placeY : hit.y;
        const yClamp = event.shiftKey ? 'below' : (event.ctrlKey ? 'above' : null);
        const clampLabel = yClamp === 'below' ? ` Y≤${{clickY}}` : (yClamp === 'above' ? ` Y≥${{clickY}}` : '');
        const clampFilter = (cells) => yClamp === 'below' ? cells.filter(c => c[1] <= clickY) : (yClamp === 'above' ? cells.filter(c => c[1] >= clickY) : cells);
        // For Shift mode: cast downward from click point to find the cavity floor, then flood-fill from there
        function getFillOrigin() {{
          if (yClamp === 'below' && hit.placeX !== undefined) {{
            // Cast downward from placement point to find first air cell
            let cy = hit.placeY;
            while (cy >= 0 && getVoxelAt(hit.placeX, cy, hit.placeZ) !== 0) cy--;
            if (cy >= 0) return [hit.placeX, cy, hit.placeZ];
            return [hit.placeX, hit.placeY, hit.placeZ];
          }}
          if (hit.placeX !== undefined) return [hit.placeX, hit.placeY, hit.placeZ];
          return [hit.x, hit.y, hit.z];
        }}
        if (mode === 'replace' && hit.mid !== undefined) {{
          const cells = clampFilter(computeFloodFill(hit.x, hit.y, hit.z, hit.mid));
          for (const [x, y, z] of cells) setVoxel(x, y, z, selectedMaterial);
          pushHistory();
          rebuildMesh();
          setStatus(`Replaced ${{cells.length}} voxels (mat ${{hit.mid}}→${{selectedMaterial}})${{clampLabel}}`);
        }} else if ((mode === 'cavity' || mode === 'air') && (hit.placeX !== undefined || hit.mid !== undefined)) {{
          const [ox, oy, oz] = getFillOrigin();
          if (getVoxelAt(ox, oy, oz) === 0) {{
            let fillCells = mode === 'cavity'
              ? clampFilter(computeCavity(ox, oy, oz))
              : clampFilter(computeFloodFillAir(ox, oy, oz));
            for (const [x, y, z] of fillCells) setVoxel(x, y, z, selectedMaterial);
            pushHistory();
            rebuildMesh();
            setStatus(`${{mode === 'cavity' ? 'Cavity' : 'Air'}} filled: ${{fillCells.length}} voxels${{clampLabel}}`);
          }} else if (hit.mid !== undefined) {{
            const cells = clampFilter(computeFloodFill(hit.x, hit.y, hit.z, hit.mid));
            for (const [x, y, z] of cells) setVoxel(x, y, z, selectedMaterial);
            pushHistory();
            rebuildMesh();
            setStatus(`Replaced ${{cells.length}} voxels (mat ${{hit.mid}}→${{selectedMaterial}})${{clampLabel}}`);
          }}
        }} else if (hit.mid !== undefined) {{
          const cells = clampFilter(computeFloodFill(hit.x, hit.y, hit.z, hit.mid));
          for (const [x, y, z] of cells) setVoxel(x, y, z, selectedMaterial);
          pushHistory();
          rebuildMesh();
          setStatus(`Replaced ${{cells.length}} voxels (mat ${{hit.mid}}→${{selectedMaterial}})${{clampLabel}}`);
        }}
      }}
      break;
    
    case 'line':
      if (!lineStart) {{
        // Set start point
        if (hit.placeX !== undefined) {{
          lineStart = [hit.placeX, hit.placeY, hit.placeZ];
        }} else {{
          lineStart = [hit.x, hit.y, hit.z];
        }}
        setStatus(`Line start set at (${{lineStart[0]}},${{lineStart[1]}},${{lineStart[2]}}) — click end point`);
      }} else {{
        // Place line
        let endX, endY, endZ;
        if (hit.placeX !== undefined) {{
          endX = hit.placeX; endY = hit.placeY; endZ = hit.placeZ;
        }} else {{
          endX = hit.x; endY = hit.y; endZ = hit.z;
        }}
        const lineVoxels = computeLineVoxels(lineStart[0], lineStart[1], lineStart[2], endX, endY, endZ);
        for (const [lx, ly, lz] of lineVoxels) {{
          if (lx >= 0 && lx < W && ly >= 0 && ly < H && lz >= 0 && lz < D) {{
            setVoxel(lx, ly, lz, selectedMaterial);
          }}
        }}
        pushHistory();
        rebuildMesh();
        setStatus(`Drew line: ${{lineVoxels.length}} voxels`);
        lineStart = null;
      }}
      break;
    
    case 'box':
      if (!boxStart) {{
        if (hit.placeX !== undefined) {{
          boxStart = [hit.placeX, hit.placeY, hit.placeZ];
        }} else {{
          boxStart = [hit.x, hit.y, hit.z];
        }}
        setStatus(`Box start set at (${{boxStart[0]}},${{boxStart[1]}},${{boxStart[2]}}) — click end point`);
      }} else {{
        let endX, endY, endZ;
        if (hit.placeX !== undefined) {{
          endX = hit.placeX; endY = hit.placeY; endZ = hit.placeZ;
        }} else {{
          endX = hit.x; endY = hit.y; endZ = hit.z;
        }}
        const boxVoxels = computeBoxVoxels(boxStart[0], boxStart[1], boxStart[2], endX, endY, endZ);
        for (const [bx, by, bz] of boxVoxels) {{
          if (bx >= 0 && bx < W && by >= 0 && by < H && bz >= 0 && bz < D) {{
            setVoxel(bx, by, bz, selectedMaterial);
          }}
        }}
        pushHistory();
        rebuildMesh();
        setStatus(`Filled box: ${{boxVoxels.length}} voxels`);
        boxStart = null;
      }}
      break;
    
    case 'select':
      if (hit.mid === undefined) {{
        setStatus('Click on a solid voxel to select connected mass | Shift=single | Ctrl=box');
        return;
      }}
      if (event.shiftKey) {{
        // Single voxel select — no flood fill
        selVoxels = [[hit.x, hit.y, hit.z, hit.mid]];
        selectionActive = true;
        document.getElementById('sel-info').textContent = `Sel: 1 voxel (mat ${{hit.mid}})`;
        setStatus(`Selected 1 voxel at (${{hit.x}},${{hit.y}},${{hit.z}}) — Shift+click for single, Ctrl+click+drag for box`);
      }} else if (event.ctrlKey) {{
        // Box select — select all non-air voxels within a 3D box from last click to this click
        if (!boxStart || boxStart[3] !== 'select-box') {{
          boxStart = [hit.x, hit.y, hit.z, 'select-box'];
          setStatus(`Box select start at (${{hit.x}},${{hit.y}},${{hit.z}}) — Ctrl+click end point`);
        }} else {{
          const [sx, sy, sz] = boxStart;
          const ex = hit.x, ey = hit.y, ez = hit.z;
          const minX = Math.min(sx, ex), maxX = Math.max(sx, ex);
          const minY = Math.min(sy, ey), maxY = Math.max(sy, ey);
          const minZ = Math.min(sz, ez), maxZ = Math.max(sz, ez);
          selVoxels = [];
          for (let x = minX; x <= maxX; x++)
            for (let y = minY; y <= maxY; y++)
              for (let z = minZ; z <= maxZ; z++) {{
                const mid = getVoxelAt(x, y, z);
                if (mid !== 0) selVoxels.push([x, y, z, mid]);
              }}
          selectionActive = true;
          document.getElementById('sel-info').textContent = `Sel: ${{selVoxels.length}} voxels (box)`;
          setStatus(`Box selected ${{selVoxels.length}} voxels from (${{minX}},${{minY}},${{minZ}}) to (${{maxX}},${{maxY}},${{maxZ}})`);
          boxStart = null;
        }}
      }} else {{
        // Flood-fill connected same-material voxels
        selVoxels = computeFloodFill(hit.x, hit.y, hit.z, hit.mid).map(p => [p[0], p[1], p[2], hit.mid]);
        selectionActive = true;
        const matName = allMaterials.find(m => m.id === hit.mid);
        document.getElementById('sel-info').textContent = `Sel: ${{selVoxels.length}} voxels (mat ${{hit.mid}})`;
        setStatus(`Selected ${{selVoxels.length}} connected voxels (mat ${{hit.mid}}${{matName ? ' ' + matName.name : ''}}) — Shift=single, Ctrl=box`);
      }}
      break;
    
    case 'extrude':
      if (hit.mid !== undefined) {{
        if (event.shiftKey) {{
          // Shift+click: start drag extrude — detect face, enter drag mode
          const nx = Math.round(hit.normal.x);
          const ny = Math.round(hit.normal.y);
          const nz = Math.round(hit.normal.z);
          extrudeDragFace = computeExtrudeFace(hit.x, hit.y, hit.z, hit.mid, nx, ny, nz);
          extrudeDragNormal = [nx, ny, nz];
          extrudeDragMid = hit.mid;
          extrudeDragLayers = 1;
          extrudeDragActive = true;
          isDragging = true; // prevent performTool from firing again on mouseup
          setStatus(`Extrude drag: ${{extrudeDragFace.length}} face voxels, move mouse along (${{nx}},${{ny}},${{nz}}) — release to commit`);
        }} else {{
          performExtrude(hit);
        }}
      }}
      break;
    
    case 'ruler':
      if (!rulerStart) {{
        if (hit.placeX !== undefined) {{
          rulerStart = [hit.placeX, hit.placeY, hit.placeZ];
        }} else {{
          rulerStart = [hit.x, hit.y, hit.z];
        }}
        setStatus(`Ruler start: (${{rulerStart[0]}},${{rulerStart[1]}},${{rulerStart[2]}}) — click end point`);
      }} else {{
        let endX, endY, endZ;
        if (hit.placeX !== undefined) {{
          endX = hit.placeX; endY = hit.placeY; endZ = hit.placeZ;
        }} else {{
          endX = hit.x; endY = hit.y; endZ = hit.z;
        }}
        const dx = endX - rulerStart[0], dy = endY - rulerStart[1], dz = endZ - rulerStart[2];
        const dist = Math.sqrt(dx*dx + dy*dy + dz*dz);
        const manhattan = Math.abs(dx) + Math.abs(dy) + Math.abs(dz);
        const lineVoxels = computeLineVoxels(rulerStart[0], rulerStart[1], rulerStart[2], endX, endY, endZ);
        setStatus(`📏 Distance: ${{dist.toFixed(2)}} | Voxels in line: ${{lineVoxels.length}} | Δ=(${{dx}},${{dy}},${{dz}}) | Manhattan: ${{manhattan}} | From (${{rulerStart[0]}},${{rulerStart[1]}},${{rulerStart[2]}}) to (${{endX}},${{endY}},${{endZ}})`);
        rulerStart = null;
      }}
      break;
  }}
}}

function floodFill(x, y, z, targetMid, replaceMid) {{
  if (targetMid === replaceMid) return;
  const stack = [[x, y, z]];
  const visited = new Set();
  while (stack.length > 0) {{
    const [cx, cy, cz] = stack.pop();
    const key = `${{cx}},${{cy}},${{cz}}`;
    if (visited.has(key)) continue;
    visited.add(key);
    if (getVoxelAt(cx, cy, cz) !== targetMid) continue;
    setVoxel(cx, cy, cz, replaceMid);
    stack.push([cx+1, cy, cz], [cx-1, cy, cz], [cx, cy+1, cz], [cx, cy-1, cz], [cx, cy, cz+1], [cx, cy, cz-1]);
  }}
}}

// === HIGHLIGHT PREVIEW ===
// Compute all "exterior" air cells: air reachable from the model grid boundary
let _exteriorAirCache = null;
let _exteriorAirVoxelCount = -1;
function invalidateExteriorCache() {{ _exteriorAirCache = null; }}
function computeExteriorAir() {{
  // Invalidate cache if voxel count changed
  if (_exteriorAirCache && _exteriorAirVoxelCount === voxelMap.size) return _exteriorAirCache;
  const exterior = new Set();
  const stack = [];
  // Seed from all air cells on the grid boundary
  for (let x = 0; x < W; x++) {{
    for (let z = 0; z < D; z++) {{
      if (getVoxelAt(x, 0, z) === 0) stack.push([x, 0, z]);
      if (getVoxelAt(x, H-1, z) === 0) stack.push([x, H-1, z]);
    }}
    for (let y = 0; y < H; y++) {{
      if (getVoxelAt(x, y, 0) === 0) stack.push([x, y, 0]);
      if (getVoxelAt(x, y, D-1) === 0) stack.push([x, y, D-1]);
    }}
  }}
  for (let z = 0; z < D; z++) {{
    for (let y = 0; y < H; y++) {{
      if (getVoxelAt(0, y, z) === 0) stack.push([0, y, z]);
      if (getVoxelAt(W-1, y, z) === 0) stack.push([W-1, y, z]);
    }}
  }}
  while (stack.length > 0) {{
    const [cx, cy, cz] = stack.pop();
    const key = `${{cx}},${{cy}},${{cz}}`;
    if (exterior.has(key)) continue;
    if (cx < 0 || cx >= W || cy < 0 || cy >= H || cz < 0 || cz >= D) continue;
    if (getVoxelAt(cx, cy, cz) !== 0) continue;
    exterior.add(key);
    stack.push([cx+1, cy, cz], [cx-1, cy, cz], [cx, cy+1, cz], [cx, cy-1, cz], [cx, cy, cz+1], [cx, cy, cz-1]);
  }}
  _exteriorAirCache = exterior;
  _exteriorAirVoxelCount = voxelMap.size;
  return exterior;
}}

function computeFloodFillAir(x, y, z) {{
  // Flood fill connected empty space (voxel value 0)
  if (getVoxelAt(x, y, z) !== 0) return [];
  const stack = [[x, y, z]];
  const visited = new Set();
  const result = [];
  while (stack.length > 0) {{
    const [cx, cy, cz] = stack.pop();
    const key = `${{cx}},${{cy}},${{cz}}`;
    if (visited.has(key)) continue;
    visited.add(key);
    if (cx < 0 || cx >= W || cy < 0 || cy >= H || cz < 0 || cz >= D) continue;
    if (getVoxelAt(cx, cy, cz) !== 0) continue;
    result.push([cx, cy, cz]);
    if (result.length > 10000) break; // safety limit
    stack.push([cx+1, cy, cz], [cx-1, cy, cz], [cx, cy+1, cz], [cx, cy-1, cz], [cx, cy, cz+1], [cx, cy, cz-1]);
  }}
  return result;
}}

// Cavity = connected air from click point, minus exterior air (true interior only)
function computeCavity(x, y, z) {{
  if (getVoxelAt(x, y, z) !== 0) return [];
  const exterior = computeExteriorAir();
  const airCells = computeFloodFillAir(x, y, z);
  return airCells.filter(([cx, cy, cz]) => !exterior.has(`${{cx}},${{cy}},${{cz}}`));
}}
function computeFloodFill(x, y, z, targetMid) {{
  const stack = [[x, y, z]];
  const visited = new Set();
  const result = [];
  while (stack.length > 0) {{
    const [cx, cy, cz] = stack.pop();
    const key = `${{cx}},${{cy}},${{cz}}`;
    if (visited.has(key)) continue;
    visited.add(key);
    if (getVoxelAt(cx, cy, cz) !== targetMid) continue;
    result.push([cx, cy, cz]);
    if (result.length > 50000) break; // safety limit
    stack.push([cx+1, cy, cz], [cx-1, cy, cz], [cx, cy+1, cz], [cx, cy-1, cz], [cx, cy, cz+1], [cx, cy, cz-1]);
  }}
  return result;
}}

function computeLineVoxels(x0, y0, z0, x1, y1, z1) {{
  const result = [];
  const dx = Math.abs(x1 - x0), dy = Math.abs(y1 - y0), dz = Math.abs(z1 - z0);
  const sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
  let x = x0, y = y0, z = z0;
  const steps = Math.max(dx, dy, dz);
  if (steps === 0) {{ result.push([x, y, z]); return result; }}
  const ex = dx / steps, ey = dy / steps, ez = dz / steps;
  let exAcc = 0, eyAcc = 0, ezAcc = 0;
  for (let i = 0; i <= steps; i++) {{
    result.push([Math.round(x), Math.round(y), Math.round(z)]);
    x += sx * (dx > 0 ? 1 : 0);
    y += sy * (dy > 0 ? 1 : 0);
    z += sz * (dz > 0 ? 1 : 0);
  }}
  // Bresenham-like: use parametric approach
  result.length = 0;
  for (let i = 0; i <= steps; i++) {{
    const t = i / steps;
    result.push([
      Math.round(x0 + (x1 - x0) * t),
      Math.round(y0 + (y1 - y0) * t),
      Math.round(z0 + (z1 - z0) * t)
    ]);
  }}
  return result;
}}

function computeBoxVoxels(x0, y0, z0, x1, y1, z1) {{
  const result = [];
  const minX = Math.min(x0, x1), maxX = Math.max(x0, x1);
  const minY = Math.min(y0, y1), maxY = Math.max(y0, y1);
  const minZ = Math.min(z0, z1), maxZ = Math.max(z0, z1);
  for (let x = minX; x <= maxX; x++)
    for (let y = minY; y <= maxY; y++)
      for (let z = minZ; z <= maxZ; z++)
        result.push([x, y, z]);
  return result;
}}

function updateHighlight(event) {{
  if (currentTool === 'camera') {{
    clearHighlight();
    document.getElementById('tooltip').style.display = 'none';
    return;
  }}
  const hit = raycastVoxel(event);
  if (!hit) {{
    clearHighlight();
    document.getElementById('tooltip').style.display = 'none';
    return;
  }}

  const color = TOOL_COLORS[currentTool] || 0x00ff00;
  let positions = [];
  let infoText = '';

  switch (currentTool) {{
    case 'place':
      if (hit.placeX !== undefined) {{
        positions = [[hit.placeX, hit.placeY, hit.placeZ]];
        infoText = `Place @ (${{hit.placeX}},${{hit.placeY}},${{hit.placeZ}})`;
      }}
      break;

    case 'erase':
      if (hit.mid !== undefined) {{
        positions = [[hit.x, hit.y, hit.z]];
        infoText = `Erase @ (${{hit.x}},${{hit.y}},${{hit.z}}) mat=${{hit.mid}}`;
      }}
      break;

    case 'paint':
      if (hit.mid !== undefined) {{
        positions = [[hit.x, hit.y, hit.z]];
        infoText = `Paint @ (${{hit.x}},${{hit.y}},${{hit.z}}) ${{hit.mid}}→${{selectedMaterial}}`;
      }}
      break;

    case 'eyedropper':
      if (hit.mid !== undefined) {{
        positions = [[hit.x, hit.y, hit.z]];
        const m = allMaterials.find(m => m.id === hit.mid);
        infoText = `Pick @ (${{hit.x}},${{hit.y}},${{hit.z}}) ${{m ? m.name : hit.mid}}`;
      }}
      break;

    case 'fill':
      if (hit.mid !== undefined || hit.placeX !== undefined) {{
        const mode = document.getElementById('fill-mode').value;
        const clickY = hit.placeX !== undefined ? hit.placeY : hit.y;
        const yClamp = event.shiftKey ? 'below' : (event.ctrlKey ? 'above' : null);
        const clampLabel = yClamp === 'below' ? ` Y≤${{clickY}}` : (yClamp === 'above' ? ` Y≥${{clickY}}` : '');
        const clampFilter = (cells) => yClamp === 'below' ? cells.filter(c => c[1] <= clickY) : (yClamp === 'above' ? cells.filter(c => c[1] >= clickY) : cells);
        const modHint = yClamp === 'below' ? ' [Shift: cast down + fill below]' : (yClamp === 'above' ? ' [Ctrl: fill above]' : ' [Shift/Ctrl: clamp Y]');
        // For Shift mode: cast downward to find cavity origin
        function getPreviewOrigin() {{
          if (yClamp === 'below' && hit.placeX !== undefined) {{
            let cy = hit.placeY;
            while (cy >= 0 && getVoxelAt(hit.placeX, cy, hit.placeZ) !== 0) cy--;
            if (cy >= 0) return [hit.placeX, cy, hit.placeZ];
            return [hit.placeX, hit.placeY, hit.placeZ];
          }}
          if (hit.placeX !== undefined) return [hit.placeX, hit.placeY, hit.placeZ];
          return [hit.x, hit.y, hit.z];
        }}
        if (mode === 'replace' && hit.mid !== undefined) {{
          const fillPositions = clampFilter(computeFloodFill(hit.x, hit.y, hit.z, hit.mid));
          positions = fillPositions;
          infoText = `Replace ${{fillPositions.length}} voxels (mat ${{hit.mid}} → ${{selectedMaterial}})${{clampLabel}}${{modHint}}`;
        }} else if ((mode === 'cavity' || mode === 'air') && (hit.placeX !== undefined || hit.mid !== undefined)) {{
          const [ox, oy, oz] = getPreviewOrigin();
          if (getVoxelAt(ox, oy, oz) === 0) {{
            let fillCells = mode === 'cavity'
              ? clampFilter(computeCavity(ox, oy, oz))
              : clampFilter(computeFloodFillAir(ox, oy, oz));
            positions = fillCells;
            infoText = `${{mode === 'cavity' ? 'Cavity' : 'Air'}} fill: ${{fillCells.length}} voxels${{clampLabel}}${{modHint}}`;
          }} else if (hit.mid !== undefined) {{
            const fillPositions = clampFilter(computeFloodFill(hit.x, hit.y, hit.z, hit.mid));
            positions = fillPositions;
            infoText = `Replace ${{fillPositions.length}} voxels (mat ${{hit.mid}} → ${{selectedMaterial}})${{clampLabel}} — switch to Replace or click${{modHint}}`;
          }} else {{
            infoText = `Click on a face to fill${{modHint}}`;
          }}
        }} else if (hit.mid !== undefined) {{
          const fillPositions = clampFilter(computeFloodFill(hit.x, hit.y, hit.z, hit.mid));
          positions = fillPositions;
          infoText = `Replace ${{fillPositions.length}} voxels (mat ${{hit.mid}} → ${{selectedMaterial}})${{clampLabel}}${{modHint}}`;
        }} else {{
          infoText = `Click on a face to fill${{modHint}}`;
        }}
      }}
      break;

    case 'line':
      if (lineStart) {{
        // Show line from start to current hover
        let endX, endY, endZ;
        if (hit.placeX !== undefined) {{
          endX = hit.placeX; endY = hit.placeY; endZ = hit.placeZ;
        }} else {{
          endX = hit.x; endY = hit.y; endZ = hit.z;
        }}
        positions = computeLineVoxels(lineStart[0], lineStart[1], lineStart[2], endX, endY, endZ);
        infoText = `Line: (${{lineStart[0]}},${{lineStart[1]}},${{lineStart[2]}}) → (${{endX}},${{endY}},${{endZ}}) = ${{positions.length}} voxels`;
      }} else {{
        // First click preview
        if (hit.placeX !== undefined) {{
          positions = [[hit.placeX, hit.placeY, hit.placeZ]];
          infoText = `Line start @ (${{hit.placeX}},${{hit.placeY}},${{hit.placeZ}}) — click to set`;
        }} else {{
          positions = [[hit.x, hit.y, hit.z]];
          infoText = `Line start @ (${{hit.x}},${{hit.y}},${{hit.z}}) — click to set`;
        }}
      }}
      break;

    case 'box':
      if (boxStart) {{
        let endX, endY, endZ;
        if (hit.placeX !== undefined) {{
          endX = hit.placeX; endY = hit.placeY; endZ = hit.placeZ;
        }} else {{
          endX = hit.x; endY = hit.y; endZ = hit.z;
        }}
        positions = computeBoxVoxels(boxStart[0], boxStart[1], boxStart[2], endX, endY, endZ);
        infoText = `Box: (${{boxStart[0]}},${{boxStart[1]}},${{boxStart[2]}}) → (${{endX}},${{endY}},${{endZ}}) = ${{positions.length}} voxels`;
      }} else {{
        if (hit.placeX !== undefined) {{
          positions = [[hit.placeX, hit.placeY, hit.placeZ]];
          infoText = `Box start @ (${{hit.placeX}},${{hit.placeY}},${{hit.placeZ}}) — click to set`;
        }} else {{
          positions = [[hit.x, hit.y, hit.z]];
          infoText = `Box start @ (${{hit.x}},${{hit.y}},${{hit.z}}) — click to set`;
        }}
      }}
      break;
    
    case 'select':
      if (selectionActive && selVoxels.length > 0) {{
        // Show frozen selection
        positions = selVoxels.map(v => [v[0], v[1], v[2]]);
        infoText = `Selection: ${{selVoxels.length}} voxels — click another to reselect, or use Copy/Delete`;
      }} else if (hit.mid !== undefined) {{
        // Preview what would be selected (flood fill of same material)
        const preview = computeFloodFill(hit.x, hit.y, hit.z, hit.mid);
        positions = preview;
        const m = allMaterials.find(m => m.id === hit.mid);
        infoText = `Will select ${{preview.length}} voxels (mat ${{hit.mid}}${{m ? ' ' + m.name : ''}}) — click to confirm`;
      }} else {{
        infoText = `Click on a solid voxel to select connected mass`;
      }}
      break;
    
    case 'ruler':
      if (rulerStart) {{
        let endX, endY, endZ;
        if (hit.placeX !== undefined) {{
          endX = hit.placeX; endY = hit.placeY; endZ = hit.placeZ;
        }} else {{
          endX = hit.x; endY = hit.y; endZ = hit.z;
        }}
        const dx = endX - rulerStart[0], dy = endY - rulerStart[1], dz = endZ - rulerStart[2];
        const dist = Math.sqrt(dx*dx + dy*dy + dz*dz);
        const manhattan = Math.abs(dx) + Math.abs(dy) + Math.abs(dz);
        positions = computeLineVoxels(rulerStart[0], rulerStart[1], rulerStart[2], endX, endY, endZ);
        infoText = `📏 ${{dist.toFixed(2)}} dist | ${{positions.length}} voxels | Δ=(${{dx}},${{dy}},${{dz}}) | Manh: ${{manhattan}} — click to measure`;
      }} else {{
        if (hit.placeX !== undefined) {{
          positions = [[hit.placeX, hit.placeY, hit.placeZ]];
          infoText = `Ruler start @ (${{hit.placeX}},${{hit.placeY}},${{hit.placeZ}}) — click to set`;
        }} else {{
          positions = [[hit.x, hit.y, hit.z]];
          infoText = `Ruler start @ (${{hit.x}},${{hit.y}},${{hit.z}}) — click to set`;
        }}
      }}
      break;
    
    case 'extrude':
      if (extrudeDragActive) {{
        // Show preview of extruded layers based on current drag amount
        positions = [];
        const [nx, ny, nz] = extrudeDragNormal;
        for (let layer = 1; layer <= extrudeDragLayers; layer++) {{
          for (const [fx, fy, fz] of extrudeDragFace) {{
            const px = fx + nx * layer, py = fy + ny * layer, pz = fz + nz * layer;
            if (px >= 0 && px < W && py >= 0 && py < H && pz >= 0 && pz < D) {{
              positions.push([px, py, pz]);
            }}
          }}
        }}
        infoText = `Extrude drag: ${{extrudeDragLayers}} layer(s) × ${{extrudeDragFace.length}} = ${{positions.length}} voxels dir=(${{nx}},${{ny}},${{nz}})`;
      }} else if (hit.mid !== undefined) {{
        positions = [[hit.x, hit.y, hit.z]];
        const nx = Math.round(hit.normal.x), ny = Math.round(hit.normal.y), nz = Math.round(hit.normal.z);
        if (hit.placeX !== undefined) {{
          positions.push([hit.placeX, hit.placeY, hit.placeZ]);
        }}
        const shiftHint = event.shiftKey ? ' [Shift+click to drag-extrude]' : ' [Hold Shift to drag-extrude]';
        infoText = `Extrude @ (${{hit.x}},${{hit.y}},${{hit.z}}) dir=(${{nx}},${{ny}},${{nz}})${{shiftHint}}`;
      }}
      break;
  }}

  // Paste mode preview — show ghost of clipboard voxels at current offset
  if (pasteMode && clipboard.length > 0) {{
    const bx = pasteOrigin.x + pasteOffset.x;
    const by = pasteOrigin.y + pasteOffset.y;
    const bz = pasteOrigin.z + pasteOffset.z;
    const pastePositions = [];
    for (const v of clipboard) {{
      const px = bx + v.x, py = by + v.y, pz = bz + v.z;
      if (px >= 0 && px < W && py >= 0 && py < H && pz >= 0 && pz < D) {{
        pastePositions.push([px, py, pz]);
      }}
    }}
    // Show paste preview in cyan
    showHighlight(pastePositions, 0x00ffff);
    infoText = `Paste preview: ${{pastePositions.length}} voxels at (${{bx}},${{by}},${{bz}}) — arrows=move | Enter=confirm | Esc=cancel`;
    // Update status coords
    document.getElementById('status-coords').textContent = `paste @ x:${{bx}} y:${{by}} z:${{bz}}`;
    if (infoText) {{
      const tt = document.getElementById('tooltip');
      tt.style.display = 'block';
      tt.style.left = (event.clientX + 12) + 'px';
      tt.style.top = (event.clientY + 12) + 'px';
      tt.textContent = infoText;
    }}
    return; // skip normal highlight when in paste mode
  }}

  // If selection is active and we're not using the select tool, show frozen selection
  if (selectionActive && selVoxels.length > 0 && currentTool !== 'select') {{
    const selPositions = selVoxels.map(v => [v[0], v[1], v[2]]);
    // Show both the tool highlight and the selection
    // Tool highlight in tool color, selection overlay in orange
    showHighlight(positions.concat(selPositions), color);
    // Re-render selection on top with orange edges
    if (selPositions.length > 0) {{
      // Add a second highlight pass for selection in orange
      const selColor = 0xff8800;
      const count = selPositions.length;
      const edgesGeo2 = new THREE.EdgesGeometry(new THREE.BoxGeometry(1, 1, 1));
      const edgesMat2 = new THREE.LineBasicMaterial({{ color: selColor, transparent: true, opacity: 0.9 }});
      const selEdges = new THREE.InstancedMesh(edgesGeo2, edgesMat2, count);
      for (let i = 0; i < count; i++) {{
        dummyMatrix.setPosition(selPositions[i][0] - W / 2, selPositions[i][1], selPositions[i][2] - D / 2);
        selEdges.setMatrixAt(i, dummyMatrix);
      }}
      selEdges.instanceMatrix.needsUpdate = true;
      scene.add(selEdges);
      // Store for cleanup on next highlight call
      hlEdgesMesh2 = selEdges;
    }}
  }} else {{
    showHighlight(positions, color);
  }}

  // Update status coords
  if (hit.placeX !== undefined) {{
    document.getElementById('status-coords').textContent =
      `x:${{hit.placeX}} y:${{hit.placeY}} z:${{hit.placeZ}}`;
  }} else if (hit.mid !== undefined) {{
    document.getElementById('status-coords').textContent =
      `x:${{hit.x}} y:${{hit.y}} z:${{hit.z}}`;
  }}

  // Tooltip
  if (infoText) {{
    const tt = document.getElementById('tooltip');
    tt.style.display = 'block';
    tt.style.left = (event.clientX + 12) + 'px';
    tt.style.top = (event.clientY + 12) + 'px';
    tt.textContent = infoText;
  }}
}}

// === UI WIRING ===
function setTool(tool) {{
  currentTool = tool;
  lineStart = null;
  boxStart = null;
  rulerStart = null;
  extrudeDragActive = false;
  extrudeDragFace = [];
  extrudeDragLayers = 1;
  if (pasteMode) {{ cancelPaste(); }}
  boxDragging = false;
  document.querySelectorAll('.tool-btn[data-tool]').forEach(btn => {{
    btn.classList.toggle('active', btn.dataset.tool === tool);
  }});
  document.getElementById('status-tool').textContent = `Tool: ${{tool.charAt(0).toUpperCase() + tool.slice(1)}}`;
  let desc = TOOL_DESC[tool] || '';
  if (selectionActive && selVoxels.length > 0 && (tool === 'erase' || tool === 'paint')) {{
    desc = `Selection active (${{selVoxels.length}} voxels) — click to apply to entire selection`;
  }}
  document.getElementById('status-info').textContent = desc;
  clearHighlight();
  // Re-render selection overlay immediately if active
  if (selectionActive && selVoxels.length > 0 && tool !== 'select') {{
    const selPositions = selVoxels.map(v => [v[0], v[1], v[2]]);
    const count = selPositions.length;
    const edgesGeo2 = new THREE.EdgesGeometry(new THREE.BoxGeometry(1, 1, 1));
    const edgesMat2 = new THREE.LineBasicMaterial({{ color: 0xff8800, transparent: true, opacity: 0.9 }});
    hlEdgesMesh2 = new THREE.InstancedMesh(edgesGeo2, edgesMat2, count);
    for (let i = 0; i < count; i++) {{
      dummyMatrix.setPosition(selPositions[i][0] - W / 2, selPositions[i][1], selPositions[i][2] - D / 2);
      hlEdgesMesh2.setMatrixAt(i, dummyMatrix);
    }}
    hlEdgesMesh2.instanceMatrix.needsUpdate = true;
    scene.add(hlEdgesMesh2);
  }}
}}

document.querySelectorAll('.tool-btn[data-tool]').forEach(btn => {{
  btn.addEventListener('click', () => setTool(btn.dataset.tool));
}});

// Undo/Redo buttons
document.getElementById('undo-btn').addEventListener('click', undo);
document.getElementById('redo-btn').addEventListener('click', redo);

// Grid toggle
document.getElementById('grid-btn').addEventListener('click', () => {{
  showEdges = !showEdges;
  document.getElementById('grid-btn').classList.toggle('active', showEdges);
  rebuildMesh();
}});

// Reset view
document.getElementById('reset-btn').addEventListener('click', () => {{
  camera.position.copy(initialCamPos);
  controls.target.set(0, H / 2, 0);
  controls.update();
}});

// Y-slice sliders
const yMaxSlider = document.getElementById('ySliceMax');
const yMinSlider = document.getElementById('ySliceMin');
const yMaxVal = document.getElementById('ymax-val');
const yMinVal = document.getElementById('ymin-val');

yMaxSlider.addEventListener('input', () => {{
  yMax = parseInt(yMaxSlider.value);
  yMaxVal.textContent = yMax;
  if (yMax < yMin) {{ yMin = yMax; yMinSlider.value = yMin; yMinVal.textContent = yMin; }}
  rebuildMesh();
  updateLayerInfo();
}});

yMinSlider.addEventListener('input', () => {{
  yMin = parseInt(yMinSlider.value);
  yMinVal.textContent = yMin;
  if (yMin > yMax) {{ yMax = yMin; yMaxSlider.value = yMax; yMaxVal.textContent = yMax; }}
  rebuildMesh();
  updateLayerInfo();
}});

// X-slice sliders
const xMaxSlider = document.getElementById('xSliceMax');
const xMinSlider = document.getElementById('xSliceMin');
const xMaxVal = document.getElementById('xmax-val');
const xMinVal = document.getElementById('xmin-val');

xMaxSlider.addEventListener('input', () => {{
  xMax = parseInt(xMaxSlider.value);
  xMaxVal.textContent = xMax;
  if (xMax < xMin) {{ xMin = xMax; xMinSlider.value = xMin; xMinVal.textContent = xMin; }}
  rebuildMesh();
  updateLayerInfo();
}});

xMinSlider.addEventListener('input', () => {{
  xMin = parseInt(xMinSlider.value);
  xMinVal.textContent = xMin;
  if (xMin > xMax) {{ xMax = xMin; xMaxSlider.value = xMax; xMaxVal.textContent = xMax; }}
  rebuildMesh();
  updateLayerInfo();
}});

// Z-slice sliders
const zMaxSlider = document.getElementById('zSliceMax');
const zMinSlider = document.getElementById('zSliceMin');
const zMaxVal = document.getElementById('zmax-val');
const zMinVal = document.getElementById('zmin-val');

zMaxSlider.addEventListener('input', () => {{
  zMax = parseInt(zMaxSlider.value);
  zMaxVal.textContent = zMax;
  if (zMax < zMin) {{ zMin = zMax; zMinSlider.value = zMin; zMinVal.textContent = zMin; }}
  rebuildMesh();
  updateLayerInfo();
}});

zMinSlider.addEventListener('input', () => {{
  zMin = parseInt(zMinSlider.value);
  zMinVal.textContent = zMin;
  if (zMin > zMax) {{ zMax = zMin; zMaxSlider.value = zMax; zMaxVal.textContent = zMax; }}
  rebuildMesh();
  updateLayerInfo();
}});

function updateLayerInfo() {{
  const allY = yMin === 0 && yMax === H - 1;
  const allX = xMin === 0 && xMax === W - 1;
  const allZ = zMin === 0 && zMax === D - 1;
  if (allY && allX && allZ) {{
    document.getElementById('layer-info').textContent = 'All layers visible';
  }} else {{
    const parts = [];
    if (!allY) parts.push(`Y=${{yMin}}-${{yMax}}`);
    if (!allX) parts.push(`X=${{xMin}}-${{xMax}}`);
    if (!allZ) parts.push(`Z=${{zMin}}-${{zMax}}`);
    document.getElementById('layer-info').textContent = parts.join(' | ');
  }}
}}

yMaxVal.textContent = yMax;
yMinVal.textContent = yMin;
xMaxVal.textContent = xMax;
xMinVal.textContent = xMin;
zMaxVal.textContent = zMax;
zMinVal.textContent = zMin;

// === MATERIAL PALETTE ===
function buildMaterialList() {{
  const container = document.getElementById('mat-list');
  container.innerHTML = '';
  
  // Show present materials first, then all others
  const presentIds = new Set();
  for (const v of voxelMap.values()) presentIds.add(v);
  
  const sorted = [...allMaterials].sort((a, b) => {{
    const aPresent = presentIds.has(a.id);
    const bPresent = presentIds.has(b.id);
    if (aPresent && !bPresent) return -1;
    if (!aPresent && bPresent) return 1;
    return a.id - b.id;
  }});
  
  for (const m of sorted) {{
    const count = [...voxelMap.values()].filter(v => v === m.id).length;
    const item = document.createElement('div');
    item.className = 'mat-item' + (m.id === selectedMaterial ? ' selected' : '');
    item.dataset.mid = m.id;
    item.innerHTML = `
      <div class="mat-swatch" style="background:${{m.hex}}"></div>
      <span class="mat-name">${{m.name}}</span>
      ${{count > 0 ? `<span class="mat-count">${{count}}</span>` : ''}}
      <div class="mat-vis ${{matVisible.get(m.id) ? '' : 'hidden'}}" data-mid="${{m.id}}">${{matVisible.get(m.id) ? '👁' : '🚫'}}</div>
    `;
    
    item.addEventListener('click', (e) => {{
      if (e.target.classList.contains('mat-vis')) {{
        const mid = parseInt(e.target.dataset.mid);
        matVisible.set(mid, !matVisible.get(mid));
        e.target.classList.toggle('hidden', !matVisible.get(mid));
        e.target.textContent = matVisible.get(mid) ? '👁' : '🚫';
        rebuildMesh();
      }} else {{
        selectedMaterial = m.id;
        updateMaterialSelection();
      }}
    }});
    
    container.appendChild(item);
  }}
}}

function updateMaterialSelection() {{
  document.querySelectorAll('.mat-item').forEach(item => {{
    item.classList.toggle('selected', parseInt(item.dataset.mid) === selectedMaterial);
  }});
  buildMaterialList();
}}

// === STATUS ===
function setStatus(msg) {{
  document.getElementById('status-info').textContent = msg;
}}

// === MOUSE EVENTS ===
let isDragging = false;
let mouseDownPos = null;
let lastMouseEvent = null;

renderer.domElement.addEventListener('mousedown', (e) => {{
  if (e.button === 0) {{
    mouseDownPos = {{ x: e.clientX, y: e.clientY }};
    isDragging = false;
    // If extrude tool + shift, start drag extrude via performTool
    if (currentTool === 'extrude' && e.shiftKey) {{
      const hit = raycastVoxel(e);
      if (hit && hit.mid !== undefined) {{
        const nx = Math.round(hit.normal.x);
        const ny = Math.round(hit.normal.y);
        const nz = Math.round(hit.normal.z);
        extrudeDragFace = computeExtrudeFace(hit.x, hit.y, hit.z, hit.mid, nx, ny, nz);
        extrudeDragNormal = [nx, ny, nz];
        extrudeDragMid = hit.mid;
        extrudeDragLayers = 1;
        extrudeDragActive = true;
        isDragging = true;
        setStatus(`Extrude drag: ${{extrudeDragFace.length}} face voxels — drag along (${{nx}},${{ny}},${{nz}}), release to commit`);
      }}
    }}
  }}
}});

renderer.domElement.addEventListener('mousemove', (e) => {{
  lastMouseEvent = e;
  if (mouseDownPos) {{
    const dx = e.clientX - mouseDownPos.x;
    const dy = e.clientY - mouseDownPos.y;
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) isDragging = true;
  }}
  // If extrude drag is active, compute layers from mouse movement
  if (extrudeDragActive && mouseDownPos) {{
    const dx = e.clientX - mouseDownPos.x;
    const dy = e.clientY - mouseDownPos.y;
    // Project mouse delta onto screen-space normal direction
    // Approximate: each normal axis contributes based on camera view
    // Simple heuristic: use dominant normal axis + mouse delta magnitude
    const [nx, ny, nz] = extrudeDragNormal;
    // Screen-space projection: Y-up normal → mouse dy; X/Z normal → mouse dx
    let projection = 0;
    if (ny !== 0) projection = -dy * ny; // dragging up = positive Y extrude
    else projection = dx; // sideways normals use dx
    // Also consider dy for X/Z normals when looking from front
    if (nx !== 0 || nz !== 0) {{
      // Use both dx and -dy for a more intuitive feel
      projection = dx - dy * 0.5;
    }}
    const layers = Math.max(1, Math.round(projection / 15)); // 15px per layer
    extrudeDragLayers = Math.min(layers, 50); // cap at 50 layers
  }}
  updateHighlight(e);
}});

renderer.domElement.addEventListener('mouseup', (e) => {{
  if (extrudeDragActive) {{
    // Commit the drag extrude
    const [nx, ny, nz] = extrudeDragNormal;
    let placed = 0;
    for (let layer = 1; layer <= extrudeDragLayers; layer++) {{
      for (const [fx, fy, fz] of extrudeDragFace) {{
        const px = fx + nx * layer, py = fy + ny * layer, pz = fz + nz * layer;
        if (px >= 0 && px < W && py >= 0 && py < H && pz >= 0 && pz < D) {{
          if (getVoxelAt(px, py, pz) === 0) {{
            voxelMap.set(`${{px}},${{py}},${{pz}}`, extrudeDragMid);
            placed++;
          }}
        }}
      }}
    }}
    pushHistory();
    rebuildMesh();
    setStatus(`Extruded ${{placed}} voxels (${{extrudeDragLayers}} layers × ${{extrudeDragFace.length}} face) dir=(${{nx}},${{ny}},${{nz}})`);
    extrudeDragActive = false;
    extrudeDragFace = [];
    extrudeDragLayers = 1;
    mouseDownPos = null;
    isDragging = false;
    return;
  }}
  if (e.button === 0 && !isDragging && !pasteMode) {{
    performTool(e);
  }}
  mouseDownPos = null;
  isDragging = false;
}});

renderer.domElement.addEventListener('mouseleave', () => {{
  clearHighlight();
  document.getElementById('tooltip').style.display = 'none';
}});

// === KEYBOARD SHORTCUTS ===
document.addEventListener('keydown', (e) => {{
  // Paste mode intercepts arrow keys, Enter, Escape
  if (pasteMode) {{
    if (e.key === 'ArrowLeft') {{ e.preventDefault(); movePaste(-1, 0, 0); if (lastMouseEvent) updateHighlight(lastMouseEvent); return; }}
    if (e.key === 'ArrowRight') {{ e.preventDefault(); movePaste(1, 0, 0); if (lastMouseEvent) updateHighlight(lastMouseEvent); return; }}
    if (e.key === 'ArrowUp') {{ e.preventDefault(); movePaste(0, 1, 0); if (lastMouseEvent) updateHighlight(lastMouseEvent); return; }}
    if (e.key === 'ArrowDown') {{ e.preventDefault(); movePaste(0, -1, 0); if (lastMouseEvent) updateHighlight(lastMouseEvent); return; }}
    if (e.key === 'Enter') {{ e.preventDefault(); confirmPaste(); return; }}
    if (e.key === 'Escape') {{ e.preventDefault(); cancelPaste(); return; }}
    if (e.key === '[') {{ e.preventDefault(); movePaste(0, 0, -1); if (lastMouseEvent) updateHighlight(lastMouseEvent); return; }}
    if (e.key === ']') {{ e.preventDefault(); movePaste(0, 0, 1); if (lastMouseEvent) updateHighlight(lastMouseEvent); return; }}
    return; // block other shortcuts during paste
  }}
  if (e.ctrlKey || e.metaKey) {{
    if (e.key === 'z') {{ e.preventDefault(); undo(); }}
    if (e.key === 'y') {{ e.preventDefault(); redo(); }}
    return;
  }}
  switch (e.key.toLowerCase()) {{
    case 'p': setTool('place'); break;
    case 'e': setTool('erase'); break;
    case 'b': setTool('paint'); break;
    case 'i': setTool('eyedropper'); break;
    case 'f': setTool('fill'); break;
    case 'l': setTool('line'); break;
    case 'x': setTool('box'); break;
    case 's': setTool('select'); break;
    case 'r': setTool('ruler'); break;
    case 'j': setTool('extrude'); break;
    case 'escape':
      lineStart = null;
      boxStart = null;
      rulerStart = null;
      extrudeDragActive = false;
      extrudeDragFace = [];
      extrudeDragLayers = 1;
      clearSelection();
      clearHighlight();
      setTool('camera');
      break;
  }}
}});

// === SELECTION TOOL ===
function getSelectionVoxels() {{
  return selVoxels.map(v => ({{ x: v[0], y: v[1], z: v[2], mid: v[3] }}));
}}

window.copySelection = function() {{
  if (selVoxels.length === 0) return;
  // Find min corner for relative positions
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  for (const v of selVoxels) {{
    minX = Math.min(minX, v[0]); minY = Math.min(minY, v[1]); minZ = Math.min(minZ, v[2]);
  }}
  clipboard = selVoxels.map(v => ({{ x: v[0] - minX, y: v[1] - minY, z: v[2] - minZ, mid: v[3] }}));
  copyOrigin = {{ x: minX, y: minY, z: minZ }};
  setStatus(`Copied ${{clipboard.length}} voxels to clipboard (origin: ${{minX}},${{minY}},${{minZ}})`);
}};

window.pasteSelection = function() {{
  if (clipboard.length === 0) return;
  // Enter paste preview mode — don't place yet
  pasteMode = true;
  // Start at copy origin so ghost appears where the original was
  pasteOrigin = {{ x: copyOrigin.x, y: copyOrigin.y, z: copyOrigin.z }};
  pasteOffset = {{ x: 0, y: 0, z: 0 }};
  document.getElementById('paste-controls').style.display = 'block';
  setStatus(`Paste mode: ${{clipboard.length}} voxels — use buttons/arrow keys to move, Enter to confirm, Esc to cancel`);
}};

window.movePaste = function(dx, dy, dz) {{
  if (!pasteMode) return;
  pasteOffset.x += dx; pasteOffset.y += dy; pasteOffset.z += dz;
  if (lastMouseEvent) updateHighlight(lastMouseEvent);
}};

window.confirmPaste = function() {{
  if (!pasteMode || clipboard.length === 0) return;
  const bx = pasteOrigin.x + pasteOffset.x;
  const by = pasteOrigin.y + pasteOffset.y;
  const bz = pasteOrigin.z + pasteOffset.z;
  let placed = 0;
  for (const v of clipboard) {{
    const px = bx + v.x, py = by + v.y, pz = bz + v.z;
    if (px >= 0 && px < W && py >= 0 && py < H && pz >= 0 && pz < D) {{
      voxelMap.set(`${{px}},${{py}},${{pz}}`, v.mid);
      placed++;
    }}
  }}
  pushHistory();
  rebuildMesh();
  setStatus(`Pasted ${{placed}} voxels at (${{bx}},${{by}},${{bz}})`);
  pasteMode = false;
  document.getElementById('paste-controls').style.display = 'none';
}};

window.cancelPaste = function() {{
  pasteMode = false;
  document.getElementById('paste-controls').style.display = 'none';
  setStatus('Paste cancelled');
}};

window.deleteSelection = function() {{
  if (selVoxels.length === 0) return;
  for (const v of selVoxels) {{
    voxelMap.delete(`${{v[0]}},${{v[1]}},${{v[2]}}`);
  }}
  pushHistory();
  rebuildMesh();
  setStatus(`Deleted ${{selVoxels.length}} voxels from selection`);
  clearSelection();
}};

window.clearSelection = function() {{
  selVoxels = [];
  selectionActive = false;
  document.getElementById('sel-info').textContent = 'No selection';
  clearHighlight();
}};

// === EXTRUDE TOOL ===
// Extract the connected face voxels for a given click + normal direction
function computeExtrudeFace(x, y, z, targetMid, nx, ny, nz) {{
  const faceVoxels = [];
  const stack = [[x, y, z]];
  const visited = new Set();
  while (stack.length > 0) {{
    const [cx, cy, cz] = stack.pop();
    const key = `${{cx}},${{cy}},${{cz}}`;
    if (visited.has(key)) continue;
    visited.add(key);
    if (getVoxelAt(cx, cy, cz) !== targetMid) continue;
    if (getVoxelAt(cx + nx, cy + ny, cz + nz) === 0) {{
      faceVoxels.push([cx, cy, cz]);
      const perpDirs = [];
      if (nx === 0) perpDirs.push([1, 0, 0], [-1, 0, 0]);
      if (ny === 0) perpDirs.push([0, 1, 0], [0, -1, 0]);
      if (nz === 0) perpDirs.push([0, 0, 1], [0, 0, -1]);
      for (const [dx, dy, dz] of perpDirs) {{
        stack.push([cx + dx, cy + dy, cz + dz]);
      }}
    }}
  }}
  return faceVoxels;
}}

// Click a face to extrude it outward by 1 voxel (copies the face layer)
function performExtrude(hit) {{
  if (hit.mid === undefined || !hit.normal) return;
  const nx = Math.round(hit.normal.x);
  const ny = Math.round(hit.normal.y);
  const nz = Math.round(hit.normal.z);
  // Find all voxels on the same face as the clicked voxel
  const faceVoxels = [];
  const stack = [[hit.x, hit.y, hit.z]];
  const visited = new Set();
  const targetMid = hit.mid;
  while (stack.length > 0) {{
    const [cx, cy, cz] = stack.pop();
    const key = `${{cx}},${{cy}},${{cz}}`;
    if (visited.has(key)) continue;
    visited.add(key);
    if (getVoxelAt(cx, cy, cz) !== targetMid) continue;
    // Check if this voxel is on the face (neighbor in normal direction is empty)
    const nKey = `${{cx + nx}},${{cy + ny}},${{cz + nz}}`;
    if (getVoxelAt(cx + nx, cy + ny, cz + nz) === 0) {{
      faceVoxels.push([cx, cy, cz]);
      // Flood fill in the plane perpendicular to normal
      const perpDirs = [];
      if (nx === 0) perpDirs.push([1, 0, 0], [-1, 0, 0]);
      if (ny === 0) perpDirs.push([0, 1, 0], [0, -1, 0]);
      if (nz === 0) perpDirs.push([0, 0, 1], [0, 0, -1]);
      for (const [dx, dy, dz] of perpDirs) {{
        stack.push([cx + dx, cy + dy, cz + dz]);
      }}
    }}
  }}
  // Place new voxels in the normal direction
  for (const [fx, fy, fz] of faceVoxels) {{
    const px = fx + nx, py = fy + ny, pz = fz + nz;
    if (px >= 0 && px < W && py >= 0 && py < H && pz >= 0 && pz < D) {{
      voxelMap.set(`${{px}},${{py}},${{pz}}`, targetMid);
    }}
  }}
  pushHistory();
  rebuildMesh();
  setStatus(`Extruded ${{faceVoxels.length}} voxels along (${{nx}},${{ny}},${{nz}})`);
}}

// === MATERIAL REPLACE ===
window.doReplace = function() {{
  const fromMid = parseInt(document.getElementById('replace-from').value);
  const toMid = parseInt(document.getElementById('replace-to').value);
  if (fromMid === toMid) return;
  let count = 0;
  for (const [key, mid] of voxelMap) {{
    if (mid === fromMid) {{
      voxelMap.set(key, toMid);
      count++;
    }}
  }}
  pushHistory();
  rebuildMesh();
  buildMaterialList();
  document.getElementById('replace-modal').style.display = 'none';
  setStatus(`Replaced ${{count}} voxels: ${{fromMid}} → ${{toMid}}`);
}};

function populateReplaceDropdowns() {{
  const presentIds = new Set();
  for (const v of voxelMap.values()) presentIds.add(v);
  const fromSel = document.getElementById('replace-from');
  const toSel = document.getElementById('replace-to');
  fromSel.innerHTML = '';
  toSel.innerHTML = '';
  for (const m of allMaterials) {{
    if (presentIds.has(m.id)) {{
      fromSel.innerHTML += `<option value="${{m.id}}">${{m.name}} (${{m.id}})</option>`;
    }}
    toSel.innerHTML += `<option value="${{m.id}}">${{m.name}} (${{m.id}})</option>`;
  }}
}}

// === SCRIPT CONSOLE ===
window.runConsole = function() {{
  const code = document.getElementById('console-input').value;
  const output = document.getElementById('console-output');
  try {{
    const fn = new Function('W', 'H', 'D', 'voxelMap', 'getVoxelAt', 'setVoxel', 'rebuildMesh', 'pushHistory', code);
    const result = fn(W, H, D, voxelMap, getVoxelAt, setVoxel, rebuildMesh, pushHistory);
    output.textContent = result !== undefined ? String(result) : 'OK';
    pushHistory();
    rebuildMesh();
    buildMaterialList();
  }} catch (e) {{
    output.textContent = 'Error: ' + e.message;
  }}
}};

// === MIRROR BUTTONS ===
document.getElementById('mirror-btn').addEventListener('click', () => {{
  const panel = document.getElementById('adv-panel');
  panel.style.display = panel.style.display === 'none' ? 'block' : 'none';
}});
document.getElementById('mirror-x').addEventListener('click', () => {{
  mirrorX = !mirrorX;
  document.getElementById('mirror-x').classList.toggle('active', mirrorX);
  setStatus(`Mirror X: ${{mirrorX ? 'ON' : 'OFF'}}`);
}});
document.getElementById('mirror-z').addEventListener('click', () => {{
  mirrorZ = !mirrorZ;
  document.getElementById('mirror-z').classList.toggle('active', mirrorZ);
  setStatus(`Mirror Z: ${{mirrorZ ? 'ON' : 'OFF'}}`);
}});
document.getElementById('mirror-y').addEventListener('click', () => {{
  mirrorY = !mirrorY;
  document.getElementById('mirror-y').classList.toggle('active', mirrorY);
  setStatus(`Mirror Y: ${{mirrorY ? 'ON' : 'OFF'}}`);
}});

// === REPLACE BUTTON ===
document.getElementById('replace-btn').addEventListener('click', () => {{
  populateReplaceDropdowns();
  document.getElementById('replace-modal').style.display = 'flex';
}});

// === CONSOLE BUTTON ===
document.getElementById('console-btn').addEventListener('click', () => {{
  document.getElementById('console-modal').style.display = 'flex';
}});

// === EXPAND VOLUME ===
function updateExpandPreview() {{
  const xn = parseInt(document.getElementById('exp-x-neg').value) || 0;
  const xp = parseInt(document.getElementById('exp-x-pos').value) || 0;
  const yn = parseInt(document.getElementById('exp-y-neg').value) || 0;
  const yp = parseInt(document.getElementById('exp-y-pos').value) || 0;
  const zn = parseInt(document.getElementById('exp-z-neg').value) || 0;
  const zp = parseInt(document.getElementById('exp-z-pos').value) || 0;
  const newW = W + xn + xp, newH = H + yn + yp, newD = D + zn + zp;
  document.getElementById('exp-preview').textContent =
    `Current: ${{W}}x${{H}}x${{D}} → New: ${{newW}}x${{newH}}x${{newD}} (${{newW*newH*newD}} total voxels)`;
}}
document.getElementById('expand-btn').addEventListener('click', () => {{
  document.getElementById('expand-modal').style.display = 'flex';
  updateExpandPreview();
}});
['exp-x-neg','exp-x-pos','exp-y-neg','exp-y-pos','exp-z-neg','exp-z-pos'].forEach(id => {{
  document.getElementById(id).addEventListener('input', updateExpandPreview);
}});

window.doExpand = function() {{
  const xn = parseInt(document.getElementById('exp-x-neg').value) || 0;
  const xp = parseInt(document.getElementById('exp-x-pos').value) || 0;
  const yn = parseInt(document.getElementById('exp-y-neg').value) || 0;
  const yp = parseInt(document.getElementById('exp-y-pos').value) || 0;
  const zn = parseInt(document.getElementById('exp-z-neg').value) || 0;
  const zp = parseInt(document.getElementById('exp-z-pos').value) || 0;
  if (xn + xp + yn + yp + zn + zp === 0) {{
    document.getElementById('expand-modal').style.display = 'none';
    return;
  }}
  // Shift existing voxels by (xn, yn, zn)
  if (xn > 0 || yn > 0 || zn > 0) {{
    const entries = Array.from(voxelMap.entries());
    voxelMap.clear();
    for (const [key, mid] of entries) {{
      const [x, y, z] = key.split(',').map(Number);
      voxelMap.set(`${{x + xn}},${{y + yn}},${{z + zn}}`, mid);
    }}
  }}
  W += xn + xp;
  H += yn + yp;
  D += zn + zp;
  // Update grid floor
  scene.remove(gridHelper);
  gridHelper.geometry.dispose();
  gridHelper.material.dispose();
  gridHelper = new THREE.GridHelper(Math.max(W, D) * 2, Math.max(W, D), 0x444466, 0x222244);
  gridHelper.position.set(0, 0, 0);
  scene.add(gridHelper);
  // Update camera target
  controls.target.set(0, H / 2, 0);
  controls.update();
  pushHistory();
  rebuildMesh();
  setStatus(`Expanded to ${{W}}x${{H}}x${{D}}`);
  document.getElementById('expand-modal').style.display = 'none';
}};

// === SAVE / LOAD / EXPORT ===
// Collect current editor state into a JSON object
function getEditorData() {{
  return {{
    dims: [W, H, D],
    materials: allMaterials,
    voxels: Array.from(voxelMap.entries()).map(([k, v]) => {{
      const [x, y, z] = k.split(',').map(Number);
      return [x, y, z, v];
    }})
  }};
}}

// Save project — full editor state as downloadable JSON
window.saveProject = function() {{
  const data = getEditorData();
  data.format = 'voxel_editor_project';
  data.version = 1;
  data.savedAt = new Date().toISOString();
  const blob = new Blob([JSON.stringify(data, null, 2)], {{ type: 'application/json' }});
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `voxel_project_${{new Date().toISOString().slice(0,10)}}.json`;
  a.click();
  setStatus(`Project saved: ${{data.voxels.length}} voxels`);
}};

// Load project from JSON file
window.loadProject = function(event) {{
  const file = event.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = (e) => {{
    try {{
      const data = JSON.parse(e.target.result);
      if (!data.dims || !data.voxels) throw new Error('Invalid project file');
      // Clear current state
      voxelMap.clear();
      for (const [x, y, z, mid] of data.voxels) {{
        voxelMap.set(`${{x}},${{y}},${{z}}`, mid);
      }}
      pushHistory();
      rebuildMesh();
      buildMaterialList();
      setStatus(`Loaded project: ${{data.voxels.length}} voxels, dims ${{data.dims[0]}}x${{data.dims[1]}}x${{data.dims[2]}}`);
      closeExport();
    }} catch (err) {{
      setStatus(`Load failed: ${{err.message}}`);
    }}
  }};
  reader.readAsText(file);
  event.target.value = ''; // reset for reuse
}};

// Import .stasset JSON (same format as export — dims + voxels array)
window.importStasset = function(event) {{
  const file = event.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = (e) => {{
    try {{
      const data = JSON.parse(e.target.result);
      if (!data.dims || !data.voxels) throw new Error('Invalid stasset JSON');
      voxelMap.clear();
      for (const [x, y, z, mid] of data.voxels) {{
        if (mid !== 0) voxelMap.set(`${{x}},${{y}},${{z}}`, mid);
      }}
      pushHistory();
      rebuildMesh();
      buildMaterialList();
      setStatus(`Imported ${{data.voxels.length}} voxels from .stasset JSON`);
      closeExport();
    }} catch (err) {{
      setStatus(`Import failed: ${{err.message}}`);
    }}
  }};
  reader.readAsText(file);
  event.target.value = '';
}};

// Export .stasset-compatible JSON for Python conversion
window.exportStassetJSON = function() {{
  const data = getEditorData();
  data.format = 'stasset_export';
  const blob = new Blob([JSON.stringify(data, null, 2)], {{ type: 'application/json' }});
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `voxel_export_${{new Date().toISOString().slice(0,10)}}.json`;
  a.click();
  setStatus(`Exported ${{data.voxels.length}} voxels to .stasset JSON`);
}};

// Show raw JSON in textarea
window.exportRawJSON = function() {{
  const ta = document.getElementById('export-json');
  ta.style.display = ta.style.display === 'none' ? 'block' : 'none';
  if (ta.style.display !== 'none') {{
    ta.value = JSON.stringify(getEditorData(), null, 2);
  }}
}};

// Legacy compat
document.getElementById('export-btn').addEventListener('click', () => {{
  document.getElementById('export-modal').style.display = 'flex';
}});

window.closeExport = function() {{
  document.getElementById('export-modal').style.display = 'none';
  const ta = document.getElementById('export-json');
  ta.style.display = 'none';
}};

// === RESIZE ===
window.addEventListener('resize', () => {{
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
}});

// === FPS ===
let lastTime = performance.now();
let frameCount = 0;
function updateFPS() {{
  frameCount++;
  const now = performance.now();
  if (now - lastTime >= 1000) {{
    document.getElementById('status-fps').textContent = `${{frameCount}} FPS`;
    frameCount = 0;
    lastTime = now;
  }}
}}

// === RENDER LOOP ===
function animate() {{
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
  updateFPS();
}}

// === INIT ===
buildMaterialList();
rebuildMesh();
animate();
</script>
</body>
</html>'''
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(html)
    print(f"Saved {filepath}")
    print(f"  Dims: {w}x{h}x{d} | Voxels: {len(voxel_data)} | Materials: {len(all_materials)}")

if __name__ == "__main__":
    infile = sys.argv[1] if len(sys.argv) > 1 else "fire_escape_example1_expanded2.stasset"
    outfile = sys.argv[2] if len(sys.argv) > 2 else "voxel_editor.html"
    
    v, dims, scale, meta = load_stasset_full(infile)
    title = infile.replace('.stasset', '').replace('_', ' ').title()
    voxel_to_editor(v, dims, outfile, title=title)
    print(f"Open in browser: {outfile}")
