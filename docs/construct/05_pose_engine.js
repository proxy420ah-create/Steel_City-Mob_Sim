'use strict';
/* construct/05_pose_engine.js — PARTY handoff + OUTFIT, poseEngine — the animator's rig math, ported flat-matrix.
   Split from construct.html (was lines 969-1117); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ═══ PERSISTENT PARTY — followers cross pages. sessionStorage 'dvm_party' is the handoff:
   the Commission writes who's heeling on portal-E; the Construct writes who's coming home
   on door-E. Each follower is the real poseEngine rig — gait, transitions, gaze, eyes. ═══ */
const PARTY = JSON.parse(new URLSearchParams(location.search).get('party') || sessionStorage.getItem('dvm_party') || '[]');   // ?party= deep-links a party spec (probes); sessionStorage is the live handoff
sessionStorage.removeItem('dvm_party');                                                                                     // handoff is one-shot — dvm_fol is the standing record from here
const OUTFIT = { 3: 126, 4: 126, 6: 126, 7: 105 };   // Civilian_01 "Suit Blue" region→mat remap — same constant the Commission applies

function poseEngine(rig) {
  const DEF = {                                                // DEFAULT_PARAMS (animator ≡ C# defaults) — only the sections Idle/Walking read
    restPose:    { leftArmZ: -Math.PI / 2, rightArmZ: Math.PI / 2 },
    jointOffset: { 1: { x: 0, y: 0, z: 0 }, 2: { x: 0, y: 0, z: 0 }, 3: { x: 0, y: 0, z: 0 }, 4: { x: 0, y: 0, z: 0 }, 5: { x: 0, y: 0, z: 0 } },
    armSwing:    { axisL: 0, axisR: 0, signL: 1, signR: 1 },
    legStride:   { axisL: 0, axisR: 0, signL: 1, signR: 1 },
    legTwist:    { leftRest: 0, rightRest: 0 },
    elbowBend:   { leftRest: 0, rightRest: 0, axisL: 1, axisR: 1, signL: 1, signR: -1, twistL: 0, twistR: 0 },
    kneeBend:    { leftRest: 0, rightRest: 0, axisL: 0, axisR: 0, signL: 1, signR: 1 },
    walkKeyframes: { autoMirror: true, cycleDuration: 1.2, bodyBob: { enabled: true, amplitude: .6 }, weightShift: { enabled: true, amplitude: .4 },
      kf0: { armSwingL: .3, armSwingR: -.3, legStrideL: -.4, legStrideR: .4, elbowBendL: .1, elbowBendR: .1, kneeBendL: 0, kneeBendR: 0, forearmTwistL: 0, forearmTwistR: 0 },
      kf1: { armSwingL: 0, armSwingR: 0, legStrideL: .3, legStrideR: -.1, elbowBendL: .3, elbowBendR: .3, kneeBendL: .8, kneeBendR: .15, forearmTwistL: 0, forearmTwistR: 0 },
      kf2: null, kf3: null },
  };
  const ap = rig.animParams || {}, P = {};
  for (const k in DEF) P[k] = Object.assign({}, DEF[k], ap[k]);   // animator load rule: per-section shallow merge over the defaults
  const dims = rig.dims || [96, 96, 96], piv = {}, jo = {};
  for (const g in rig.pivots || {}) { const p = rig.pivots[g]; piv[g] = [p.x * dims[0], p.y * dims[1], p.z * dims[2]]; }   // normalized → voxel units
  for (let g = 1; g <= 5; g++) { const o = P.jointOffset[g] || {}; jo[g] = [o.x || 0, o.y || 0, o.z || 0]; }
  jo[6] = jo[4]; jo[7] = jo[5]; jo[8] = jo[2]; jo[9] = jo[3];   // shins/forearms inherit their parent's offset
  const vox = rig.voxels, N = vox.length, grp = rig.groups || {};
  const vx = new Float32Array(N), vy = new Float32Array(N), vz = new Float32Array(N), vg = new Uint8Array(N);
  vox.forEach((v, i) => { vx[i] = v[0]; vy[i] = v[1]; vz[i] = v[2]; vg[i] = grp[v[0] + ',' + v[1] + ',' + v[2]] || 0; });
  /* rotations — row-major 3×3, the animator/C#/HLSL convention (rY is Unity-handed: [[c,0,-s],[0,1,0],[s,0,c]]).
     Scratch pool: every helper returns a pooled slot, never a fresh array — evalPose ran ~50
     allocations/call at 60Hz×bodies = the 2-second GC hitch the boss felt. _pi resets at the top
     of each evalPose, so slots are monotonic within a call and nothing aliases a live input. */
  const _m9 = [], _v3 = []; for (let i = 0; i < 128; i++) _m9.push([0, 0, 0, 0, 0, 0, 0, 0, 0]);
  for (let i = 0; i < 128; i++) _v3.push([0, 0, 0]);                     // 128/128 — the aim+IK path measured _pi=75;
  let _pi = 0, _pw = 0; const nx9 = () => _m9[_pi++ % _m9.length], nx3 = () => _v3[_pi++ % _v3.length];   // the old 64-slot _v3
  const _poolCheck = () => { if (_pi > Math.min(_m9.length, _v3.length) && !_pw) { _pw = 1; console.warn('POSE POOL OVERFLOW — _pi', _pi); } };   // DESIGNER's nit: wrap aliases silently — say so once (conservative: shared counter, smallest pool)
  const ID9 = [1, 0, 0, 0, 1, 0, 0, 0, 1];
  const rX = (a, o = nx9()) => { const c = Math.cos(a), s = Math.sin(a); o[0] = 1; o[1] = 0; o[2] = 0; o[3] = 0; o[4] = c; o[5] = -s; o[6] = 0; o[7] = s; o[8] = c; return o; };
  const rY = (a, o = nx9()) => { const c = Math.cos(a), s = Math.sin(a); o[0] = c; o[1] = 0; o[2] = -s; o[3] = 0; o[4] = 1; o[5] = 0; o[6] = s; o[7] = 0; o[8] = c; return o; };
  const rZ = (a, o = nx9()) => { const c = Math.cos(a), s = Math.sin(a); o[0] = c; o[1] = -s; o[2] = 0; o[3] = s; o[4] = c; o[5] = 0; o[6] = 0; o[7] = 0; o[8] = 1; return o; };
  const rA = (ax, a, o) => ax === 1 ? rY(a, o) : ax === 2 ? rZ(a, o) : rX(a, o);
  const mm = (a, b, o = nx9()) => {
    o[0] = a[0] * b[0] + a[1] * b[3] + a[2] * b[6]; o[1] = a[0] * b[1] + a[1] * b[4] + a[2] * b[7]; o[2] = a[0] * b[2] + a[1] * b[5] + a[2] * b[8];
    o[3] = a[3] * b[0] + a[4] * b[3] + a[5] * b[6]; o[4] = a[3] * b[1] + a[4] * b[4] + a[5] * b[7]; o[5] = a[3] * b[2] + a[4] * b[5] + a[5] * b[8];
    o[6] = a[6] * b[0] + a[7] * b[3] + a[8] * b[6]; o[7] = a[6] * b[1] + a[7] * b[4] + a[8] * b[7]; o[8] = a[6] * b[2] + a[7] * b[5] + a[8] * b[8];
    return o; };
  /* gait — 4 keyframes at phase 0/.25/.5/.75, kf2/kf3 = L↔R mirror of kf0/kf1, cyclic Catmull-Rom; phase from the integrated gaitTime */
  const WK = P.walkKeyframes;
  const mirror = k => ({ armSwingL: k.armSwingR, armSwingR: k.armSwingL, legStrideL: k.legStrideR, legStrideR: k.legStrideL, elbowBendL: k.elbowBendR, elbowBendR: k.elbowBendL,
                         kneeBendL: k.kneeBendR, kneeBendR: k.kneeBendL, forearmTwistL: k.forearmTwistR, forearmTwistR: k.forearmTwistL });
  const KF = [WK.kf0, WK.kf1, WK.kf2 || (WK.autoMirror ? mirror(WK.kf0) : WK.kf0), WK.kf3 || (WK.autoMirror ? mirror(WK.kf1) : WK.kf1)];
  const cr = (a, b, c, d, t) => .5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
  /* ═══ IK AIM channel — character_animator.html's simulated ArmAimSolver, ported flat-matrix.
     2-bone law-of-cosines: shoulder places the elbow in the hint plane, forearm cumulative lands the
     hand axis on target. Solved rotations REPLACE the group's ownRot blended (Unity Slerp into
     ikOverrides). gid 2/8 = the arm that reads RIGHT in the Unity view (this tool's 'L Arm'). ═══ */
  const UP3 = [0, 1, 0];
  const dot3 = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2],
        sub3 = (a, b, o = nx3()) => { o[0] = a[0] - b[0]; o[1] = a[1] - b[1]; o[2] = a[2] - b[2]; return o; },
        norm3 = (v, o = nx3()) => { const l = Math.hypot(v[0], v[1], v[2]); if (l > 1e-9) { o[0] = v[0] / l; o[1] = v[1] / l; o[2] = v[2] / l; } else { o[0] = 0; o[1] = 0; o[2] = 1; } return o; },
        len3 = v => Math.hypot(v[0], v[1], v[2]),
        cross3 = (a, b, o = nx3()) => { o[0] = a[1] * b[2] - a[2] * b[1]; o[1] = a[2] * b[0] - a[0] * b[2]; o[2] = a[0] * b[1] - a[1] * b[0]; return o; },
        mT = (a, o = nx9()) => { o[0] = a[0]; o[1] = a[3]; o[2] = a[6]; o[3] = a[1]; o[4] = a[4]; o[5] = a[7]; o[6] = a[2]; o[7] = a[5]; o[8] = a[8]; return o; };
  function basisFromFwd(f, u, o = nx9()) {
    if (Math.abs(dot3(f, u)) > .95) u = Math.abs(f[1]) < .9 ? UP3 : [1, 0, 0];
    const r = norm3(cross3(u, f)), up = cross3(f, r);
    o[0] = r[0]; o[1] = up[0]; o[2] = f[0]; o[3] = r[1]; o[4] = up[1]; o[5] = f[1]; o[6] = r[2]; o[7] = up[2]; o[8] = f[2];
    return o;
  }
  function basisDelta(bF, sF, up) { const s = basisFromFwd(norm3(sF), up), b = basisFromFwd(norm3(bF), up); return mm(s, mT(b)); }
  function perpOf(h, ax) { const d = dot3(h, ax); _tmp3[0] = ax[0] * d; _tmp3[1] = ax[1] * d; _tmp3[2] = ax[2] * d;
    let p = sub3(h, _tmp3);                                                          // h − ax·(h·ax): the hint minus its axis component
    if (dot3(p, p) < 1e-8) { p = nx3(); if (Math.abs(ax[0]) < .9) { p[0] = ax[1]; p[1] = -ax[0]; p[2] = 0; } else { p[0] = 0; p[1] = ax[2]; p[2] = -ax[1]; } }
    return norm3(p); }
  function scaledRot(m, w, o = nx9()) {                                                   // rotation-matrix slerp: m scaled toward identity by 1−w
    if (w <= 1e-4) return ID9; if (w >= .9999) return m;
    const cA = Math.max(-1, Math.min(1, (m[0] + m[4] + m[8] - 1) / 2)), a0 = Math.acos(cA), s = 2 * Math.sin(a0);
    if (Math.abs(s) < 1e-5) return w < .5 ? ID9 : m;
    const ax = (m[7] - m[5]) / s, ay = (m[2] - m[6]) / s, az = (m[3] - m[1]) / s, ang = a0 * w, c = Math.cos(ang), sn = Math.sin(ang), ic = 1 - c;
    o[0] = c + ax * ax * ic; o[1] = ax * ay * ic - az * sn; o[2] = ax * az * ic + ay * sn;
    o[3] = ay * ax * ic + az * sn; o[4] = c + ay * ay * ic; o[5] = ay * az * ic - ax * sn;
    o[6] = az * ax * ic - ay * sn; o[7] = az * ay * ic + ax * sn; o[8] = c + az * az * ic;
    return o;
  }
  const _tmp3 = [0, 0, 0], _tmp3b = [0, 0, 0];
  /* hoisted per-call structures — the pose matrixes change, the containers don't (see pool note above) */
  const _FWD3 = [0, 0, 1], _hint = [0, 0, 0], _elbow = [0, 0, 0];
  const _gPose = {}, _gOut = { pose: _gPose, bobY: 0, shiftX: 0 }, _sol = { qArm: null, qFore: null };
  const _C = [null, { c: [], o: jo[1] },                                                  // 0 Body: none · 1 Head: rest-identity (aim swaps c)
    { c: [[piv[2], null]], o: jo[2] }, { c: [[piv[3], null]], o: jo[3] },                 // 2/3 arms: restPose Z drop, then swing
    { c: [[piv[4], null]], o: jo[4] }, { c: [[piv[5], null]], o: jo[5] },                 // 4/5 legs: hip stride
    { c: [[piv[6], null], [piv[4], null]], o: jo[4] }, { c: [[piv[7], null], [piv[5], null]], o: jo[5] },   // 6/7 shins: knee then hip
    { c: [[piv[8], null], [piv[2], null]], o: jo[2] }, { c: [[piv[9], null], [piv[3], null]], o: jo[3] }];  // 8/9 forearms: elbow then shoulder
  const _headPair = [piv[1], null], _headC = [_headPair], _noC = [];
  let _hand = null;
  function handPoint() {                                                                 // fist tip: farthest gid-8 voxel from the shoulder pivot, cached
    if (!_hand) {
      const sv = piv[2]; let hv = null, best = -1;
      for (let i = 0; i < N; i++) if (vg[i] === 8) { const p = [vx[i] + .5, vy[i] + .5, vz[i] + .5], d = dot3(sub3(p, sv, _tmp3), sub3(p, sv, _tmp3b)); if (d > best) { best = d; hv = p; } }
      _hand = hv ? { hv, axis: norm3(sub3(hv, piv[8], _tmp3)).slice() } : null;           // .slice() — the cache must outlive the pool slots it borrowed
    }
    return _hand;
  }
  function ikSolve(T, reach) {                                                           // T = target in model voxel-index coords
    const sv = piv[2], ev = piv[8], hand = handPoint();
    if (!sv || !ev || !hand) return null;
    const st = sub3(T, sv), dist = len3(st), toT = nx3();
    if (dist > 1e-6) { toT[0] = st[0] / dist; toT[1] = st[1] / dist; toT[2] = st[2] / dist; } else { toT[0] = 0; toT[1] = 0; toT[2] = 1; }
    const bindArm = norm3(sub3(ev, sv)), L1 = len3(sub3(ev, sv)), L2 = len3(sub3(hand.hv, ev)), UP = UP3;
    let qArm;
    if (L1 < 1e-4 || L2 < 1e-4) qArm = basisDelta(bindArm, toT, UP);
    else {
      const d = Math.min(Math.max(dist, Math.abs(L1 - L2) + .01), (L1 + L2) * reach);
      const cosA = Math.min(1, Math.max(-1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d))), angA = Math.acos(cosA);
      const sideX = Math.sign(sv[0] / dims[0] - .5) || 1;                                 // elbow hints outward on the arm's own side
      _hint[0] = sideX * .4; _hint[1] = -1; _hint[2] = .2;
      const bendRef = perpOf(_hint, toT);
      const ca = Math.cos(angA), sa = Math.sin(angA);
      _elbow[0] = toT[0] * ca + bendRef[0] * sa; _elbow[1] = toT[1] * ca + bendRef[1] * sa; _elbow[2] = toT[2] * ca + bendRef[2] * sa;
      qArm = basisDelta(bindArm, norm3(_elbow), UP);
    }
    _sol.qArm = qArm; _sol.qFore = mm(mT(qArm), basisDelta(hand.axis, toT, UP));          // forearm cumulative must land hand axis on target
    return _sol;
  }
  function gait(gt) {
    const dur = WK.cycleDuration, ph = (((gt % dur) + dur) % dur) / dur, s = Math.min(3, Math.floor(ph * 4)), t = ph * 4 - s;
    const A = KF[(s + 3) % 4], B = KF[s], C = KF[(s + 1) % 4], D = KF[(s + 2) % 4];
    for (const k in B) _gPose[k] = cr(A[k] || 0, B[k] || 0, C[k] || 0, D[k] || 0, t);
    _gOut.bobY = WK.bodyBob.enabled ? -Math.cos(ph * 4 * Math.PI) * WK.bodyBob.amplitude : 0;         // lowest at contact, highest at mid-stance
    _gOut.shiftX = WK.weightShift.enabled ? Math.sin(ph * 2 * Math.PI) * WK.weightShift.amplitude : 0;  // lateral weight shift over the stance leg
    return _gOut;
  }
  /* out[3i..3i+2] ← posed voxel-index position of voxel i under (state, gaitTime, aim) — aim = {T:[ix,iy,iz] model coords, blend, head} or null */
  function evalPose(state, gt, out, aim) {
    _pi = 0;                                                                             // scratch slots run monotonic per call — nothing aliases a live input
    const AS = P.armSwing, LS = P.legStride, EB = P.elbowBend, KB = P.kneeBend, RP = P.restPose, LT = P.legTwist;
    const g = state === 1 ? gait(gt) : null;                   // Idle: rest pose only · Walking: rest pose + gait + bob + shift
    const k = state === 11 ? { armSwingL: .2, armSwingR: .2, legStrideL: -1.45, legStrideR: -1.45, kneeBendL: 1.45, kneeBendR: 1.45,   // Sit (animator DEFAULT_PARAMS.sitting): thighs forward ~83°, shins fold down, torso upright, no bob. Stride sign is flipped vs the animator's +1.45 — fitted: all 12,608 voxels land inside the authored vinny_seated_preview composite
                                elbowBendL: EB.leftRest, elbowBendR: EB.rightRest, forearmTwistL: EB.twistL, forearmTwistR: EB.twistR } : g && g.pose;
    const armL = mm(rA(AS.axisL, AS.signL * (k ? k.armSwingL : 0)), rZ(RP.leftArmZ)), armR = mm(rA(AS.axisR, AS.signR * (k ? k.armSwingR : 0)), rZ(RP.rightArmZ));
    const legL = mm(rA(LS.axisL, LS.signL * (k ? k.legStrideL : 0)), rY(LT.leftRest)), legR = mm(rA(LS.axisR, LS.signR * (k ? k.legStrideR : 0)), rY(LT.rightRest));
    const twL = k ? k.forearmTwistL : EB.twistL, twR = k ? k.forearmTwistR : EB.twistR;
    let foreL = rA(EB.axisL, EB.signL * (k ? k.elbowBendL : EB.leftRest)), foreR = rA(EB.axisR, EB.signR * (k ? k.elbowBendR : EB.rightRest));
    if (twL) foreL = mm(foreL, rX(twL)); if (twR) foreR = mm(foreR, rX(twR));
    const shinL = rA(KB.axisL, KB.signL * (k ? k.kneeBendL : KB.leftRest)), shinR = rA(KB.axisR, KB.signR * (k ? k.kneeBendR : KB.rightRest));
    _C[2].c[0][1] = armL; _C[3].c[0][1] = armR;
    _C[4].c[0][1] = legL; _C[5].c[0][1] = legR;
    _C[6].c[0][1] = shinL; _C[6].c[1][1] = legL; _C[7].c[0][1] = shinR; _C[7].c[1][1] = legR;
    _C[8].c[0][1] = foreL; _C[8].c[1][1] = armL; _C[9].c[0][1] = foreR; _C[9].c[1][1] = armR;
    _C[1].c = _noC;                                                      // head defaults to rest-identity
    if (aim) {                                                           // IK AIM channel — solves REPLACE the arm chain's ownRot, blended (over ANY state)
      const sol = ikSolve(aim.T, .96);
      if (sol) {
        const ikArm = mm(scaledRot(mm(sol.qArm, mT(armL)), aim.blend), armL);
        const ikFore = mm(scaledRot(mm(sol.qFore, mT(foreL)), aim.blend), foreL);
        _C[2].c[0][1] = ikArm; _C[8].c[0][1] = ikFore; _C[8].c[1][1] = ikArm;
        if (aim.head > 0) {                                              // eyes take the aim too — head bind-forward → target dir, blended
          const d = sub3(aim.T, piv[1]), toT = len3(d) > 1e-6 ? norm3(d) : _FWD3;
          _headPair[1] = scaledRot(basisDelta(_FWD3, toT, UP3), aim.head); _C[1].c = _headC;
        }
      }
    }
    _poolCheck();
    const sx = g ? g.shiftX : 0, by = g ? g.bobY : 0;
    for (let i = 0; i < N; i++) {
      const ch = _C[vg[i]]; let x = vx[i], y = vy[i], z = vz[i];
      if (ch) {
        for (let c = 0; c < ch.c.length; c++) {
          const pv = ch.c[c][0], m = ch.c[c][1], dx = x - pv[0], dy = y - pv[1], dz = z - pv[2];
          x = pv[0] + m[0] * dx + m[1] * dy + m[2] * dz; y = pv[1] + m[3] * dx + m[4] * dy + m[5] * dz; z = pv[2] + m[6] * dx + m[7] * dy + m[8] * dz;
        }
        x += ch.o[0]; y += ch.o[1]; z += ch.o[2];
      }
      out[3 * i] = x + sx; out[3 * i + 1] = y + by; out[3 * i + 2] = z;
    }
  }
  return { N, evalPose };
}
/* ═══ /POSE ENGINE ═══ */
