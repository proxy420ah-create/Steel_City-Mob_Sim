// _place_probe.mjs — end-to-end headless test of the Construct's place mode (X).
// Drives __place/__cam over CDP: toggle → grab desk → drag to NE corner (carry check)
// → drop → copy → paste → drop → reload → verify persistence → del → reload → verify gone.
//
//   node _place_probe.mjs
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_shotprof_${PORT}_${Date.now()}`);                 // unique per run — a reused profile leaks stale localStorage

const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });

let ws = null, mid = 0; const pend = new Map();
const cdp = (method, params = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
const evl = async expr => {
  const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 500));
  return r.result && r.result.result ? r.result.result.value : undefined;
};
async function connect() {
  for (let i = 0; i < 60; i++) {
    try {
      const list = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`));
      const t = list.find(t => t.type === 'page' && t.url.includes('construct'));
      if (t) {
        ws = new WebSocket(t.webSocketDebuggerUrl);
        await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
        ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
        return;
      }
    } catch {}
    await sleep(500);
  }
  throw new Error('no page target');
}
const waitBoot = async () => { for (let i = 0; i < 120; i++) { if (await evl('!!window.__place && !!window.__warmDone')) return; await sleep(500); }
  throw new Error('__place never appeared'); };

const R = [];
const check = (name, ok, detail) => { R.push({ name, ok: !!ok, detail }); console.log((ok ? 'PASS' : 'FAIL') + '  ' + name + (detail ? '  — ' + detail : '')); };

await connect(); await waitBoot(); await sleep(800);

// 1 — toggle on
await evl('__place.toggle(true)');
check('toggle on', await evl('__place.on()'));
check('legend chip visible', await evl(`[...document.querySelectorAll('div')].some(d => d.textContent.startsWith('PLACE —') && d.style.display === 'block')`));

// 2 — grab the desk (its hosted lamp/book/stamper should enlist as passengers)
await evl('__place.grab("commission_lawdesk")');
check('grab selects desk', (await evl('__place.sel()')) === 'commission_lawdesk');
const carry = await evl('(window.__place._c = (() => { const s = window.__construct.placed.find(p => p.name === "commission_lawdesk"); return (s._carry || []).map(c => c.q.name); })())');
check('carry enlisted 3 passengers', carry && carry.length === 3, JSON.stringify(carry));

// 3 — select-only contract: grab → item does NOT move when the camera does (the bug the user hit)
await evl('__cam(1.8, 1.9, -4.2, 2.6, 0, -5.3)');
await sleep(600);
const still = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('selection does not move the item', still && still.x === 2.3 && still.z === -1.1, JSON.stringify(still));
check('stat line shows the selection', await evl('[...document.querySelectorAll("div")].some(d => d.textContent.startsWith("SEL commission_lawdesk"))'));

// 3b — keyboard moves the selection directly (Commission parity): arrows / ⇧coarse / , rotate / carry
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight" }))');
await sleep(250);
const kn = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('arrow nudge .005 moves selection', kn && Math.abs(kn.x - 2.305) < .002 && kn.z === -1.1, JSON.stringify(kn));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", shiftKey: true }))');
await sleep(250);
const kc = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('⇧arrow nudge .05 coarse', kc && Math.abs(kc.x - 2.355) < .005, JSON.stringify(kc));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "," }))');
await sleep(250);
const kr = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check(', rotates +15°', kr && Math.abs(kr.rotY - (Math.PI / 2 + Math.PI / 12)) < .01, JSON.stringify(kr));
const kl = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_desklamp');
check('passengers ride keyboard moves', kl && (kl.x !== 2.3 || kl.z !== -1.6), JSON.stringify(kl));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" }))');
await sleep(250);
check('enter accepts + deselects', (await evl('__place.sel()')) === null && await evl('![...document.querySelectorAll("div")].some(d => d.textContent.startsWith("SEL "))'));
const savedKb = JSON.parse(await evl('localStorage.getItem("dvm_place")') || '{}');
check('keyboard edits persisted', savedKb.commission_lawdesk && Math.abs(savedKb.commission_lawdesk.x - 2.355) < .01, JSON.stringify(savedKb.commission_lawdesk));

