// === CHARACTER POSE ENGINE ===
// Shared posing math for character animation states.
// Used by both character_animator.html and voxel_editor.html (character preview).
// Extracted from character_animator.html to avoid code duplication.

// === MATH UTILITIES ===
export function rotationX(angle) {
  const c = Math.cos(angle), s = Math.sin(angle);
  return [[1,0,0],[0,c,-s],[0,s,c]];
}
export function rotationY(angle) {
  const c = Math.cos(angle), s = Math.sin(angle);
  return [[c,0,-s],[0,1,0],[s,0,c]];
}
export function rotationZ(angle) {
  const c = Math.cos(angle), s = Math.sin(angle);
  return [[c,-s,0],[s,c,0],[0,0,1]];
}
export function matMul3(a, b) {
  const r = [[0,0,0],[0,0,0],[0,0,0]];
  for (let i=0;i<3;i++) for (let j=0;j<3;j++) for (let k=0;k<3;k++)
    r[i][j] += a[i][k]*b[k][j];
  return r;
}
export function matVec3(m, v) {
  return [
    m[0][0]*v[0]+m[0][1]*v[1]+m[0][2]*v[2],
    m[1][0]*v[0]+m[1][1]*v[1]+m[1][2]*v[2],
    m[2][0]*v[0]+m[2][1]*v[1]+m[2][2]*v[2],
  ];
}
export function rotationByAxis(axis, angle) {
  if (axis === 1) return rotationY(angle);
  if (axis === 2) return rotationZ(angle);
  return rotationX(angle);
}

