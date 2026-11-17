// _designer_body_check.mjs — DVM-2269: the DESIGNER seat body on thecommission.html.
// Verifies the seatBody factory mounted BOTH bodies, DESIGNER anchored to its own ring
// chair (purple suit), orders/gaze/POV work per-body, and the construct→commission
// party return heels DESIGNER1 out of the portal on follow.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp({ page: 'thecommission', settle: 2500 });

// ── mount ──────────────────────────────────────────────────────────────
check('CHAIR body mounted',   await evl(`!!window.__chairbody && !!__seatBodies.CHAIR`));
check('DESIGNER body mounted', await evl(`!!window.__designerbody && !!__seatBodies.DESIGNER`));
check('eyes registries',       await evl(`!!__seatEyes.CHAIR && !!__seatEyes.DESIGNER && __eyes === __seatEyes.CHAIR`));

// ── seat anchoring: both on the ring, same radius, different angle, seated pose ──
const pos = await evl(`(() => {
  const c = __chairbody.root.position, d = __designerbody.root.position;
  return { c:[c.x,c.y,c.z], d:[d.x,d.y,d.z],
    cr: Math.hypot(c.x+.015, c.z+.105), dr: Math.hypot(d.x+.015, d.z+.105),
    sep: Math.hypot(c.x-d.x, c.z-d.z) };
})()`);
check('both seated (y = SEAT_OFF[1], equal)', pos && pos.c[1] === pos.d[1] && pos.d[1] !== 0, pos && 'y=' + pos.d[1]);
check('DESIGNER on ring radius', pos && Math.abs(pos.dr - pos.cr) < 0.05, pos && 'cr=' + pos.cr.toFixed(3) + ' dr=' + pos.dr.toFixed(3));
check('distinct chair', pos && pos.sep > 0.8, pos && 'sep=' + pos.sep.toFixed(3));
check('starts posted+seated', await evl(`__designerbody.order() === 'post'`));

// ── purple suit: same rig + same OUTFIT path as _designer_char_check (region 3 → mat 126 = #bc8cff) ──
const purp = await evl(`(() => {
  const m = __designerbody.mesh, c = m.instanceColor;
  if (!c) return null;
  let best = null, bd = 1e9;                                    // nearest voxel color to linear #bc8cff (0.504,0.262,1.0)
  for (let i = 0; i < c.count; i++) {
    const d = (c.getX(i)-.504)**2 + (c.getY(i)-.262)**2 + (c.getZ(i)-1)**2;
    if (d < bd) { bd = d; best = [c.getX(i), c.getY(i), c.getZ(i)]; }
  }
  return best;
})()`);
check('purple suit renders', purp && Math.abs(purp[0]-.504)<.08 && Math.abs(purp[1]-.262)<.08 && Math.abs(purp[2]-1)<.05,
  purp && 'linear ' + purp.map(n=>n.toFixed(3)).join(','));

// ── orders: wait → follow → post, and a real walk-out + walk-home ──
await evl(`__designerbody.setOrder('wait')`);
check('order wait applied', await evl(`__designerbody.order() === 'wait'`));
const seatedPos = pos.d;
await evl(`__designerbody.teleport(-3.5, 3.5)`);            // out on the floor — POST must walk home + sit
await evl(`__designerbody.setOrder('post')`);
await sleep(9000);                                          // nav: stand-point leg → radial in → snap+sit
const after = await evl(`(() => { const d = __designerbody.root.position; return { p:[d.x,d.y,d.z], seatedY:d.y!==0 }; })()`);
check('POST walked home', after && Math.hypot(after.p[0]-seatedPos[0], after.p[2]-seatedPos[2]) < 0.15,
  after && 'at ' + after.p.map(n=>n.toFixed(2)).join(','));
check('POST seated again', after && after.seatedY === true);

// ── gaze + POV eyes ─────────────────────────────────────────────────────
await evl(`__designerbody.gaze({x:0,y:1,z:0})`);
check('gaze lands', await evl(`!!(__seatEyes.DESIGNER.gaze())`));
const eye = await evl(`__seatEyes.DESIGNER.eye()`);
check('eye anchor finite', eye && eye.every(Number.isFinite), eye && eye.map(n=>n.toFixed(2)).join(','));
await evl(`__seatEyes.DESIGNER.show()`);
check('PiP on', await evl(`__seatEyes.DESIGNER.on() === true`));
check('shot returns a frame', await evl(`(__seatEyes.DESIGNER.shot()||'').startsWith('data:image/png')`));
await evl(`__seatEyes.DESIGNER.hide()`);
await evl(`__designerbody.ungaze()`);

// ── CHAIR regression: still seated, orders intact, body exclusion registry ──
check('CHAIR still seated at its chair', await evl(`__chairbody.order() === 'post'`), await evl(`__chairbody.order()`));
check('seatRoots holds 2 groups', await evl(`__seatRoots.size === 2`));

// ── inspector: DESIGNER seat gets order chips incl. POV ─────────────────
await evl(`openInspector('DESIGNER')`);
check('DESIGNER inspector order chips', await evl(`!!document.querySelector('#inspector .i-orders #i_follow')`));
check('DESIGNER inspector POV chip',    await evl(`!!document.querySelector('#inspector .i-orders #i_pov')`));
await evl(`document.getElementById('inspector').classList.remove('open')`);

// ── party return: DESIGNER1 heeled through the portal → steps out on follow ──
await evl(`sessionStorage.setItem('dvm_party', JSON.stringify([{char:'DESIGNER1',order:'follow'}]))`);
await evl(`location.href = 'thecommission.html?from=construct'`);
for (let i = 0; i < 120 && !(await evl('!!window.__designerbody')); i++) await sleep(500);
await sleep(1500);
check('DESIGNER returned on follow', await evl(`__designerbody.order() === 'follow'`));
const tp = await evl(`(() => { const d = __designerbody.root.position; return [d.x, d.z]; })()`);
check('DESIGNER stepped out of the portal', tp && Math.hypot(tp[0]+6.4, tp[1]-4.5) < 1.0, tp && tp.map(n=>n.toFixed(2)).join(','));
check('CHAIR stayed posted (not in party)', await evl(`__chairbody.order() === 'post'`));

finish();