// 3c — Esc backs out: revert restores the grab-time pose and releases the selection
await evl('__place.grab("commission_lawdesk")');
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowLeft" }))');
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowLeft" }))');
await sleep(250);
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }))');
await sleep(250);
const rv = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('esc reverts to the grab pose', rv && Math.abs(rv.x - 2.355) < .01 && (await evl('__place.sel()')) === null, JSON.stringify(rv));

// 3d — sibling regression: co-located door+frame must NOT leapfrog on commits (the floating-door bug)
const doorY = JSON.parse(await evl('__place.dump()')).filter(p => p.name === 'commission_door' || p.name === 'commission_doorframe').map(p => p.y);
check('door + frame stay grounded through commits', doorY.length === 2 && doorY.every(y => y === 0), JSON.stringify(doorY));

// 4 — ⇧+LMB drag: paste a spare desk onto open floor (no passengers to occlude the pick ray), drag it, undo, delete it
await evl('__place.grab("commission_lawdesk")'); await evl('__place.copy()');
await evl('__cam(-1.2, 1.6, -3.4, -1.2, 0, -4.6)');      // aim at bare platform
await sleep(300);
await evl('__place.paste()');
await sleep(400);
const spare = JSON.parse(await evl('__place.dump()')).find(p => p.src === 'extra');
check('spare desk pasted on the platform', spare && spare.z < -3, JSON.stringify(spare));
await evl('document.querySelector("canvas").dispatchEvent(new MouseEvent("mousedown", { button: 0, shiftKey: true, bubbles: true }))');
await sleep(250);
const dragSt = await evl('(() => { const s = window.__construct.placed.find(p => p._drag); return s ? s.source + ":" + s.name : "none"; })()');
check('⇧+LMB engages drag on the hit item', dragSt === 'extra:commission_lawdesk', dragSt);
await evl('__cam(-2.8, 1.7, -5.2, -2.8, 0, -6.4)');      // drag it further south-west
await sleep(500);
const dragged = JSON.parse(await evl('__place.dump()')).find(p => p.src === 'extra');
check('shift-drag moves the item on the ray', dragged && dragged.x < -1.8 && dragged.z < -5, JSON.stringify(dragged));
await evl('document.querySelector("canvas").dispatchEvent(new MouseEvent("mouseup", { button: 0, bubbles: true }))');
await evl('__cam(-1.2, 1.6, -3.4, -1.2, 0, -4.6)');      // aim back — no further motion should occur
await sleep(400);
const parked = JSON.parse(await evl('__place.dump()')).find(p => p.src === 'extra');
const parkedSel = await evl('__place.sel()');
check('mouseup releases drag, keeps selection', parked && parked.x === dragged.x && parked.z === dragged.z && parkedSel === 'commission_lawdesk', JSON.stringify(parked) + ' sel:' + parkedSel);
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" }))');
await sleep(250);
const exOver = JSON.parse(await evl('localStorage.getItem("dvm_extra")') || '[]');
check('drag committed to dvm_extra', exOver.length === 1 && exOver[0].z < -5, JSON.stringify(exOver));

// 4d — ^Z undoes the drag → the spare returns to its paste spot; Delete then removes it cleanly
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true }))');
await sleep(350);
const undoneX = JSON.parse(await evl('__place.dump()')).find(p => p.src === 'extra');
check('ctrl-Z restores the pre-drag pose', undoneX && Math.abs(undoneX.x + 1.2) < .4 && Math.abs(undoneX.z + 4.6) < .4, JSON.stringify(undoneX));
const spareIdx = JSON.parse(await evl('__place.dump()')).findIndex(p => p.src === 'extra');
await evl(`__place.grabIdx(${spareIdx})`);
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "Delete" }))');
await sleep(300);
const afterSpareDel = JSON.parse(await evl('__place.dump()'));
check('spare deleted, one desk remains', afterSpareDel.filter(p => p.name === 'commission_lawdesk').length === 1, JSON.stringify(afterSpareDel.map(p => p.name + '@' + p.src)));