// === WALK KEYFRAME INTERPOLATION ===
function smoothstep(t) { return t * t * (3 - 2 * t); }
function cosineInterp(t) { return 0.5 - 0.5 * Math.cos(t * Math.PI); }
function catmullRom(p0, p1, p2, p3, t) {
  const t2 = t * t;
  const t3 = t2 * t;
  return 0.5 * (
    (2 * p1) +
    (-p0 + p2) * t +
    (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
    (-p0 + 3 * p1 - 3 * p2 + p3) * t3
  );
}

function mirrorWalkPose(pose) {
  return {
    armSwingL: pose.armSwingR,     armSwingR: pose.armSwingL,
    legStrideL: pose.legStrideR,   legStrideR: pose.legStrideL,
    elbowBendL: pose.elbowBendR,   elbowBendR: pose.elbowBendL,
    kneeBendL: pose.kneeBendR,     kneeBendR: pose.kneeBendL,
    forearmTwistL: pose.forearmTwistR, forearmTwistR: pose.forearmTwistL,
  };
}

function getWalkKfPose(idx, wkf) {
  if (idx === 0) return wkf.kf0;
  if (idx === 1) return wkf.kf1;
  if (idx === 2) return wkf.kf2 || (wkf.autoMirror ? mirrorWalkPose(wkf.kf0) : wkf.kf0);
  if (idx === 3) return wkf.kf3 || (wkf.autoMirror ? mirrorWalkPose(wkf.kf1) : wkf.kf1);
  return wkf.kf0;
}

let _walkPoseCache = null;
let _walkPoseCacheKey = '';
function invalidateWalkPoseCache() { _walkPoseCache = null; _walkPoseCacheKey = ''; }

function getWalkPose(animTime, animSpeed, params) {
  const cacheKey = animTime.toFixed(4) + ':' + animSpeed.toFixed(2);
  if (_walkPoseCacheKey === cacheKey && _walkPoseCache) return _walkPoseCache;

  const wkf = params.walkKeyframes;
  const cycleDur = wkf.cycleDuration / Math.max(0.01, animSpeed);
  const cyclePhase = ((animTime % cycleDur) + cycleDur) % cycleDur / cycleDur;

  const kfPositions = [0.0, 0.25, 0.5, 0.75];
  let kfAIdx = 0, kfBIdx = 1, t = 0;

  for (let i = 0; i < 4; i++) {
    const next = (i + 1) % 4;
    const posA = kfPositions[i];
    const posB = kfPositions[next];
    if (next === 0) {
      if (cyclePhase >= posA) {
        kfAIdx = i; kfBIdx = 0;
        t = (cyclePhase - posA) / (1.0 - posA);
        break;
      } else if (cyclePhase < posB) {
        kfAIdx = 3; kfBIdx = 0;
        t = (cyclePhase + (1.0 - 0.75)) / (1.0 - 0.75 + 0.0);
        break;
      }
    } else {
      if (cyclePhase >= posA && cyclePhase < posB) {
        kfAIdx = i; kfBIdx = next;
        t = (cyclePhase - posA) / (posB - posA);
        break;
      }
    }
  }

  const kfA = getWalkKfPose(kfAIdx, wkf);
  const kfB = getWalkKfPose(kfBIdx, wkf);
  const interp = wkf.interpolation || 'spline';
  const interpFn = interp === 'spline' ? catmullRom : interp === 'cosine' ? cosineInterp : smoothstep;

  const getKf = (idx) => getWalkKfPose((idx + 4) % 4, wkf);
  const kfPrev = getKf(kfAIdx - 1);
  const kfNext = getKf(kfBIdx + 1);

  function interpVal(key) {
    if (interp === 'spline') {
      return catmullRom(kfPrev[key], kfA[key], kfB[key], kfNext[key], t);
    }
    const v = interpFn(t);
    return kfA[key] + (kfB[key] - kfA[key]) * v;
  }

  const pose = {
    armSwingL: interpVal('armSwingL'),     armSwingR: interpVal('armSwingR'),
    legStrideL: interpVal('legStrideL'),   legStrideR: interpVal('legStrideR'),
    elbowBendL: interpVal('elbowBendL'),   elbowBendR: interpVal('elbowBendR'),
    kneeBendL: interpVal('kneeBendL'),     kneeBendR: interpVal('kneeBendR'),
    forearmTwistL: interpVal('forearmTwistL'), forearmTwistR: interpVal('forearmTwistR'),
  };

  const bobAmp = wkf.bodyBob && wkf.bodyBob.enabled ? wkf.bodyBob.amplitude : 0;
  const bobFn = bobAmp > 0 ? Math.sin(cyclePhase * 2 * Math.PI) : () => 0;
  const bodyBobY = bobFn() * bobAmp;

  const shiftAmp = wkf.weightShift && wkf.weightShift.enabled ? wkf.weightShift.amplitude : 0;
  const shiftFn = shiftAmp > 0 ? Math.cos(cyclePhase * 2 * Math.PI) : () => 0;
  const weightShiftX = shiftFn() * shiftAmp;

  const result = { pose, cyclePhase, kfAIdx, kfBIdx, interpT: t, bodyBobY, weightShiftX };
  _walkPoseCache = result;
  _walkPoseCacheKey = cacheKey;
  return result;
}

// === PIVOTS ===
export const DEFAULT_PIVOTS = {
  0: { x: 0.5, y: 0.4, z: 0.5 },
  1: { x: 0.5, y: 0.78, z: 0.5 },
  2: { x: 0.25, y: 0.75, z: 0.5 },
  3: { x: 0.75, y: 0.75, z: 0.5 },
  4: { x: 0.375, y: 0.34, z: 0.5 },
  5: { x: 0.625, y: 0.34, z: 0.5 },
  8: { x: 0.25, y: 0.75, z: 0.5 },
  9: { x: 0.75, y: 0.75, z: 0.5 },
  6: { x: 0.375, y: 0.20, z: 0.5 },
  7: { x: 0.625, y: 0.20, z: 0.5 },
};

// === ANIMATION STATES ===
export const POSE_STATES = [
  { id: 0, name: 'Idle', desc: 'Standing still' },
  { id: 1, name: 'Walking', desc: 'Walking cycle' },
  { id: 2, name: 'Looking', desc: 'Head looking around' },
  { id: 3, name: 'Aim Walk', desc: 'Aiming pose + walking gait' },
  { id: 4, name: 'Aiming', desc: 'Static aiming pose' },
  { id: 5, name: 'Crouching', desc: 'Crouch pose' },
  { id: 8, name: 'Down', desc: 'Lying down / defeated' },
  { id: 9, name: 'T-Pose', desc: 'Bind/rest pose' },
];

// === DEFAULT ANIMATION PARAMETERS ===
export const DEFAULT_PARAMS = {
  restPose: {
    leftArmZ:  -Math.PI / 2,
    rightArmZ:  Math.PI / 2,
  },
  jointOffset: {
    1: { x: 0, y: 0, z: 0 },
    2: { x: 0, y: 0, z: 0 },
    3: { x: 0, y: 0, z: 0 },
    4: { x: 0, y: 0, z: 0 },
    5: { x: 0, y: 0, z: 0 },
  },
  walk: {
    armSwing: 0.3, armFreq: 6.0, legStride: 0.4, legFreq: 6.0,
  },
  walkKeyframes: {
    autoMirror: true,
    cycleDuration: 1.2,
    interpolation: 'spline',
    bodyBob: { enabled: true, amplitude: 0.6 },
    weightShift: { enabled: true, amplitude: 0.4 },
    kf0: {
      armSwingL: 0.3, armSwingR: -0.3,
      legStrideL: -0.4, legStrideR: 0.4,
      elbowBendL: 0.1, elbowBendR: 0.1,
      kneeBendL: 0.0, kneeBendR: 0.0,
      forearmTwistL: 0.0, forearmTwistR: 0.0,
    },
    kf1: {
      armSwingL: 0.0, armSwingR: 0.0,
      legStrideL: 0.3, legStrideR: -0.1,
      elbowBendL: 0.3, elbowBendR: 0.3,
      kneeBendL: 0.8, kneeBendR: 0.15,
      forearmTwistL: 0.0, forearmTwistR: 0.0,
    },
    kf2: null, kf3: null,
  },
  armSwing: {
    axisL: 0, axisR: 0, signL: 1, signR: 1,
  },
  legStride: {
    axisL: 0, axisR: 0, signL: 1, signR: 1,
  },
  legTwist: {
    leftRest: 0.0, rightRest: 0.0,
  },
  elbowBend: {
    leftRest: 0.0, rightRest: 0.0, walkAmp: 0.15,
    axisL: 1, axisR: 1, signL: 1, signR: -1,
    twistL: 0.0, twistR: 0.0, twistWalkAmp: 0.15,
  },
  kneeBend: {
    leftRest: 0.0, rightRest: 0.0, walkAmp: 0.42,
    axisL: 0, axisR: 0, signL: 1, signR: 1,
  },
  looking: {
    headYaw: 0.5, headYawFreq: 2.0,
    headPitch: 0.035, headPitchFreq: 1.3,
  },
  aiming: {
    weaponType: 'pistol',
    torsoTwist: 0.2,
    headYaw: 0.0, headPitch: -0.05, headTilt: 0.0,
    armSwingL: -1.4, armSwingR: 0.0,
    shoulderReachL: 0.0, shoulderReachR: 0.0,
    elbowBendL: 0.3, elbowBendR: 0.0,
  },
  crouching: {
    bodyLower: 0.0, modelLower: 4.0, bodyLean: 0.0,
    headPitch: 0.0, armSwingL: 0.0, armSwingR: 0.0,
    legStrideL: -1.15, legStrideR: 0.0,
    kneeBendL: 1.15, kneeBendR: 1.40,
  },
};

// === WEAPON PRESETS ===
export const AIM_WEAPON_PRESETS = {
  pistol: {
    torsoTwist: 0.2,
    armSwingL: -1.4, armSwingR: 0.0,
    shoulderReachL: 0.0, shoulderReachR: 0.0,
    elbowBendL: 0.3, elbowBendR: 0.0,
    headYaw: 0.0, headPitch: -0.05, headTilt: 0.0,
  },
  dual: {
    torsoTwist: 0.0,
    armSwingL: -1.4, armSwingR: -1.4,
    shoulderReachL: 0.0, shoulderReachR: 0.0,
    elbowBendL: 0.3, elbowBendR: 0.3,
    headYaw: 0.0, headPitch: -0.05, headTilt: 0.0,
  },
  rifle: {
    torsoTwist: 0.85,
    armSwingL: -1.60, armSwingR: -1.35,
    shoulderReachL: -0.20, shoulderReachR: -1.00,
    elbowBendL: 0.00, elbowBendR: 0.70,
    headYaw: -0.90, headPitch: 0.10, headTilt: 0.15,
  },
};

export function applyWeaponPreset(type, animParams) {
  const preset = AIM_WEAPON_PRESETS[type];
  if (!preset) return;
  animParams.aiming.weaponType = type;
  Object.assign(animParams.aiming, preset);
}

// === GROUP ROTATION (Forward Kinematics) ===
// This is the core posing function. Given a group ID, it computes the
// hierarchical transform chain that should be applied to all voxels in
// that group for the given animation state.
export function computeGroupRotation(gid, dims, voxelSize, animState, animTime, animSpeed, animParams, pivots) {
  const PI = Math.PI;
  const identity = [[1,0,0],[0,1,0],[0,0,1]];

  if (animState === 9) return null;

  const isAimingState = (animState > 2.5 && animState < 4.5);
  const isCrouchingState = (animState > 4.5 && animState < 5.5);
  const torsoTwist = isAimingState ? animParams.aiming.torsoTwist : 0;
  const bodyLean = isCrouchingState ? animParams.crouching.bodyLean : 0;
  const bodyLower = isCrouchingState ? animParams.crouching.bodyLower : 0;
  const modelLower = isCrouchingState ? animParams.crouching.modelLower : 0;
  const hasBodyRot = torsoTwist !== 0 || bodyLean !== 0;

  function getBodyTransform() {
    if (!hasBodyRot) return null;
    const tp = pivots[0];
    if (!tp) return null;
    const bodyPivot = [tp.x * dims[0] * voxelSize, tp.y * dims[1] * voxelSize, tp.z * dims[2] * voxelSize];
    let rot = identity;
    if (bodyLean !== 0) rot = matMul3(rotationX(bodyLean), rot);
    if (torsoTwist !== 0) rot = matMul3(rotationY(torsoTwist), rot);
    return { pivot: bodyPivot, rot };
  }

  const bodyOffset = [0, -bodyLower - modelLower, 0];

  if (gid === 0) {
    const bodyTransform = getBodyTransform();
    if (!bodyTransform && bodyLower === 0 && modelLower === 0) return null;
    const chain = bodyTransform ? [bodyTransform] : [];
    return { chain, offset: bodyOffset };
  }

  const PARENT_OF = { 8: 2, 9: 3, 6: 4, 7: 5 };
  const CHILD_GROUPS = [6, 7, 8, 9];

  let parentResult = null;
  if (gid in PARENT_OF) {
    parentResult = computeGroupRotation(PARENT_OF[gid], dims, voxelSize, animState, animTime, animSpeed, animParams, pivots);
  }

  const p = pivots[gid];
  if (!p) return parentResult;
  const ownPivot = [p.x * dims[0] * voxelSize, p.y * dims[1] * voxelSize, p.z * dims[2] * voxelSize];

  let offset;
  if (parentResult) {
    offset = parentResult.offset;
  } else {
    const off = animParams.jointOffset[gid] || { x: 0, y: 0, z: 0 };
    offset = [off.x, off.y, off.z];
  }
  if (bodyLower !== 0 && (gid === 1 || gid === 2 || gid === 3)) {
    offset = [offset[0], offset[1] - bodyLower, offset[2]];
  }
  if (modelLower !== 0 && (gid === 1 || gid === 2 || gid === 3 || gid === 4 || gid === 5)) {
    offset = [offset[0], offset[1] - modelLower, offset[2]];
  }

  let ownRot = null;

  if (gid === 1) {
    let headYaw = 0, headPitch = 0, headTilt = 0;
    if (animState > 1.5 && animState < 2.5) {
      const p2 = animParams.looking;
      headYaw = Math.sin(animTime * p2.headYawFreq) * p2.headYaw;
      headPitch = Math.sin(animTime * p2.headPitchFreq) * p2.headPitch;
    } else if (animState > 2.5 && animState < 4.5) {
      const p2 = animParams.aiming;
      headYaw = p2.headYaw; headPitch = p2.headPitch; headTilt = p2.headTilt;
    } else if (animState > 4.5 && animState < 5.5) {
      headPitch = animParams.crouching.headPitch;
    } else {
      ownRot = identity;
    }
    if (ownRot === null) ownRot = matMul3(rotationY(headYaw), matMul3(rotationX(headPitch), rotationZ(headTilt)));
  } else if (gid === 2) {
    const as = animParams.armSwing;
    let swing = 0;
    let reach = 0;
    if (animState > 0.5 && animState < 1.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      swing = as.signL * wp.pose.armSwingL;
    } else if (animState > 2.5 && animState < 4.5) {
      swing = as.signL * animParams.aiming.armSwingL;
      reach = animParams.aiming.shoulderReachL;
    } else if (animState > 4.5 && animState < 5.5) {
      swing = as.signL * animParams.crouching.armSwingL;
    } else {
      ownRot = rotationZ(animParams.restPose.leftArmZ);
    }
    if (ownRot === null) {
      ownRot = matMul3(rotationY(reach), matMul3(rotationByAxis(as.axisL, swing), rotationZ(animParams.restPose.leftArmZ)));
    }
  } else if (gid === 3) {
    const as = animParams.armSwing;
    let swing = 0;
    let reach = 0;
    if (animState > 0.5 && animState < 1.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      swing = as.signR * wp.pose.armSwingR;
    } else if (animState > 2.5 && animState < 4.5) {
      swing = as.signR * animParams.aiming.armSwingR;
      reach = animParams.aiming.shoulderReachR;
    } else if (animState > 4.5 && animState < 5.5) {
      swing = as.signR * animParams.crouching.armSwingR;
    } else {
      ownRot = rotationZ(animParams.restPose.rightArmZ);
    }
    if (ownRot === null) {
      ownRot = matMul3(rotationY(reach), matMul3(rotationByAxis(as.axisR, swing), rotationZ(animParams.restPose.rightArmZ)));
    }
  } else if (gid === 4) {
    const ls = animParams.legStride;
    const twist = animParams.legTwist.leftRest;
    let stride = 0;
    if (animState > 0.5 && animState < 1.5 || animState > 2.5 && animState < 3.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      stride = ls.signL * wp.pose.legStrideL;
    } else if (animState > 4.5 && animState < 5.5) {
      stride = ls.signL * animParams.crouching.legStrideL;
    } else {
      ownRot = rotationY(twist);
    }
    if (ownRot === null) ownRot = matMul3(rotationByAxis(ls.axisL, stride), rotationY(twist));
  } else if (gid === 5) {
    const ls = animParams.legStride;
    const twist = animParams.legTwist.rightRest;
    let stride = 0;
    if (animState > 0.5 && animState < 1.5 || animState > 2.5 && animState < 3.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      stride = ls.signR * wp.pose.legStrideR;
    } else if (animState > 4.5 && animState < 5.5) {
      stride = ls.signR * animParams.crouching.legStrideR;
    } else {
      ownRot = rotationY(twist);
    }
    if (ownRot === null) ownRot = matMul3(rotationByAxis(ls.axisR, stride), rotationY(twist));
  } else if (gid === 8) {
    const eb = animParams.elbowBend;
    let bend = eb.signL * eb.leftRest;
    let twist = eb.twistL;
    if (animState > 0.5 && animState < 1.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      bend = eb.signL * wp.pose.elbowBendL;
      twist = wp.pose.forearmTwistL;
    } else if (animState > 2.5 && animState < 4.5) {
      bend = eb.signL * animParams.aiming.elbowBendL;
    }
    ownRot = rotationByAxis(eb.axisL, bend);
    if (twist !== 0) ownRot = matMul3(ownRot, rotationX(twist));
  } else if (gid === 9) {
    const eb = animParams.elbowBend;
    let bend = eb.signR * eb.rightRest;
    let twist = eb.twistR;
    if (animState > 0.5 && animState < 1.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      bend = eb.signR * wp.pose.elbowBendR;
      twist = wp.pose.forearmTwistR;
    } else if (animState > 2.5 && animState < 4.5) {
      bend = eb.signR * animParams.aiming.elbowBendR;
    }
    ownRot = rotationByAxis(eb.axisR, bend);
    if (twist !== 0) ownRot = matMul3(ownRot, rotationX(twist));
  } else if (gid === 6) {
    const kb = animParams.kneeBend;
    let bend = kb.signL * kb.leftRest;
    if (animState > 0.5 && animState < 1.5 || animState > 2.5 && animState < 3.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      bend = kb.signL * wp.pose.kneeBendL;
    } else if (animState > 4.5 && animState < 5.5) {
      bend += kb.signL * animParams.crouching.kneeBendL;
    }
    ownRot = rotationByAxis(kb.axisL, bend);
  } else if (gid === 7) {
    const kb = animParams.kneeBend;
    let bend = kb.signR * kb.rightRest;
    if (animState > 0.5 && animState < 1.5 || animState > 2.5 && animState < 3.5) {
      const wp = getWalkPose(animTime, animSpeed, animParams);
      bend = kb.signR * wp.pose.kneeBendR;
    } else if (animState > 4.5 && animState < 5.5) {
      bend += kb.signR * animParams.crouching.kneeBendR;
    }
    ownRot = rotationByAxis(kb.axisR, bend);
  }

  if (ownRot === null) return parentResult;

  const ownTransform = { pivot: ownPivot, rot: ownRot };
  let chain = parentResult ? [ownTransform, ...parentResult.chain] : [ownTransform];

  if (hasBodyRot && (gid === 1 || gid === 2 || gid === 3)) {
    const bodyTransform = getBodyTransform();
    if (bodyTransform) chain = [...chain, bodyTransform];
  }

  return { chain, offset };
}

// === POSE A VOXEL LIST ===
// Given a list of {x, y, z, mid, gid} voxels, animation state, and params,
// returns a new array of {x, y, z, mid} with forward kinematics applied.
// This is the main entry point for the voxel editor's character preview.
export function poseVoxels(voxels, dims, animState, animTime, animSpeed, animParams, pivots) {
  const voxelSize = 1.0;
  const isWalkState = (animState > 0.5 && animState < 1.5) || (animState > 2.5 && animState < 3.5);
  const walkPose = isWalkState ? getWalkPose(animTime, animSpeed, animParams) : null;
  const bodyBobY = walkPose ? walkPose.bodyBobY : 0;
  const weightShiftX = walkPose ? walkPose.weightShiftX : 0;

  const result = new Array(voxels.length);
  for (let i = 0; i < voxels.length; i++) {
    const v = voxels[i];
    let px = v.x;
    let py = v.y;
    let pz = v.z;

    if (v.gid >= 0) {
      const r = computeGroupRotation(v.gid, dims, voxelSize, animState, animTime, animSpeed, animParams, pivots);
      if (r && r.chain) {
        let pos = [v.x, v.y, v.z];
        for (const { pivot, rot } of r.chain) {
          const rel = [pos[0] - pivot[0], pos[1] - pivot[1], pos[2] - pivot[2]];
          const transformed = matVec3(rot, rel);
          pos = [transformed[0] + pivot[0], transformed[1] + pivot[1], transformed[2] + pivot[2]];
        }
        const off = r.offset || [0, 0, 0];
        px = pos[0] + off[0];
        py = pos[1] + off[1];
        pz = pos[2] + off[2];
      }
    }

    if (isWalkState) {
      px += weightShiftX;
      py += bodyBobY;
    }

    result[i] = { x: px, y: py, z: pz, mid: v.mid };
  }
  return result;
}

// === PARSE CHARACTER JSON ===
// Loads a character JSON (Civilian1.json format) and returns a structured object
// with voxels (including group IDs), dims, pivots, and animParams.
export function parseCharacterData(data) {
  const dims = data.dims || [96, 96, 96];
  const voxels = [];
  const groupMap = new Map();

  if (data.voxels) {
    for (const v of data.voxels) {
      if (Array.isArray(v)) {
        const [x, y, z, mid] = v;
        if (mid !== 0) {
          voxels.push({ x, y, z, mid, gid: 0 });
        }
      }
    }
  }

  if (data.groups) {
    if (Array.isArray(data.groups)) {
      for (const [x, y, z, gid] of data.groups) groupMap.set(`${x},${y},${z}`, gid);
    } else {
      for (const [key, gid] of Object.entries(data.groups)) groupMap.set(key, gid);
    }
  }

  for (const v of voxels) {
    const gid = groupMap.get(`${v.x},${v.y},${v.z}`);
    if (gid !== undefined) v.gid = gid;
  }

  let pivots = JSON.parse(JSON.stringify(DEFAULT_PIVOTS));
  if (data.pivots) {
    pivots = data.pivots;
  }

  let animParams = JSON.parse(JSON.stringify(DEFAULT_PARAMS));
  if (data.animParams) {
    const defaults = JSON.parse(JSON.stringify(DEFAULT_PARAMS));
    animParams = defaults;
    for (const section of Object.keys(data.animParams)) {
      if (defaults[section] && typeof defaults[section] === 'object' && !Array.isArray(defaults[section])) {
        animParams[section] = Object.assign({}, defaults[section], data.animParams[section]);
      } else {
        animParams[section] = data.animParams[section];
      }
    }
  }

  // Build materials lookup
  let materials = [];
  if (data.materials) materials = data.materials;

  return { dims, voxels, pivots, animParams, materials };
}
