"""Generate an interactive Three.js HTML preview of a .stasset file.
Uses InstancedMesh for performant voxel rendering with OrbitControls.
Usage: python render_html.py <file.stasset> [output.html]
"""
import sys
import numpy as np
from stasset_io import load_stasset_full
from material_library import get_material_color, get_material_name

def voxel_to_html(voxels, dims, filepath, title="Voxel Preview"):
    w, h, d = dims
    colors = {}
    for mid in np.unique(voxels):
        if mid == 0:
            continue
        r, g, b, a = get_material_color(int(mid))
        colors[int(mid)] = (int(r*255), int(g*255), int(b*255), get_material_name(int(mid)))
    
    voxel_list = []
    for y in range(h):
        for z in range(d):
            for x in range(w):
                mid = int(voxels[x, y, z])
                if mid == 0:
                    continue
                is_surface = False
                for dx, dy, dz in [(-1,0,0),(1,0,0),(0,-1,0),(0,1,0),(0,0,-1),(0,0,1)]:
                    nx, ny, nz = x+dx, y+dy, z+dz
                    if nx<0 or nx>=w or ny<0 or ny>=h or nz<0 or nz>=d:
                        is_surface = True
                        break
                    if voxels[nx, ny, nz] == 0:
                        is_surface = True
                        break
                if not is_surface:
                    continue
                r, g, b, name = colors.get(mid, (200, 200, 200, "Unknown"))
                voxel_list.append({"x": x, "y": y, "z": z, "r": r, "g": g, "b": b, "mid": mid, "name": name})
    
    legend_items = []
    for mid in sorted(colors.keys()):
        r, g, b, name = colors[mid]
        count = int(np.count_nonzero(voxels == mid))
        hex_c = f"#{r:02x}{g:02x}{b:02x}"
        legend_items.append(f'<div class="legend-item"><div class="legend-swatch" style="background:{hex_c}"></div><span>{name} ({mid}) - {count}v</span></div>')
    
    voxel_json = str([{"x":v["x"],"y":v["y"],"z":v["z"],"r":v["r"],"g":v["g"],"b":v["b"],"mid":v["mid"]} for v in voxel_list])
    
    html = f'''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>{title}</title>
<style>
* {{ margin:0; padding:0; box-sizing:border-box; }}
body {{ background:#1a1a2e; color:#eee; font-family:monospace; overflow:hidden; }}
#canvas-container {{ width:100vw; height:100vh; }}
#controls {{
  position:fixed; top:10px; left:10px;
  background:rgba(0,0,0,0.8); padding:15px; border-radius:8px;
  font-size:12px; max-width:280px; max-height:90vh; overflow-y:auto;
  z-index:100;
}}
#controls h2 {{ font-size:14px; margin-bottom:8px; color:#0ff; }}
#controls label {{ display:block; margin:5px 0; }}
#controls input[type=range] {{ width:100%; }}
.legend-item {{ display:flex; align-items:center; gap:6px; margin:3px 0; font-size:11px; }}
.legend-swatch {{ width:14px; height:14px; border-radius:2px; border:1px solid #333; flex-shrink:0; }}
.info {{ color:#0f0; margin-top:4px; font-size:11px; }}
.btn {{ background:#333; color:#eee; border:1px solid #555; padding:4px 10px; border-radius:4px; cursor:pointer; margin:2px; font-size:11px; }}
.btn:hover {{ background:#444; }}
.btn.active {{ background:#0a4; }}
.compass-info {{ font-size:11px; margin:4px 0; }}
.compass-info span {{ font-weight:bold; }}
.cx {{ color:#e33; }}
.cy {{ color:#3e3; }}
.cz {{ color:#33e; }}
</style>
</head>
<body>
<div id="canvas-container"></div>
<div id="controls">
  <h2>{title}</h2>
  <p style="font-size:11px;color:#aaa;margin-bottom:8px;">{w}x{h}x{d} | {np.count_nonzero(voxels)} voxels | {len(voxel_list)} visible</p>
  
  <label>Y Slice (max): <input type="range" id="ySlice" min="0" max="{h-1}" value="{h-1}" oninput="updateSlice()"></label>
  <label>Y Slice (min): <input type="range" id="ySliceMin" min="0" max="{h-1}" value="0" oninput="updateSlice()"></label>
  
  <div style="margin:8px 0;">
    <button class="btn" onclick="resetView()">Reset View</button>
    <button class="btn" onclick="toggleWireframe()" id="wfBtn">Wireframe</button>
    <button class="btn active" onclick="toggleEdges()" id="edgeBtn">Grid Lines</button>
  </div>
  
  <div class="info" id="sliceInfo">All layers</div>
  <div class="compass-info">
    <span class="cx">X</span>=red &nbsp;
    <span class="cy">Y</span>=green(up) &nbsp;
    <span class="cz">Z</span>=blue
  </div>
  
  <h3 style="margin-top:10px;font-size:12px;color:#0ff;">Materials</h3>
{chr(10).join("    " + li for li in legend_items)}
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

const voxelData = {voxel_json};
const W = {w}, H = {h}, D = {d};

// Scene setup
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1a1a2e);

const camera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.1, 2000);
const initialCamPos = new THREE.Vector3(W * 1.8, H * 1.2, D * 1.8);
camera.position.copy(initialCamPos);

const renderer = new THREE.WebGLRenderer({{
  canvas: document.createElement('canvas'),
  antialias: true,
  preserveDrawingBuffer: true
}});
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
document.getElementById('canvas-container').appendChild(renderer.domElement);

// Controls
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.1;
controls.target.set(0, H / 2, 0);

// Lighting
const ambient = new THREE.AmbientLight(0x666688, 0.6);
scene.add(ambient);

const dirLight = new THREE.DirectionalLight(0xffffff, 1.0);
dirLight.position.set(W, H * 2, D);
scene.add(dirLight);

const dirLight2 = new THREE.DirectionalLight(0x88aaff, 0.4);
dirLight2.position.set(-W, H, -D);
scene.add(dirLight2);

// Build instanced mesh
const geometry = new THREE.BoxGeometry(1, 1, 1);
const material = new THREE.MeshLambertMaterial({{ vertexColors: false }});
let instancedMesh = null;
let edgesMesh = null;
let wireframeMesh = null;
let wireframeMode = false;
let showEdges = true;

function buildMesh(yMin, yMax) {{
  const visible = voxelData.filter(v => v.y >= yMin && v.y <= yMax);
  const count = visible.length;
  
  if (instancedMesh) {{
    scene.remove(instancedMesh);
    instancedMesh.geometry.dispose();
    instancedMesh.dispose();
  }}
  
  if (count === 0) return;
  
  instancedMesh = new THREE.InstancedMesh(geometry, material, count);
  instancedMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count * 3), 3);
  
  const matrix = new THREE.Matrix4();
  const color = new THREE.Color();
  
  for (let i = 0; i < count; i++) {{
    const v = visible[i];
    // Center the model: X right, Y up, Z forward
    matrix.setPosition(v.x - W / 2, v.y, v.z - D / 2);
    instancedMesh.setMatrixAt(i, matrix);
    color.setRGB(v.r / 255, v.g / 255, v.b / 255);
    instancedMesh.setColorAt(i, color);
  }}
  
  instancedMesh.instanceMatrix.needsUpdate = true;
  if (instancedMesh.instanceColor) instancedMesh.instanceColor.needsUpdate = true;
  scene.add(instancedMesh);
  
  // Build edges overlay for grid visibility
  if (edgesMesh) {{
    scene.remove(edgesMesh);
    edgesMesh.geometry.dispose();
    edgesMesh.material.dispose();
  }}
  if (showEdges) {{
    const edgesGeo = new THREE.EdgesGeometry(geometry);
    const edgesMat = new THREE.LineBasicMaterial({{ color: 0x000000, transparent: true, opacity: 0.3 }});
    edgesMesh = new THREE.InstancedMesh(edgesGeo, edgesMat, count);
    for (let i = 0; i < count; i++) {{
      const v = visible[i];
      matrix.setPosition(v.x - W / 2, v.y, v.z - D / 2);
      edgesMesh.setMatrixAt(i, matrix);
    }}
    edgesMesh.instanceMatrix.needsUpdate = true;
    scene.add(edgesMesh);
  }}
}}

// Compass axes
function buildCompass() {{
  const axisGroup = new THREE.Group();
  axisGroup.position.set(-W / 2 - 3, 0, -D / 2 - 3);
  
  const axisLen = Math.max(W, H, D) * 0.3;
  
  // X axis (red)
  const xMat = new THREE.LineBasicMaterial({{ color: 0xff3333 }});
  const xGeo = new THREE.BufferGeometry().setFromPoints([
    new THREE.Vector3(0, 0, 0),
    new THREE.Vector3(axisLen, 0, 0)
  ]);
  axisGroup.add(new THREE.Line(xGeo, xMat));
  
  // Y axis (green)
  const yMat = new THREE.LineBasicMaterial({{ color: 0x33ff33 }});
  const yGeo = new THREE.BufferGeometry().setFromPoints([
    new THREE.Vector3(0, 0, 0),
    new THREE.Vector3(0, axisLen, 0)
  ]);
  axisGroup.add(new THREE.Line(yGeo, yMat));
  
  // Z axis (blue)
  const zMat = new THREE.LineBasicMaterial({{ color: 0x3333ff }});
  const zGeo = new THREE.BufferGeometry().setFromPoints([
    new THREE.Vector3(0, 0, 0),
    new THREE.Vector3(0, 0, axisLen)
  ]);
  axisGroup.add(new THREE.Line(zGeo, zMat));
  
  scene.add(axisGroup);
}}

buildMesh(0, H - 1);
buildCompass();

// UI functions
window.updateSlice = function() {{
  const yMax = parseInt(document.getElementById('ySlice').value);
  const yMin = parseInt(document.getElementById('ySliceMin').value);
  document.getElementById('sliceInfo').textContent = `Y=${{yMin}} to Y=${{yMax}}`;
  buildMesh(yMin, yMax);
  if (wireframeMode) updateWireframe();
}};

window.resetView = function() {{
  camera.position.copy(initialCamPos);
  controls.target.set(0, H / 2, 0);
  controls.update();
  document.getElementById('ySlice').value = H - 1;
  document.getElementById('ySliceMin').value = 0;
  updateSlice();
}};

function updateWireframe() {{
  if (wireframeMesh) {{
    scene.remove(wireframeMesh);
    wireframeMesh.geometry.dispose();
    wireframeMesh.material.dispose();
    wireframeMesh = null;
  }}
  if (!wireframeMode || !instancedMesh) return;
  
  const yMax = parseInt(document.getElementById('ySlice').value);
  const yMin = parseInt(document.getElementById('ySliceMin').value);
  const visible = voxelData.filter(v => v.y >= yMin && v.y <= yMax);
  
  const edgesGeo = new THREE.EdgesGeometry(geometry);
  const wireMat = new THREE.LineBasicMaterial({{ color: 0x00ffff, transparent: true, opacity: 0.3 }});
  wireframeMesh = new THREE.InstancedMesh(edgesGeo, wireMat, visible.length);
  
  const matrix = new THREE.Matrix4();
  for (let i = 0; i < visible.length; i++) {{
    const v = visible[i];
    matrix.setPosition(v.x - W / 2, v.y, v.z - D / 2);
    wireframeMesh.setMatrixAt(i, matrix);
  }}
  wireframeMesh.instanceMatrix.needsUpdate = true;
  scene.add(wireframeMesh);
}}

window.toggleWireframe = function() {{
  wireframeMode = !wireframeMode;
  document.getElementById('wfBtn').classList.toggle('active');
  if (wireframeMode) {{
    if (instancedMesh) instancedMesh.visible = false;
    updateWireframe();
  }} else {{
    if (instancedMesh) instancedMesh.visible = true;
    if (wireframeMesh) {{
      scene.remove(wireframeMesh);
      wireframeMesh.geometry.dispose();
      wireframeMesh.material.dispose();
      wireframeMesh = null;
    }}
  }}
}};

window.toggleEdges = function() {{
  showEdges = !showEdges;
  document.getElementById('edgeBtn').classList.toggle('active');
  const yMax = parseInt(document.getElementById('ySlice').value);
  const yMin = parseInt(document.getElementById('ySliceMin').value);
  buildMesh(yMin, yMax);
}};

// Resize
window.addEventListener('resize', () => {{
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
}});

// Render loop
function animate() {{
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
}}
animate();
</script>
</body>
</html>'''
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(html)
    print(f"Saved {filepath} ({len(voxel_list)} visible voxels)")

if __name__ == "__main__":
    infile = sys.argv[1] if len(sys.argv) > 1 else "fire_escape_example1_expanded2.stasset"
    outfile = sys.argv[2] if len(sys.argv) > 2 else infile.replace('.stasset', '.html')
    
    v, dims, scale, meta = load_stasset_full(infile)
    title = infile.replace('.stasset', '').replace('_', ' ').title()
    voxel_to_html(v, dims, outfile, title=title)
    print(f"Open in browser: {outfile}")