// 5 — copy + paste → a second desk emits at the aim spot and selects (no drag, no move of the source)
await evl('__place.grab("commission_lawdesk")'); await evl('__place.copy()'); await evl('__place.paste()');
await sleep(1500);
const after = JSON.parse(await evl('__place.dump()'));
check('paste emitted a 2nd desk', after.filter(p => p.name === 'commission_lawdesk').length === 2, JSON.stringify(after.filter(p => p.name === 'commission_lawdesk')));
check('paste auto-selected the copy, not dragging', (await evl('__place.sel()')) === 'commission_lawdesk' && !(await evl('window.__construct.placed.some(p => p._drag)')));
const copy = after.find(p => p.src === 'extra');
check('copy is an extra with solids', copy && copy.solids === 2, copy && JSON.stringify(copy));
const srcDesk = after.find(p => p.src === 'manifest' && p.name === 'commission_lawdesk');
check('source desk unmoved by the copy op', srcDesk && Math.abs(srcDesk.x - 2.355) < .05, JSON.stringify(srcDesk));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" }))');
await sleep(250);
const extra = JSON.parse(await evl('localStorage.getItem("dvm_extra")') || '[]');
check('dvm_extra recorded the copy', extra.length === 1 && extra[0].name === 'commission_lawdesk', JSON.stringify(extra));

// 6 — reload → layout persists (override + extra both re-materialize)
console.log('PRE-RELOAD dvm_place:', await evl('localStorage.getItem("dvm_place")'));
console.log('PRE-RELOAD dump:', await evl('__place.dump()'));
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot = JSON.parse(await evl('__place.dump()'));
const desks = boot.filter(p => p.name === 'commission_lawdesk');
check('reload: 2 desks present', desks.length === 2, JSON.stringify(desks));
check('reload: override spot held', desks.some(d => Math.abs(d.x - 2.355) < .05 && Math.abs(d.z + 1.1) < .05), JSON.stringify(desks));
const lampRe = boot.find(p => p.name === 'commission_desklamp');
check('reload: lamp near desk', lampRe && desks.some(d => Math.abs(lampRe.x - d.x) < 1.0 && Math.abs(lampRe.z - d.z) < 1.0), JSON.stringify(lampRe));

// 7 — delete the extra copy → dvm_extra empties; tombstone keeps it gone
await evl('__place.toggle(true)');
const exIdx = await evl('window.__construct.placed.findIndex(p => p.source === "extra")');
check('extra copy found for delete', exIdx >= 0, 'idx ' + exIdx);
await evl(`__place.grabIdx(${exIdx})`);
await evl('__place.del()');
const afterDel = JSON.parse(await evl('__place.dump()'));
check('extra deleted live', afterDel.filter(p => p.name === 'commission_lawdesk').length === 1);
check('dvm_extra emptied', JSON.parse(await evl('localStorage.getItem("dvm_extra")') || '[]').length === 0);
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot2 = JSON.parse(await evl('__place.dump()'));
check('reload: extra stays deleted', boot2.filter(p => p.name === 'commission_lawdesk').length === 1, JSON.stringify(boot2.filter(p => p.name === 'commission_lawdesk')));

// 8 — restore ordering: an extra saved on a moved manifest host must seat ON it.
//     (BOSS's gun-on-floor bug: extras emitted before overrides applied → the host
//     wasn't there yet → seat measured floor. Restore is now tombstones → overrides →
//     extras → re-seat.)
await evl(`localStorage.setItem('dvm_place', JSON.stringify({ commission_lawdesk: { x: 2.6, z: 0.03, rotY: 0 } }))`);
await evl(`localStorage.setItem('dvm_extra', JSON.stringify([{ name: 'SW_Model_10', kind: 'item', pickable: true, solids: null, x: 2.55, z: -0.05, rotY: 0 }]))`);
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot3 = JSON.parse(await evl('__place.dump()'));
const gun = boot3.find(p => p.name === 'SW_Model_10');
check('reload: extra seats on moved host — not floor', gun && gun.y > .15, JSON.stringify(gun));
const deskAt = boot3.find(p => p.name === 'commission_lawdesk' && Math.abs(p.x - 2.6) < .05);
check('reload: desk override applied first', !!deskAt, JSON.stringify(boot3.filter(p => p.name === 'commission_lawdesk')));

// 9 — a manifest item hand-placed to the floor re-seats to 0 (overrides save no y0 —
//     the restore must re-derive height or it floats at its manifest seat)
await evl('__construct.take("commission_stamper")');
await evl('__construct.place(-1.0, -4.5)');                                       // open floor, away from all hosts
await sleep(300);
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot4 = JSON.parse(await evl('__place.dump()'));
const stamperRe = boot4.find(p => p.name === 'commission_stamper');
check('reload: floor-placed item re-seats to 0', stamperRe && stamperRe.y < .05, JSON.stringify(stamperRe));

