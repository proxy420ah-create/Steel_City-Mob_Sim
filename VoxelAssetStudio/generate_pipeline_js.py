#!/usr/bin/env python3
"""Append JS to character_pipeline.html"""
import os

output_path = os.path.join(os.path.dirname(__file__), "character_pipeline.html")

js = r'''
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const allMaterials = __MATERIALS__;
const GROUPS = [
  {id:0, name:'Body', color:'#888888'},
  {id:1, name:'Head', color:'#00ff66'},
  {id:2, name:'Left Arm', color:'#00aaff'},
  {id:3, name:'Right Arm', color:'#ff6666'},
  {id:4, name:'Left Leg', color:'#ffaa00'},
  {id:5, name:'Right Leg', color:'#aa66ff'},
];
const STATES = [
  {id:0,name:'Idle'},{id:1,name:'Walking'},{id:2,name:'Looking'},
  {id:3,name:'Checking'},{id:4,name:'Aiming'},{id:5,name:'Crouching'},
  {id:6,name:'Flinching'},{id:7,name:'Falling'},{id:8,name:'Down'},
];
const DEFAULT_PIVOTS = {
  1:{x:0.5,y:0.78,z:0.5}, 2:{x:0.25,y:0.75,z:0.5},
  3:{x:0.75,y:0.75,z:0.5}, 4:{x:0.375,y:0.34,z:0.5},
  5:{x:0.625,y:0.34,z:0.5},
};
let pivots = JSON.parse(JSON.stringify(DEFAULT_PIVOTS));
const DEFAULT_PARAMS = {
  walk:{armSwing:0.3,armFreq:6.0,legStride:0.4,legFreq:6.0},
  looking:{headYaw:0.5,headYawFreq:2.0,headPitch:0.1,headPitchFreq:1.3},
  aiming:{headYaw:0.3,headPitch:-0.1,armSwing:-1.2},
  crouching:{headPitch:0.2,armSwingL:0.3,armSwingR:-0.3,legStride:0.6},
  flinching:{headPitch:0.4,armSwing:-1.5},
  falling:{legStrideL:-0.5,legStrideR:0.5},
};
let animParams = JSON.parse(JSON.stringify(DEFAULT_PARAMS));

let W=16, H=32, D=10;
const voxelMap = new Map();
const groupMap = new Map();

// === SCENE ===
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0a0a14);
const camera = new THREE.PerspectiveCamera(50, window.innerWidth/window.innerHeight, 0.1, 5000);
camera.position.set(W*3, H*2, D*3);
const renderer = new THREE.WebGLRenderer({antialias:true});
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
document.getElementById('canvas-container').appendChild(renderer.domElement);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true; controls.dampingFactor = 0.1;
controls.target.set(0, H/2, 0);
scene.add(new THREE.AmbientLight(0x666688, 0.6));
const dl = new THREE.DirectionalLight(0xffffff, 1.0); dl.position.set(W, H*2, D); scene.add(dl);
const dl2 = new THREE.DirectionalLight(0x88aaff, 0.4); dl2.position.set(-W, H, -D); scene.add(dl2);
const grid = new THREE.GridHelper(Math.max(W,D)*4, Math.max(W,D), 0x444466, 0x222244);
scene.add(grid);

// === RAYCASTING & MESH ===
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
const dummyMatrix = new THREE.Matrix4();
const boxGeo = new THREE.BoxGeometry(1,1,1);
const lambertMat = new THREE.MeshLambertMaterial();
let instancedMesh = null, edgesMesh = null;
let viewMode = 'material';
let currentPhase = 1;
const GROUP_COLORS = {};
for (const g of GROUPS) GROUP_COLORS[g.id] = new THREE.Color(g.color);
function getMatColor(mid) {
  const m = allMaterials.find(m=>m.id===mid);
  return m ? new THREE.Color(m.r/255, m.g/255, m.b/255) : new THREE.Color(0.8,0.8,0.8);
}

function getVoxelList() {
  const result = [];
  for (const [key, mid] of voxelMap) {
    const [x,y,z] = key.split(',').map(Number);
    result.push({x,y,z,mid,gid:groupMap.get(key)||0});
  }
  return result;
}

function rebuildMesh() {
  if (instancedMesh){scene.remove(instancedMesh);instancedMesh.dispose();}
  if (edgesMesh){scene.remove(edgesMesh);edgesMesh.geometry.dispose();edgesMesh.material.dispose();}
  const voxels = getVoxelList();
  const count = voxels.length;
  if (count === 0) return;
  instancedMesh = new THREE.InstancedMesh(boxGeo, lambertMat, count);
  instancedMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count*3),3);
  const color = new THREE.Color();
  for (let i=0;i<count;i++){
    const v = voxels[i];
    dummyMatrix.setPosition(v.x-W/2, v.y, v.z-D/2);
    instancedMesh.setMatrixAt(i, dummyMatrix);
    if (viewMode==='group') color.copy(GROUP_COLORS[v.gid]||GROUP_COLORS[0]);
    else color.copy(getMatColor(v.mid));
    instancedMesh.setColorAt(i, color);
  }
  instancedMesh.instanceMatrix.needsUpdate = true;
  if (instancedMesh.instanceColor) instancedMesh.instanceColor.needsUpdate = true;
  scene.add(instancedMesh);
  const edgesGeo = new THREE.EdgesGeometry(boxGeo);
  const edgesMat = new THREE.LineBasicMaterial({color:0x000000,transparent:true,opacity:0.2});
  edgesMesh = new THREE.InstancedMesh(edgesGeo, edgesMat, count);
  for (let i=0;i<count;i++){const v=voxels[i];dummyMatrix.setPosition(v.x-W/2,v.y,v.z-D/2);edgesMesh.setMatrixAt(i,dummyMatrix);}
  edgesMesh.instanceMatrix.needsUpdate = true; scene.add(edgesMesh);
  applyIsolate();
}

// === ISOLATE ===
let isolatedGroup = -1;
function applyIsolate() {
  if (!instancedMesh) return;
  const voxels = getVoxelList();
  for (let i=0;i<voxels.length;i++) {
    const v = voxels[i];
    const isIso = isolatedGroup < 0 || v.gid === isolatedGroup;
    dummyMatrix.makeScale(isIso?1:0.001, isIso?1:0.001, isIso?1:0.001);
    dummyMatrix.setPosition(v.x-W/2, v.y, v.z-D/2);
    instancedMesh.setMatrixAt(i, dummyMatrix);
    if (edgesMesh) edgesMesh.setMatrixAt(i, dummyMatrix);
  }
  instancedMesh.instanceMatrix.needsUpdate = true;
  if (edgesMesh) edgesMesh.instanceMatrix.needsUpdate = true;
}

// === SEEDED RNG (Windows LCG from RE) ===
class SeededRNG {
  constructor(seed){this.state=(seed>>>0)||1;}
  next(){this.state=(Math.imul(this.state,0x343fd)+0x269ec3)>>>0;return(this.state>>16)&0x7FFF;}
  range(max){return this.next()%max;}
}

// === CHARACTER GENERATION ===
function skinMaterial(tone) {
  if (tone < 16) return 105;
  if (tone < 32) return 122;
  if (tone < 48) return 102;
  return 103;
}

function setV(x,y,z,mid){if(mid===0)voxelMap.delete(`${x},${y},${z}`);else voxelMap.set(`${x},${y},${z}`,mid);}

function generateCharacter(seed, features) {
  voxelMap.clear(); groupMap.clear();
  const skin = skinMaterial(features.skin);
  const headDims = [
    [6,8,5],[7,7,5],[5,9,5],[6,7,6],[5,8,4],[7,8,5],
    [5,9,4],[7,7,6],[5,7,4],[7,8,6],[6,8,4],[6,7,5],
    [5,9,5],[6,7,4],
  ];
  const hd = headDims[features.head % 14];
  const hw = hd[0], hh = hd[1], hdz = hd[2];
  const hx = Math.floor((W - hw) / 2);
  const hz = Math.floor((D - hdz) / 2);
  const hy = 22;
  const bodyMats = {
    hoodlum: {torso:106, legs:106, arms:106},
    civilian: {torso:103, legs:107, arms:103},
    police: {torso:120, legs:122, arms:120},
    overcoat: {torso:108, legs:106, arms:108},
  };
  const bm = bodyMats[features.body] || bodyMats.hoodlum;

  // Legs
  for (let y=0;y<10;y++) {
    for (let x=5;x<8;x++) for (let z=3;z<7;z++) setV(x,y,z,bm.legs);
    for (let x=8;x<11;x++) for (let z=3;z<7;z++) setV(x,y,z,bm.legs);
  }
  // Torso
  for (let y=10;y<22;y++) for (let x=4;x<12;x++) for (let z=3;z<7;z++) setV(x,y,z,bm.torso);
  // Arms
  for (let y=16;y<22;y++) {
    for (let x=2;x<4;x++) for (let z=3;z<7;z++) setV(x,y,z,bm.arms);
    for (let x=12;x<14;x++) for (let z=3;z<7;z++) setV(x,y,z,bm.arms);
  }
  if (features.body === 'overcoat') {
    for (let y=6;y<10;y++) for (let x=3;x<13;x++) for (let z=2;z<8;z++) setV(x,y,z,108);
  }
  // Head
  for (let y=hy;y<hy+hh;y++) for (let x=hx;x<hx+hw;x++) for (let z=hz;z<hz+hdz;z++) setV(x,y,z,skin);

  // Hair
  const hairMat = 106;
  const hairTop = hy + hh;
  switch(features.hair) {
    case 0: break;
    case 1: for (let x=hx;x<hx+hw;x++) for (let z=hz;z<hz+hdz;z++) setV(x,hairTop,z,hairMat); break;
    case 2: for (let x=hx+1;x<hx+hw-1;x++) for (let z=hz+1;z<hz+hdz-1;z++) setV(x,hairTop,z,hairMat); break;
    case 3: for (let x=hx;x<hx+hw;x++) for (let z=hz;z<hz+hdz;z++) if (x < hx+hw/2) setV(x,hairTop,z,hairMat); for (let x=hx;x<hx+3;x++) for (let y=hy;y<hy+3;y++) setV(x,y,hz,hairMat); break;
    case 4: for (let x=hx;x<hx+hw;x++) for (let z=hz;z<hz+hdz;z++) setV(x,hairTop,z,hairMat); for (let y=hy;y<hy+5;y++) { setV(hx,y,hz,hairMat); setV(hx+hw-1,y,hz,hairMat); } break;
    case 5: for (let y=hairTop;y<hairTop+2;y++) for (let z=hz;z<hz+hdz;z++) setV(hx+Math.floor(hw/2),y,z,hairMat); break;
    case 6: for (let x=hx-1;x<hx+hw+1;x++) for (let z=hz-1;z<hz+hdz+1;z++) { const dx=x-(hx+hw/2), dz=z-(hz+hdz/2); if (dx*dx+dz*dz < (hw/2+1)*(hw/2+1)) setV(x,hairTop,z,hairMat); } break;
    case 7: for (let x=hx;x<hx+hw;x++) for (let z=hz;z<hz+hdz;z++) setV(x,hairTop,z,hairMat); for (let x=hx;x<hx+hw;x++) setV(x,hairTop,hz-1,hairMat); break;
  }

  // Eyes
  const eyeY = hy + Math.floor(hh*0.45);
  const eyeZ = hz + hdz - 1;
  const eyeMat = 109;
  const eyeSpacing = Math.max(1, Math.floor(hw/3));
  const eyeLX = hx + Math.floor(hw/2) - eyeSpacing;
  const eyeRX = hx + Math.floor(hw/2) + eyeSpacing;
  switch(features.eyes) {
    case 0: setV(eyeLX,eyeY,eyeZ,eyeMat); setV(eyeRX,eyeY,eyeZ,eyeMat); break;
    case 1: setV(eyeLX,eyeY,eyeZ,eyeMat); setV(eyeLX,eyeY+1,eyeZ,eyeMat); setV(eyeRX,eyeY,eyeZ,eyeMat); setV(eyeRX,eyeY+1,eyeZ,eyeMat); break;
    case 2: for(let dx=0;dx<2;dx++)for(let dy=0;dy<2;dy++){setV(eyeLX+dx,eyeY+dy,eyeZ,eyeMat);setV(eyeRX+dx,eyeY+dy,eyeZ,eyeMat);} break;
    case 3: setV(eyeLX,eyeY,eyeZ,eyeMat); setV(eyeRX,eyeY,eyeZ,eyeMat); setV(eyeLX,eyeY,eyeZ-1,eyeMat); setV(eyeRX,eyeY,eyeZ-1,eyeMat); break;
    case 4: setV(eyeLX,eyeY,eyeZ,eyeMat); setV(eyeLX-1,eyeY,eyeZ,eyeMat); setV(eyeRX,eyeY,eyeZ,eyeMat); setV(eyeRX+1,eyeY,eyeZ,eyeMat); break;
    case 5: setV(eyeLX,eyeY+1,eyeZ,eyeMat); setV(eyeRX,eyeY+1,eyeZ,eyeMat); break;
  }

  // Nose
  const noseX = hx + Math.floor(hw/2);
  const noseY = eyeY + 1;
  switch(features.nose) {
    case 0: setV(noseX,noseY,eyeZ+1,skin); break;
    case 1: setV(noseX,noseY,eyeZ+1,skin); setV(noseX,noseY+1,eyeZ+1,skin); break;
    case 2: setV(noseX,noseY,eyeZ+1,skin); setV(noseX,noseY+1,eyeZ+1,skin); setV(noseX,noseY+2,eyeZ+1,skin); break;
    case 3: setV(noseX,noseY,eyeZ+1,skin); setV(noseX+1,noseY,eyeZ+1,skin); break;
    case 4: setV(noseX,noseY,eyeZ+1,skin); setV(noseX,noseY+1,eyeZ+2,skin); break;
  }

  // Mouth
  const mouthY = noseY + 2;
  const mouthMat = 119;
  switch(features.mouth) {
    case 0: setV(noseX,mouthY,eyeZ,mouthMat); break;
    case 1: for(let dx=-1;dx<=1;dx++) setV(noseX+dx,mouthY,eyeZ,mouthMat); break;
    case 2: setV(noseX-1,mouthY,eyeZ,mouthMat); setV(noseX+1,mouthY,eyeZ,mouthMat); setV(noseX,mouthY+1,eyeZ,mouthMat); break;
    case 3: setV(noseX-1,mouthY,eyeZ,mouthMat); setV(noseX+1,mouthY,eyeZ,mouthMat); setV(noseX,mouthY-1,eyeZ,mouthMat); break;
    case 4: setV(noseX,mouthY,eyeZ,mouthMat); setV(noseX+1,mouthY,eyeZ,mouthMat); break;
  }

  // Glasses
  const glassesMat = 109;
  switch(features.glasses) {
    case 0: break;
    case 1: setV(eyeLX-1,eyeY,eyeZ+1,glassesMat); setV(eyeLX,eyeY-1,eyeZ+1,glassesMat); setV(eyeRX+1,eyeY,eyeZ+1,glassesMat); setV(eyeRX,eyeY-1,eyeZ+1,glassesMat); setV(noseX,eyeY,eyeZ+1,glassesMat); break;
    case 2: for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++){setV(eyeLX+dx,eyeY+dy,eyeZ+1,glassesMat);setV(eyeRX+dx,eyeY+dy,eyeZ+1,glassesMat);} break;
    case 3: for(let dx=-1;dx<=1;dx++)for(let dy=0;dy<=1;dy++){setV(eyeLX+dx,eyeY+dy,eyeZ,glassesMat);setV(eyeRX+dx,eyeY+dy,eyeZ,glassesMat);} break;
  }

  // Hat
  const hatMat = 120;
  const hatTop = hairTop + 1;
  switch(features.hat) {
    case 0: break;
    case 1: for(let x=hx-1;x<hx+hw+1;x++) for(let z=hz;z<hz+hdz;z++) setV(x,hatTop,z,hatMat); break;
    case 2: for(let x=hx-2;x<hx+hw+2;x++) for(let z=hz-1;z<hz+hdz+1;z++) setV(x,hatTop,z,hatMat); for(let x=hx+1;x<hx+hw-1;x++) for(let z=hz+1;z<hz+hdz-1;z++) setV(x,hatTop+1,z,hatMat); break;
    case 3: for(let y=hy;y<hy+hh;y++) for(let x=hx;x<hx+hw;x++) setV(x,y,hz-1,hatMat); for(let x=hx;x<hx+hw;x++) for(let z=hz;z<hz+hdz;z++) setV(x,hatTop,z,hatMat); break;
    case 4: for(let x=hx;x<hx+hw;x++) for(let z=hz;z<hz+hdz;z++) setV(x,hatTop,z,hatMat); for(let x=hx+1;x<hx+hw-1;x++) for(let z=hz+1;z<hz+hdz-1;z++) setV(x,hatTop+1,z,hatMat); break;
    case 5: for(let y=hy;y<hy+3;y++) for(let x=hx;x<hx+hw;x++) setV(x,y,hz-1,hatMat); break;
    case 6: for(let x=hx-1;x<hx+hw+1;x++) for(let z=hz-1;z<hz+hdz+1;z++) setV(x,hatTop,z,hatMat); for(let y=hatTop+1;y<hatTop+4;y++) for(let x=hx+1;x<hx+hw-1;x++) for(let z=hz+1;z<hz+hdz-1;z++) setV(x,y,z,hatMat); break;
    case 7: for(let x=hx-1;x<hx+hw+1;x++) for(let z=hz-1;z<hz+hdz+1;z++) setV(x,hatTop,z,hatMat); for(let y=hy;y<hy+hh;y++) { setV(hx-1,y,hz,hatMat); setV(hx+hw,y,hz,hatMat); } break;
  }

  // Beard
  const beardMat = 106;
  switch(features.beard) {
    case 0: break;
    case 1: for(let x=hx;x<hx+hw;x++) setV(x,hy+1,hz+hdz-1,beardMat); break;
    case 2: for(let y=hy;y<hy+3;y++) for(let x=hx;x<hx+hw;x++) setV(x,y,hz+hdz-1,beardMat); for(let x=hx;x<hx+hw;x++) setV(x,hy+2,hz+hdz-2,beardMat); break;
  }

  // Scar / accessory
  switch(features.scar) {
    case 0: break;
    case 1: setV(eyeLX-1,eyeY+1,eyeZ,122); setV(eyeLX-1,eyeY+2,eyeZ,122); break;
    case 2: setV(eyeRX+1,eyeY+1,eyeZ,122); setV(eyeRX+1,eyeY+2,eyeZ,122); break;
    case 3: setV(hx-1,eyeY,hz+hdz-1,123); break;
    case 4: setV(hx+hw,eyeY,hz+hdz-1,123); break;
    case 5: setV(noseX+1,mouthY,eyeZ,123); break;
    case 6: setV(hx,eyeY+2,eyeZ,121); break;
    case 7: setV(noseX+2,mouthY,eyeZ+1,108); break;
  }

  autoAssignGroups();
  rebuildMesh();
  updateGroupCounts();
  pushHistory();
}

function autoAssignGroups() {
  groupMap.clear();
  for (const [key] of voxelMap) {
    const [x,y,z] = key.split(',').map(Number);
    let gid = 0;
    if (y >= 22) gid = 1;
    else if (x < 4 && y >= 16) gid = 2;
    else if (x >= 12 && y >= 16) gid = 3;
    else if (x < 8 && y < 11) gid = 4;
    else if (x >= 8 && y < 11) gid = 5;
    else gid = 0;
    groupMap.set(key, gid);
  }
  updateGroupCounts();
}

function clearAllGroups() {
  groupMap.clear();
  for (const [key] of voxelMap) groupMap.set(key, 0);
  rebuildMesh(); updateGroupCounts();
  setStatus('All groups cleared');
}

// === ANIMATION MATH ===
function rotX(a){const c=Math.cos(a),s=Math.sin(a);return[[1,0,0],[0,c,-s],[0,s,c]];}
function rotY(a){const c=Math.cos(a),s=Math.sin(a);return[[c,0,-s],[0,1,0],[s,0,c]];}
function matMul(a,b){const r=[[0,0,0],[0,0,0],[0,0,0]];for(let i=0;i<3;i++)for(let j=0;j<3;j++)for(let k=0;k<3;k++)r[i][j]+=a[i][k]*b[k][j];return r;}
function matVec(m,v){return[m[0][0]*v[0]+m[0][1]*v[1]+m[0][2]*v[2],m[1][0]*v[0]+m[1][1]*v[1]+m[1][2]*v[2],m[2][0]*v[0]+m[2][1]*v[1]+m[2][2]*v[2]];}

function computeGroupRotation(gid, dims, state, time, speed) {
  const PI = Math.PI;
  const hp=[dims[0]*0.5,dims[1]*0.78,dims[2]*0.5], la=[dims[0]*0.25,dims[1]*0.75,dims[2]*0.5];
  const ra=[dims[0]*0.75,dims[1]*0.75,dims[2]*0.5], ll=[dims[0]*0.375,dims[1]*0.34,dims[2]*0.5];
  const rl=[dims[0]*0.625,dims[1]*0.34,dims[2]*0.5];
  let pivot=[0,0,0], rot=[[1,0,0],[0,1,0],[0,0,1]];
  if(gid===1){
    pivot=hp;let hy=0,hp2=0;
    if(state>1.5&&state<3.5){const p=animParams.looking;hy=Math.sin(time*p.headYawFreq)*p.headYaw;hp2=Math.sin(time*p.headPitchFreq)*p.headPitch;}
    else if(state>3.5&&state<4.5){hy=animParams.aiming.headYaw;hp2=animParams.aiming.headPitch;}
    else if(state>5.5&&state<6.5){hp2=animParams.crouching.headPitch;}
    else if(state>6.5&&state<7.5){hp2=animParams.flinching.headPitch;}
    else return null;
    rot=matMul(rotY(hy),rotX(hp2));return{pivot,rot};
  } else if(gid===2){
    pivot=la;let s=0;
    if(state>0.5&&state<1.5)s=Math.sin(time*animParams.walk.armFreq*speed)*animParams.walk.armSwing;
    else if(state>3.5&&state<4.5)s=animParams.aiming.armSwing;
    else if(state>5.5&&state<6.5)s=animParams.crouching.armSwingL;
    else if(state>6.5&&state<7.5)s=animParams.flinching.armSwing;
    else return null;rot=rotX(s);return{pivot,rot};
  } else if(gid===3){
    pivot=ra;let s=0;
    if(state>0.5&&state<1.5)s=Math.sin(time*animParams.walk.armFreq*speed+PI)*animParams.walk.armSwing;
    else if(state>3.5&&state<4.5)s=animParams.aiming.armSwing;
    else if(state>5.5&&state<6.5)s=animParams.crouching.armSwingR;
    else if(state>6.5&&state<7.5)s=animParams.flinching.armSwing;
    else return null;rot=rotX(s);return{pivot,rot};
  } else if(gid===4){
    pivot=ll;let s=0;
    if(state>0.5&&state<1.5)s=Math.sin(time*animParams.walk.legFreq*speed+PI)*animParams.walk.legStride;
    else if(state>5.5&&state<6.5)s=animParams.crouching.legStride;
    else if(state>7.5&&state<8.5)s=animParams.falling.legStrideL;
    else return null;rot=rotX(s);return{pivot,rot};
  } else if(gid===5){
    pivot=rl;let s=0;
    if(state>0.5&&state<1.5)s=Math.sin(time*animParams.walk.legFreq*speed)*animParams.walk.legStride;
    else if(state>5.5&&state<6.5)s=animParams.crouching.legStride;
    else if(state>7.5&&state<8.5)s=animParams.falling.legStrideR;
    else return null;rot=rotX(s);return{pivot,rot};
  }
  return null;
}

// === ANIMATED MESH ===
let currentAnimState = 0, animTime = 0, isPlaying = true, animSpeed = 1.0;
let useAnimatedMesh = false;

function rebuildAnimatedMesh() {
  if (!useAnimatedMesh) { rebuildMesh(); return; }
  if (instancedMesh){scene.remove(instancedMesh);instancedMesh.dispose();}
  if (edgesMesh){scene.remove(edgesMesh);edgesMesh.geometry.dispose();edgesMesh.material.dispose();}
  const voxels = getVoxelList();
  const count = voxels.length;
  if (count === 0) return;
  instancedMesh = new THREE.InstancedMesh(boxGeo, lambertMat, count);
  instancedMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count*3),3);
  const color = new THREE.Color();
  const dims = [W,H,D];
  for (let i=0;i<count;i++){
    const v = voxels[i];
    let px=v.x-W/2, py=v.y, pz=v.z-D/2;
    if (v.gid > 0) {
      const r = computeGroupRotation(v.gid, dims, currentAnimState, animTime, animSpeed);
      if (r) {
        const lp=[v.x,v.y,v.z], rp=[lp[0]-r.pivot[0],lp[1]-r.pivot[1],lp[2]-r.pivot[2]];
        const t = matVec(r.rot, rp);
        px=t[0]+r.pivot[0]-W/2; py=t[1]+r.pivot[1]; pz=t[2]+r.pivot[2]-D/2;
      }
    }
    dummyMatrix.setPosition(px,py,pz);
    instancedMesh.setMatrixAt(i, dummyMatrix);
    if (viewMode==='group') color.copy(GROUP_COLORS[v.gid]||GROUP_COLORS[0]);
    else color.copy(getMatColor(v.mid));
    instancedMesh.setColorAt(i, color);
  }
  instancedMesh.instanceMatrix.needsUpdate = true;
  if (instancedMesh.instanceColor) instancedMesh.instanceColor.needsUpdate = true;
  scene.add(instancedMesh);
  const edgesGeo = new THREE.EdgesGeometry(boxGeo);
  const edgesMat = new THREE.LineBasicMaterial({color:0x000000,transparent:true,opacity:0.15});
  edgesMesh = new THREE.InstancedMesh(edgesGeo, edgesMat, count);
  for (let i=0;i<count;i++){
    const v=voxels[i];let px=v.x-W/2,py=v.y,pz=v.z-D/2;
    if(v.gid>0){const r=computeGroupRotation(v.gid,dims,currentAnimState,animTime,animSpeed);if(r){const lp=[v.x,v.y,v.z],rp=[lp[0]-r.pivot[0],lp[1]-r.pivot[1],lp[2]-r.pivot[2]];const t=matVec(r.rot,rp);px=t[0]+r.pivot[0]-W/2;py=t[1]+r.pivot[1];pz=t[2]+r.pivot[2]-D/2;}}
    dummyMatrix.setPosition(px,py,pz);edgesMesh.setMatrixAt(i,dummyMatrix);
  }
  edgesMesh.instanceMatrix.needsUpdate = true; scene.add(edgesMesh);
}

// === RAYCASTING ===
const planeGeo = new THREE.PlaneGeometry(W*4, D*4);
planeGeo.rotateX(-Math.PI/2);
const groundPlane = new THREE.Mesh(planeGeo, new THREE.MeshBasicMaterial({visible:false}));
scene.add(groundPlane);

function raycastVoxel(event) {
  pointer.set((event.clientX/window.innerWidth)*2-1, -(event.clientY/window.innerHeight)*2+1);
  raycaster.setFromCamera(pointer, camera);
  const objects = [];
  if (instancedMesh) objects.push(instancedMesh);
  objects.push(groundPlane);
  const intersects = raycaster.intersectObjects(objects, false);
  if (!intersects.length) return null;
  const hit = intersects[0];
  if (hit.object === groundPlane) {
    const p = hit.point;
    return {x:Math.floor(p.x+W/2), y:0, z:Math.floor(p.z+D/2), normal:new THREE.Vector3(0,1,0)};
  }
  const iid = hit.instanceId;
  const voxels = getVoxelList();
  if (iid >= voxels.length) return null;
  const v = voxels[iid];
  const normal = hit.face.normal.clone();
  return {x:v.x, y:v.y, z:v.z, normal, mid:v.mid, gid:v.gid,
    placeX:v.x+Math.round(normal.x), placeY:v.y+Math.round(normal.y), placeZ:v.z+Math.round(normal.z)};
}

// === VOXEL TOOLS ===
let currentTool = 'place';
let selectedMaterial = 106;
let selectedGroup = 0;
let mirrorX = false, mirrorY = false, mirrorZ = false;
let lineStart = null, boxStart = null;

function setVoxelWithMirror(x, y, z, mid) {
  const positions = [[x,y,z]];
  if (mirrorX) positions.push([W-1-x,y,z]);
  if (mirrorZ) positions.push([x,y,D-1-z]);
  if (mirrorY) positions.push([x,H-1-y,z]);
  if (mirrorX&&mirrorZ) positions.push([W-1-x,y,D-1-z]);
  if (mirrorX&&mirrorY) positions.push([W-1-x,H-1-y,z]);
  if (mirrorZ&&mirrorY) positions.push([x,H-1-y,D-1-z]);
  if (mirrorX&&mirrorY&&mirrorZ) positions.push([W-1-x,H-1-y,D-1-z]);
  for (const [px,py,pz] of positions) {
    if (px<0||px>=W||py<0||py>=H||pz<0||pz>=D) continue;
    if (mid === 0) { voxelMap.delete(`${px},${py},${pz}`); groupMap.delete(`${px},${py},${pz}`); }
    else { voxelMap.set(`${px},${py},${pz}`, mid); groupMap.set(`${px},${py},${pz}`, selectedGroup); }
  }
}

function performTool(event) {
  if (currentTool === 'camera') return;
  const hit = raycastVoxel(event);
  if (!hit) return;
  switch (currentTool) {
    case 'place':
      if (hit.placeX !== undefined) {
        const px=hit.placeX,py=hit.placeY,pz=hit.placeZ;
        if(px<0||px>=W||py<0||py>=H||pz<0||pz>=D) return;
        if (voxelMap.has(`${px},${py},${pz}`)) return;
        setVoxelWithMirror(px,py,pz,selectedMaterial);
        pushHistory(); rebuildMesh(); updateGroupCounts();
      }
      break;
    case 'erase':
      setVoxelWithMirror(hit.x,hit.y,hit.z,0);
      pushHistory(); rebuildMesh(); updateGroupCounts();
      break;
    case 'paint':
      if (hit.mid !== undefined) { setVoxelWithMirror(hit.x,hit.y,hit.z,selectedMaterial); pushHistory(); rebuildMesh(); }
      break;
    case 'eyedropper':
      if (hit.mid !== undefined) { selectedMaterial = hit.mid; updateMaterialSelection(); if (hit.gid !== undefined) selectGroup(hit.gid); setTool('paint'); }
      break;
    case 'fill':
      if (hit.mid !== undefined) { const cells = floodFill(hit.x,hit.y,hit.z,hit.mid); for (const [x,y,z] of cells) setVoxelWithMirror(x,y,z,selectedMaterial); pushHistory(); rebuildMesh(); updateGroupCounts(); }
      break;
    case 'line':
      if (!lineStart) { lineStart=[hit.placeX!==undefined?[hit.placeX,hit.placeY,hit.placeZ]:[hit.x,hit.y,hit.z]]; }
      else { const ex=hit.placeX!==undefined?hit.placeX:hit.x, ey=hit.placeY!==undefined?hit.placeY:hit.y, ez=hit.placeZ!==undefined?hit.placeZ:hit.z; const lv = computeLineVoxels(lineStart[0],lineStart[1],lineStart[2],ex,ey,ez); for (const [x,y,z] of lv) if(x>=0&&x<W&&y>=0&&y<H&&z>=0&&z<D) setVoxelWithMirror(x,y,z,selectedMaterial); pushHistory(); rebuildMesh(); updateGroupCounts(); lineStart=null; }
      break;
    case 'box':
      if (!boxStart) { boxStart=[hit.placeX!==undefined?[hit.placeX,hit.placeY,hit.placeZ]:[hit.x,hit.y,hit.z]]; }
      else { const ex=hit.placeX!==undefined?hit.placeX:hit.x, ey=hit.placeY!==undefined?hit.placeY:hit.y, ez=hit.placeZ!==undefined?hit.placeZ:hit.z; const bv = computeBoxVoxels(boxStart[0][0],boxStart[0][1],boxStart[0][2],ex,ey,ez); for (const [x,y,z] of bv) if(x>=0&&x<W&&y>=0&&y<H&&z>=0&&z<D) setVoxelWithMirror(x,y,z,selectedMaterial); pushHistory(); rebuildMesh(); updateGroupCounts(); boxStart=null; }
      break;
  }
}

function floodFill(x,y,z,targetMid) {
  const visited = new Set(), queue = [[x,y,z]], result = [];
  while (queue.length) {
    const [cx,cy,cz] = queue.shift();
    const key = `${cx},${cy},${cz}`;
    if (visited.has(key)) continue;
    if (!voxelMap.has(key)) continue;
    if (voxelMap.get(key) !== targetMid) continue;
    visited.add(key); result.push([cx,cy,cz]);
    queue.push([cx+1,cy,cz],[cx-1,cy,cz],[cx,cy+1,cz],[cx,cy-1,cz],[cx,cy,cz+1],[cx,cy,cz-1]);
  }
  return result;
}

function computeLineVoxels(x0,y0,z0,x1,y1,z1) {
  const dx=Math.abs(x1-x0),dy=Math.abs(y1-y0),dz=Math.abs(z1-z0);
  const sx=x0<x1?1:-1, sy=y0<y1?1:-1, sz=z0<z1?1:-1;
  let err = dx-dy-dz, result = [];
  while (true) {
    result.push([x0,y0,z0]);
    if (x0===x1&&y0===y1&&z0===z1) break;
    const e2 = 2*err;
    if (e2 > -dy) { err -= dy; x0 += sx; }
    if (e2 < dx) { err += dx; y0 += sy; }
    if (e2 > -dz) { err -= dz; }
    if (e2 < dz) { err += dz; }
  }
  return result;
}

function computeBoxVoxels(x0,y0,z0,x1,y1,z1) {
  const result = [];
  const minX=Math.min(x0,x1),maxX=Math.max(x0,x1),minY=Math.min(y0,y1),maxY=Math.max(y0,y1),minZ=Math.min(z0,z1),maxZ=Math.max(z0,z1);
  for (let x=minX;x<=maxX;x++) for (let y=minY;y<=maxY;y++) for (let z=minZ;z<=maxZ;z++) result.push([x,y,z]);
  return result;
}

// === HISTORY ===
const history = []; let historyIndex = -1; const MAX_HISTORY = 50;
function snapshot() { return {v:new Map(voxelMap), g:new Map(groupMap)}; }
function pushHistory() {
  history.splice(historyIndex+1);
  history.push(snapshot());
  if (history.length > MAX_HISTORY) history.shift();
  else historyIndex++;
}
function undo() {
  if (historyIndex <= 0) return;
  historyIndex--; voxelMap.clear(); groupMap.clear();
  for (const [k,v] of history[historyIndex].v) voxelMap.set(k,v);
  for (const [k,v] of history[historyIndex].g) groupMap.set(k,v);
  rebuildMesh(); updateGroupCounts();
}
function redo() {
  if (historyIndex >= history.length-1) return;
  historyIndex++; voxelMap.clear(); groupMap.clear();
  for (const [k,v] of history[historyIndex].v) voxelMap.set(k,v);
  for (const [k,v] of history[historyIndex].g) groupMap.set(k,v);
  rebuildMesh(); updateGroupCounts();
}

// === UI BUILDING ===
function buildMaterialGrid() {
  const container = document.getElementById('mat-grid');
  container.innerHTML = '';
  for (const m of allMaterials) {
    const cell = document.createElement('div');
    cell.className = 'mat-cell' + (m.id === selectedMaterial ? ' selected' : '');
    cell.style.background = `rgb(${m.r},${m.g},${m.b})`;
    cell.title = `${m.name} (${m.id})`;
    cell.onclick = () => { selectedMaterial = m.id; updateMaterialSelection(); };
    container.appendChild(cell);
  }
}
function updateMaterialSelection() {
  document.querySelectorAll('.mat-cell').forEach((el, i) => { if(allMaterials[i]) el.classList.toggle('selected', allMaterials[i].id === selectedMaterial); });
}
function buildGroupList() {
  const container = document.getElementById('group-list');
  container.innerHTML = '';
  for (const g of GROUPS) {
    const div = document.createElement('div');
    div.className = 'group-item' + (g.id === selectedGroup ? ' selected' : '');
    div.innerHTML = `<div class="group-swatch" style="background:${g.color}"></div><span class="group-name">${g.name}</span><span class="group-count" id="gcount-${g.id}">0</span>`;
    div.onclick = () => {
      if (isolatedGroup === g.id) { isolatedGroup = -1; document.getElementById('isolate-banner').style.display='none'; }
      else { isolatedGroup = g.id; document.getElementById('isolate-banner').style.display='block'; document.getElementById('isolate-name').textContent = g.name; }
      selectGroup(g.id);
      document.querySelectorAll('.group-item').forEach((el, i) => el.classList.toggle('isolated', GROUPS[i].id === isolatedGroup));
      applyIsolate();
    };
    container.appendChild(div);
  }
  updateGroupCounts();
}
function selectGroup(gid) {
  selectedGroup = gid;
  document.querySelectorAll('.group-item').forEach((el, i) => el.classList.toggle('selected', GROUPS[i].id === gid));
}
function updateGroupCounts() {
  const counts = {};
  for (const g of GROUPS) counts[g.id] = 0;
  for (const [, gid] of groupMap) counts[gid] = (counts[gid]||0)+1;
  for (const g of GROUPS) { const el = document.getElementById(`gcount-${g.id}`); if (el) el.textContent = counts[g.id]||0; }
}
function buildStateGrid() {
  const container = document.getElementById('state-grid');
  container.innerHTML = '';
  for (const s of STATES) {
    const btn = document.createElement('button');
    btn.className = 'state-btn' + (s.id === currentAnimState ? ' active' : '');
    btn.textContent = s.name;
    btn.onclick = () => selectState(s.id);
    container.appendChild(btn);
  }
}
function selectState(stateId) {
  currentAnimState = stateId; animTime = 0;
  document.querySelectorAll('.state-btn').forEach((el, i) => el.classList.toggle('active', STATES[i].id === stateId));
  useAnimatedMesh = true;
  rebuildAnimatedMesh(); buildParamControls();
}
function buildPivotControls() {
  const container = document.getElementById('pivot-controls');
  container.innerHTML = '';
  for (let gid=1; gid<=5; gid++) {
    const p = pivots[gid];
    const div = document.createElement('div');
    div.style.cssText = 'font-size:10px;color:' + GROUPS[gid].color + ';margin:4px 0 2px;';
    div.textContent = `\u25CF ${GROUPS[gid].name}`;
    container.appendChild(div);
    for (const axis of ['x','y','z']) {
      const row = document.createElement('div');
      row.className = 'pivot-row';
      row.innerHTML = `<label>${axis.toUpperCase()}</label><input type="number" min="0" max="1" step="0.01" value="${p[axis]}" data-gid="${gid}" data-axis="${axis}">`;
      row.querySelector('input').oninput = (e) => { pivots[gid][axis] = parseFloat(e.target.value); if (useAnimatedMesh) rebuildAnimatedMesh(); };
      container.appendChild(row);
    }
  }
}
function buildParamControls() {
  const container = document.getElementById('param-controls');
  container.innerHTML = '';
  let params = [];
  if (currentAnimState===1) params=[['walk','armSwing','Arm Swing',0,1.5,0.01],['walk','armFreq','Arm Freq',1,15,0.1],['walk','legStride','Leg Stride',0,1.5,0.01],['walk','legFreq','Leg Freq',1,15,0.1]];
  else if (currentAnimState===2||currentAnimState===3) params=[['looking','headYaw','Head Yaw',0,1.5,0.01],['looking','headYawFreq','Yaw Freq',0.5,10,0.1],['looking','headPitch','Head Pitch',0,1,0.01],['looking','headPitchFreq','Pitch Freq',0.5,10,0.1]];
  else if (currentAnimState===4) params=[['aiming','headYaw','Head Yaw',-1,1,0.01],['aiming','headPitch','Head Pitch',-1,1,0.01],['aiming','armSwing','Arm Swing',-2,0,0.01]];
  else if (currentAnimState===5) params=[['crouching','headPitch','Head Pitch',0,1,0.01],['crouching','armSwingL','L Arm',-1.5,1.5,0.01],['crouching','armSwingR','R Arm',-1.5,1.5,0.01],['crouching','legStride','Leg Stride',0,1.5,0.01]];
  else if (currentAnimState===6) params=[['flinching','headPitch','Head Pitch',0,1.5,0.01],['flinching','armSwing','Arm Swing',-2.5,0,0.01]];
  else if (currentAnimState===7) params=[['falling','legStrideL','L Leg',-1.5,1.5,0.01],['falling','legStrideR','R Leg',-1.5,1.5,0.01]];
  if (!params.length) { container.innerHTML = '<div style="font-size:10px;color:#888;">No params for this state.</div>'; return; }
  for (const [sec,key,label,min,max,step] of params) {
    const row = document.createElement('div');
    row.className = 'param-row';
    const val = animParams[sec][key];
    row.innerHTML = `<label>${label}</label><input type="range" min="${min}" max="${max}" step="${step}" value="${val}"><span class="param-val">${val.toFixed(2)}</span>`;
    const input = row.querySelector('input'), valSpan = row.querySelector('.param-val');
    input.oninput = (e) => { animParams[sec][key] = parseFloat(e.target.value); valSpan.textContent = parseFloat(e.target.value).toFixed(2); };
    container.appendChild(row);
  }
}
function resetPivots() { pivots = JSON.parse(JSON.stringify(DEFAULT_PIVOTS)); buildPivotControls(); if (useAnimatedMesh) rebuildAnimatedMesh(); }
function autoDetectPivots() {
  for (let gid=1; gid<=5; gid++) {
    let minX=W,maxX=0,minY=H,maxY=0,minZ=D,maxZ=0,count=0;
    for (const [key, g] of groupMap) { if (g !== gid) continue; const [x,y,z]=key.split(',').map(Number); minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);minZ=Math.min(minZ,z);maxZ=Math.max(maxZ,z);count++; }
    if (count > 0) { pivots[gid] = {x:(minX+maxX+1)/(2*W), y:(minY+maxY+1)/(2*H), z:(minZ+maxZ+1)/(2*D)}; }
  }
  buildPivotControls(); if (useAnimatedMesh) rebuildAnimatedMesh();
  setStatus('Pivots auto-detected from group bounding boxes');
}

// === FEATURE GENERATION ===
function getFeatures() {
  return {
    body: document.getElementById('feat-body').value,
    head: parseInt(document.getElementById('feat-head').value),
    hair: parseInt(document.getElementById('feat-hair').value),
    eyes: parseInt(document.getElementById('feat-eyes').value),
    nose: parseInt(document.getElementById('feat-nose').value),
    mouth: parseInt(document.getElementById('feat-mouth').value),
    skin: parseInt(document.getElementById('feat-skin').value),
    glasses: parseInt(document.getElementById('feat-glasses').value),
    hat: parseInt(document.getElementById('feat-hat').value),
    beard: parseInt(document.getElementById('feat-beard').value),
    scar: parseInt(document.getElementById('feat-scar').value),
  };
}
function generateFromFeatures() {
  const seed = parseInt(document.getElementById('seed-input').value) || 42;
  generateCharacter(seed, getFeatures());
  setStatus(`Generated character from features (seed=${seed})`);
}
function generateFromSeed() {
  const seed = parseInt(document.getElementById('seed-input').value) || 42;
  const rng = new SeededRNG(seed);
  const features = {
    body: ['hoodlum','civilian','police','overcoat'][rng.range(4)],
    head: rng.range(14),
    hair: rng.range(8),
    eyes: rng.range(6),
    nose: rng.range(5),
    mouth: rng.range(5),
    skin: rng.range(64),
    glasses: rng.range(4),
    hat: rng.range(8),
    beard: rng.range(3),
    scar: rng.range(8),
  };
  // Update UI
  document.getElementById('feat-body').value = features.body;
  document.getElementById('feat-head').value = features.head;
  document.getElementById('feat-hair').value = features.hair;
  document.getElementById('feat-eyes').value = features.eyes;
  document.getElementById('feat-nose').value = features.nose;
  document.getElementById('feat-mouth').value = features.mouth;
  document.getElementById('feat-skin').value = features.skin;
  document.getElementById('feat-glasses').value = features.glasses;
  document.getElementById('feat-hat').value = features.hat;
  document.getElementById('feat-beard').value = features.beard;
  document.getElementById('feat-scar').value = features.scar;
  generateCharacter(seed, features);
  setStatus(`Generated character from seed ${seed} (Windows LCG)`);
}
function randomizeSeed() {
  document.getElementById('seed-input').value = Math.floor(Math.random() * 999999);
  generateFromSeed();
}

// === PHASE NAVIGATION ===
function setPhase(phase) {
  currentPhase = phase;
  document.querySelectorAll('.wizard-step').forEach((el, i) => {
    const p = parseInt(el.dataset.phase);
    el.classList.toggle('active', p === phase);
    el.classList.toggle('done', p < phase);
  });
  document.querySelectorAll('.phase-content').forEach(el => el.classList.remove('active'));
  document.querySelectorAll(`#phase${phase}-left, #phase${phase}-right`).forEach(el => el.classList.add('active'));
  const backBtn = document.getElementById('wizard-back');
  const nextBtn = document.getElementById('wizard-next');
  backBtn.disabled = phase <= 1;
  const labels = {1:'Next: Sculpt & Rig \u2192', 2:'Next: Animate \u2192', 3:'Next: Export \u2192', 4:'\u2713 Finish'};
  nextBtn.textContent = labels[phase] || 'Next \u2192';
  if (phase === 2) { useAnimatedMesh = false; rebuildMesh(); buildGroupList(); buildMaterialGrid(); }
  if (phase === 3) { buildStateGrid(); buildPivotControls(); buildParamControls(); }
  if (phase === 4) { useAnimatedMesh = false; rebuildMesh(); updateSummary(); }
  const statusLabels = {1:'Phase 1: Create your character', 2:'Phase 2: Sculpt & Rig \u2014 select a group to isolate, then edit', 3:'Phase 3: Animate \u2014 select a state to preview', 4:'Phase 4: Export your character for Unity'};
  setStatus(statusLabels[phase] || '');
}

function updateSummary() {
  const counts = {};
  for (const g of GROUPS) counts[g.id] = 0;
  for (const [, gid] of groupMap) counts[gid] = (counts[gid]||0)+1;
  const total = voxelMap.size;
  const el = document.getElementById('char-summary');
  if (!el) return;
  el.innerHTML = `
    <b>Total Voxels:</b> ${total}<br><br>
    <b>Group Distribution:</b><br>
    ${GROUPS.map(g => `\u25CF ${g.name}: ${counts[g.id]||0}`).join('<br>')}<br><br>
    <b>Seed:</b> ${document.getElementById('seed-input').value}<br>
    <b>Body Type:</b> ${document.getElementById('feat-body').value}<br><br>
    <b>Export Files:</b><br>
    \u2022 .stasset JSON \u2014 voxel data for Unity<br>
    \u2022 .groups JSON \u2014 groupID assignments<br>
    \u2022 .anim.json \u2014 pivots + animation params<br>
    \u2022 .project.json \u2014 full project save<br>
  `;
}

// === EXPORT/IMPORT ===
function exportStassetJSON() {
  const voxels = [];
  for (const [key, mid] of voxelMap) { const [x,y,z] = key.split(',').map(Number); voxels.push([x,y,z,mid]); }
  const data = {format:'stasset_export', dims:[W,H,D], voxels};
  downloadJSON(data, 'character.stasset.json');
}
function exportGroupsJSON() {
  const groups = [];
  for (const [key, gid] of groupMap) { if (gid > 0) { const [x,y,z] = key.split(',').map(Number); groups.push([x,y,z,gid]); } }
  const data = {format:'groups_export', dims:[W,H,D], groups};
  downloadJSON(data, 'character.groups.json');
}
function exportAnimParams() {
  const data = {format:'anim_params', pivots, params:animParams};
  downloadJSON(data, 'character.anim.json');
}
function saveProject() {
  const voxels = [];
  for (const [key, mid] of voxelMap) { const [x,y,z] = key.split(',').map(Number); voxels.push([x,y,z,mid]); }
  const groups = [];
  for (const [key, gid] of groupMap) { if (gid > 0) { const [x,y,z] = key.split(',').map(Number); groups.push([x,y,z,gid]); } }
  const data = {format:'character_project', dims:[W,H,D], voxels, groups, pivots, animParams, seed:parseInt(document.getElementById('seed-input').value)||42};
  downloadJSON(data, 'character.project.json');
}
function downloadJSON(data, filename) {
  const blob = new Blob([JSON.stringify(data, null, 2)], {type:'application/json'});
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a'); a.href = url; a.download = filename; a.click();
  URL.revokeObjectURL(url);
  setStatus(`Exported ${filename}`);
}
function importStasset(event) {
  const file = event.target.files[0]; if (!file) return;
  const reader = new FileReader();
  reader.onload = (e) => {
    const data = JSON.parse(e.target.result);
    W = data.dims[0]; H = data.dims[1]; D = data.dims[2];
    voxelMap.clear(); groupMap.clear();
    for (const [x,y,z,mid] of data.voxels) voxelMap.set(`${x},${y},${z}`, mid);
    if (data.groups) for (const [x,y,z,gid] of data.groups) groupMap.set(`${x},${y},${z}`, gid);
    else autoAssignGroups();
    rebuildMesh(); updateGroupCounts(); pushHistory();
    document.getElementById('export-modal').style.display='none';
    setStatus(`Imported ${data.voxels.length} voxels`);
  };
  reader.readAsText(file);
}
function loadProject(event) {
  const file = event.target.files[0]; if (!file) return;
  const reader = new FileReader();
  reader.onload = (e) => {
    const data = JSON.parse(e.target.result);
    W = data.dims[0]; H = data.dims[1]; D = data.dims[2];
    voxelMap.clear(); groupMap.clear();
    for (const [x,y,z,mid] of data.voxels) voxelMap.set(`${x},${y},${z}`, mid);
    if (data.groups) for (const [x,y,z,gid] of data.groups) groupMap.set(`${x},${y},${z}`, gid);
    if (data.pivots) pivots = data.pivots;
    if (data.animParams) animParams = data.animParams;
    if (data.seed) document.getElementById('seed-input').value = data.seed;
    rebuildMesh(); updateGroupCounts(); pushHistory();
    document.getElementById('export-modal').style.display='none';
    setStatus(`Loaded project: ${data.voxels.length} voxels`);
  };
  reader.readAsText(file);
}

// === TOOL SELECTION ===
function setTool(tool) {
  currentTool = tool;
  document.querySelectorAll('[data-tool]').forEach(el => el.classList.toggle('active', el.dataset.tool === tool));
}

// === STATUS ===
function setStatus(msg) { document.getElementById('status-info').textContent = msg; }

// === EVENT HANDLERS ===
document.querySelectorAll('[data-tool]').forEach(btn => btn.onclick = () => setTool(btn.dataset.tool));
document.querySelectorAll('.view-btn').forEach(btn => btn.onclick = () => {
  viewMode = btn.dataset.view;
  document.querySelectorAll('.view-btn').forEach(b => b.classList.toggle('active', b === btn));
  if (useAnimatedMesh) rebuildAnimatedMesh(); else rebuildMesh();
});
document.getElementById('mirror-x').onclick = function() { mirrorX = !mirrorX; this.classList.toggle('active', mirrorX); };
document.getElementById('mirror-y').onclick = function() { mirrorY = !mirrorY; this.classList.toggle('active', mirrorY); };
document.getElementById('mirror-z').onclick = function() { mirrorZ = !mirrorZ; this.classList.toggle('active', mirrorZ); };
document.getElementById('undo-btn').onclick = undo;
document.getElementById('redo-btn').onclick = redo;
document.getElementById('wizard-back').onclick = () => { if (currentPhase > 1) setPhase(currentPhase - 1); };
document.getElementById('wizard-next').onclick = () => { if (currentPhase < 4) setPhase(currentPhase + 1); };
document.getElementById('play-btn').onclick = function() { isPlaying = !isPlaying; this.textContent = isPlaying ? '\u23F8' : '\u25B6'; this.classList.toggle('active', isPlaying); };
document.getElementById('stop-btn').onclick = function() { isPlaying = false; animTime = 0; document.getElementById('play-btn').textContent = '\u25B6'; document.getElementById('play-btn').classList.remove('active'); if (useAnimatedMesh) rebuildAnimatedMesh(); };
document.getElementById('speed-slider').oninput = function() { animSpeed = parseFloat(this.value); document.getElementById('speed-val').textContent = animSpeed.toFixed(1) + 'x'; };
document.getElementById('timeline-slider').oninput = function() { animTime = parseFloat(this.value); document.getElementById('timeline-time').textContent = animTime.toFixed(2) + 's'; if (useAnimatedMesh) rebuildAnimatedMesh(); };

// Auto-regenerate on feature change
['feat-body','feat-head','feat-hair','feat-eyes','feat-nose','feat-mouth','feat-skin','feat-glasses','feat-hat','feat-beard','feat-scar'].forEach(id => {
  const el = document.getElementById(id);
  if (el) el.onchange = generateFromFeatures;
});

// Mouse interaction
let isDragging = false, mouseDownPos = null;
renderer.domElement.addEventListener('pointerdown', (e) => {
  if (currentPhase === 2) { mouseDownPos = [e.clientX, e.clientY]; isDragging = false; }
});
renderer.domElement.addEventListener('pointermove', (e) => {
  if (mouseDownPos) { const dx = Math.abs(e.clientX - mouseDownPos[0]); const dy = Math.abs(e.clientY - mouseDownPos[1]); if (dx > 3 || dy > 3) isDragging = true; }
});
renderer.domElement.addEventListener('pointerup', (e) => {
  if (currentPhase === 2 && !isDragging) performTool(e);
  mouseDownPos = null; isDragging = false;
});

// Keyboard
window.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
  switch(e.key.toLowerCase()) {
    case 'p': setTool('place'); break;
    case 'e': setTool('erase'); break;
    case 'm': setTool('paint'); break;
    case 'i': setTool('eyedropper'); break;
    case 'f': setTool('fill'); break;
    case 'l': setTool('line'); break;
    case 'b': setTool('box'); break;
    case '1': case '2': case '3': case '4': case '5': case '6': selectGroup(parseInt(e.key)-1); break;
    case ' ': if (currentPhase === 3) { e.preventDefault(); document.getElementById('play-btn').click(); } break;
    case 'z': if (e.ctrlKey) undo(); break;
    case 'y': if (e.ctrlKey) redo(); break;
  }
});

// Resize
window.addEventListener('resize', () => { camera.aspect = window.innerWidth/window.innerHeight; camera.updateProjectionMatrix(); renderer.setSize(window.innerWidth, window.innerHeight); });

// === ANIMATION LOOP ===
let lastTime = performance.now(), frameCount = 0, fpsTime = 0;
function animate() {
  requestAnimationFrame(animate);
  const now = performance.now();
  const dt = (now - lastTime) / 1000;
  lastTime = now;
  frameCount++; fpsTime += dt;
  if (fpsTime >= 1) { document.getElementById('status-fps').textContent = Math.round(frameCount/fpsTime) + ' FPS'; frameCount = 0; fpsTime = 0; }
  if (currentPhase === 3 && isPlaying && useAnimatedMesh) {
    animTime += dt * animSpeed;
    const slider = document.getElementById('timeline-slider');
    if (slider) { slider.value = animTime % 10; document.getElementById('timeline-time').textContent = (animTime % 10).toFixed(2) + 's'; }
    rebuildAnimatedMesh();
  }
  controls.update();
  renderer.render(scene, camera);
}

// === INIT ===
buildMaterialGrid();
buildGroupList();
generateFromSeed();
animate();
setStatus('Phase 1: Create your character \u2014 adjust features or seed, then click Next');
'''

# Replace materials placeholder
editor_path = os.path.join(os.path.dirname(__file__), "voxel_editor.html")
with open(editor_path, "r", encoding="utf-8") as f:
    src = f.read()
start_m = src.index("const allMaterials = ") + len("const allMaterials = ")
end_m = src.index(";", start_m)
materials_json = src[start_m:end_m].strip()

js = js.replace("__MATERIALS__", materials_json)

with open(output_path, 'a', encoding='utf-8') as f:
    f.write(js)
    f.write('\n</script>\n</body>\n</html>\n')

print(f"Part 2 (JS) appended. Total size: {os.path.getsize(output_path)} bytes")