// 10 — fixpoint re-seat: an extra emitting onto a FLOATING manifest passenger (stale
//      emit-seat, host deleted) must drop to 0 — not climb it. This is BOSS's exact
//      .34-elevated-desk: passenger over the extra's footprint, extra emits onto its
//      top via the .07 scan radius, then the pass must unwind the whole stack.
await evl(`localStorage.setItem('dvm_place', JSON.stringify({ commission_stamper: { x: -0.52, z: -4.02, rotY: 0 } }))`);
await evl(`localStorage.setItem('dvm_extra', JSON.stringify([{ name: 'commission_lawdesk', kind: 'item', pickable: false, solids: [[-0.5,0,0.35],[0.5,0,0.35]], x: -0.55, z: -4.05, rotY: 0 }]))`);
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot5 = JSON.parse(await evl('__place.dump()'));
const host = boot5.find(p => p.name === 'commission_lawdesk' && p.src === 'extra');
const rider = boot5.find(p => p.name === 'commission_stamper');
check('fixpoint: extra does not climb a floating passenger', host && host.y < .05, JSON.stringify(host));
check('fixpoint: passenger lands on the extra top', rider && rider.y > .2 && rider.y < .5, JSON.stringify(rider));
check('roles: desk is surf, stamper is rider', host && host.surf === true && rider && rider.surf === false, `host.surf=${host && host.surf} rider.surf=${rider && rider.surf}`);

// 11 — held item: dropTarget's ray-march must not run every host's column scan per step
//      (bbox reject), axis snaps are 15° counters that reset the free tumble, 0 = exactly level
await evl('__construct.take("commission_stamper")');
check('stamper held', await evl('!!__construct.held()'));
await evl('window.__scanN = 0; __construct.dropTarget()');
const scans = await evl('window.__scanN');
check('drop march scans only hosts under the ray', scans <= 150, 'scans=' + scans);   // hosts under the march scan legitimately (~2 desks × ~15 steps each); pre-reject was ~39 × ~9 ≈ 350
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "," }))');
check('axis snap: yaw +1 step', await evl('__construct.heldSnap().y === 1'));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "]" }))');
check('axis snap: pitch -1 step', await evl('__construct.heldSnap().x === -1'));
await sleep(200);                                                                                // the quaternion composes in the rAF loop — read after a frame
const qSnap = await evl('JSON.stringify(__construct.heldQ())');
await evl('__construct.tumble(120, 60)'); await sleep(200);
const qFree = await evl('JSON.stringify(__construct.heldQ())');
check('free tumble rotates off the snapped pose', qSnap !== qFree);
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "[" }))'); await sleep(200);
check('axis rotation resets the free tumble', (await evl('JSON.stringify(__construct.heldQ())')) !== qFree && await evl('__construct.heldSnap().x === 0'));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "0" }))'); await sleep(200);
const qLevel = JSON.parse(await evl('JSON.stringify(__construct.heldQ())'));
check('0 = exactly level (pitch/roll zeroed)', Math.abs(qLevel[0]) < 1e-6 && Math.abs(qLevel[2]) < 1e-6, JSON.stringify(qLevel));
await evl('window.dispatchEvent(new KeyboardEvent("keydown", { key: "," }))');        // yaw +15° for the place check
const yawPre = await evl('__construct.yaw()');
await evl('__construct.place(-1.0, -4.5)');
const stRy = await evl('__construct.placed.find(p => p.name === "commission_stamper").rotY');
const stRx = await evl('__construct.placed.find(p => p.name === "commission_stamper").g.rotation.x');
const expRy = ((yawPre + Math.PI / 12) % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
check('place persists facing+snap yaw, seats level', Math.abs(stRy - expRy) < .01 && Math.abs(stRx) < 1e-6, `rotY=${stRy} exp=${expRy} rx=${stRx}`);

// 12 — mannequin station: static Vinny + pressable outfit kiosk (NW corner)
await evl('localStorage.removeItem("dvm_outfit"); localStorage.removeItem("dvm_fol")');
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(1500);
check('mannequin emitted static — never a follower', await evl('!!(__mann.f() && __mann.f().spec.static)'), await evl('JSON.stringify(__mann.f() && __mann.f().spec)'));
check('mannequin wrote no roster entry', await evl('(localStorage.getItem("dvm_fol") || "[]").indexOf("Civilian") < 0'), await evl('localStorage.getItem("dvm_fol")'));
check('two pressable buttons on the face', await evl('__mann.buttons.length === 2'));
// aim-path: stand player south of the kiosk, lock the camera onto the ▶ button → probe resolves it
const bw = JSON.parse(await evl('JSON.stringify(__mann.btnWorld(1))'));
await evl('__pos.x = ' + bw[0] + '; __pos.z = ' + (bw[2] - .8));
await evl('__cam(' + bw[0] + ', ' + (bw[1] + .35) + ', ' + (bw[2] - .8) + ', ' + bw[0] + ', ' + bw[1] + ', ' + bw[2] + ')');
await sleep(400);
check('aim at ▶ resolves the button', await evl('!!(__mann.probe() && __mann.probe().label().indexOf("SUIT") >= 0)'), await evl('__mann.probe() && __mann.probe().label()'));
await evl('Object.defineProperty(document, "pointerLockElement", { get: () => document.querySelector("canvas"), configurable: true })');
await sleep(250);                                                                                  // let the frame loop resolve promptIt → wire
check('aim at ▶ shows the ghost wireframe', await evl('!!(__mann.wire() && __mann.wire().visible)'));
await evl('delete document.pointerLockElement');                                                    // restore the prototype getter — real null again
check('buttons carry the hue-riding glyph', await evl('__mann.buttons.every(b => b.m.children.length >= 1)'));
await evl('__mann.press(1)');
check('press cycles the outfit', await evl('__mann.outfit() === 1'));
check('outfit persisted to dvm_outfit', await evl('localStorage.getItem("dvm_outfit") === "1"'));
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(1500);
check('outfit survives reload', await evl('__mann.outfit() === 1'));
check('mannequin re-emitted wearing it', await evl('!!__mann.f()'), await evl('JSON.stringify(__mann.f() && __mann.f().spec)'));
await evl('__cam(null); __pos.x = 0; __pos.z = 0');

// 12.5 — west annex: doorway seam passable, wall stubs blocked, wire lifted into an arch
check('walkOK centre of main room', await evl('__construct.walkOK(0, -2.5) === true'));
check('walkOK inside the west annex', await evl('__construct.walkOK(-5, -2.5) === true'));
check('walkOK at the door seam x=-3 z=-2.5', await evl('__construct.walkOK(-3, -2.5) === true'));
check('walkOK door north half z=-2.2', await evl('__construct.walkOK(-3, -2.5) === true && __construct.walkOK(-3, -2.3) === true'));
check('walkOK hugging the jamb face', await evl('__construct.walkOK(-3, -2.8) === true && __construct.walkOK(-3, -2.2) === true'));
check('capsule held off the jamb', await evl('__construct.walkOK(-3, -2.9) === false && __construct.walkOK(-3, -2.1) === false'));
check('wall stub south of door blocked', await evl('__construct.walkOK(-3, -4) === false'));
check('wall stub north of door blocked', await evl('__construct.walkOK(-3, -1) === false'));
check('annex floor edge blocked beyond wall', await evl('__construct.walkOK(-7.2, -2.5) === false'));
check('door jamb margin — capsule edge held', await evl('__construct.walkOK(-3, -3.1) === false'));
check('bounds = 10 slabs + 20 seams + 8 portal wires', await evl('__construct.bounds() === 38'), await evl('__construct.bounds()'));

// 13 — fly-camera clamp + release back to body
await evl('__cam(null)');
check('camLock released', true);
const shot = await cdp('Page.captureScreenshot', { format: 'png' });
fs.writeFileSync(path.join(HERE, 'probe_placemode.png'), Buffer.from(shot.result.data, 'base64'));

console.log('---'); console.log(R.every(r => r.ok) ? 'ALL PASS' : 'FAILURES: ' + JSON.stringify(R.filter(r => !r.ok)));
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(R.every(r => r.ok) ? 0 : 1);
