// _zoom_check.mjs — diagnose the hold-Z scope: does the served page carry the
// zoom code, does keys['z'] register on keydown, does camera.fov respond?
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp();

// the server must serve the zoom block in both files
check('14_loop.js serves the zoomT block',
  await evl(`fetch('construct/14_loop.js').then(r => r.text()).then(t => t.includes('zoomT'))`));
check('11_input.js declares zoomT',
  await evl(`fetch('construct/11_input.js').then(r => r.text()).then(t => t.includes('zoomT'))`));

// page-side lexicals reachable + initial state sane
check('zoomT + keys + camera in scope',
  await evl(`typeof zoomT === 'number' && typeof keys === 'object' && camera.fov === 58`));

// synthetic keydown → keys['z'] must set (proves the listener is alive)
await evl(`window.dispatchEvent(new KeyboardEvent('keydown', { key: 'z' }))`);
check('keydown z registers in keys', await evl(`keys['z'] === true`));
await sleep(400);
const fovHeld = await evl(`camera.fov`);
check('fov narrows while Z held', fovHeld < 40, 'fov=' + fovHeld);
await evl(`window.dispatchEvent(new KeyboardEvent('keyup', { key: 'z' }))`);
await sleep(500);
const fovRel = await evl(`camera.fov`);
check('fov restores on release', fovRel > 57, 'fov=' + fovRel);

// pointer-lock spike guard — bogus multi-thousand-px deltas must not move the
// view. pointerLockElement is read-only; stub it so the look path engages.
await evl(`Object.defineProperty(document, 'pointerLockElement', { get: () => renderer.domElement, configurable: true })`);
const yaw0 = await evl(`yaw`);
await evl(`window.dispatchEvent(new MouseEvent('mousemove', { movementX: 4000, movementY: -3000 }))`);
check('movementX/Y spike is dropped', (await evl(`Math.abs(yaw - ${yaw0}) < 1e-6`)));
await evl(`window.dispatchEvent(new MouseEvent('mousemove', { movementX: 40, movementY: 0 }))`);
check('normal deltas still move the view', (await evl(`Math.abs(yaw - ${yaw0}) > 1e-6`)));
await evl(`delete document.pointerLockElement`);

// modifier gates — Ctrl+Z must NOT zoom (place-undo)
await evl(`window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Control' }))`);
await evl(`window.dispatchEvent(new KeyboardEvent('keydown', { key: 'z' }))`);
await sleep(350);
check('Ctrl+Z does not zoom', (await evl(`camera.fov`)) === 58);
await evl(`window.dispatchEvent(new KeyboardEvent('keyup', { key: 'z' }))`);
await evl(`window.dispatchEvent(new KeyboardEvent('keyup', { key: 'Control' }))`);

// options overlay — O toggles it, a row click arms capture, the next keypress rebinds
// zoomKey + persists; the capture-phase keydown must swallow everything while open.
// Menu-open dispatches go to document.body (real key target) so propagation applies —
// dispatching on window lands at-target where capture order can't gate the game.
check('18_options.js serves',
  await evl(`fetch('construct/18_options.js').then(r => r.text()).then(t => t.includes('__optToggle'))`));
check('zoomKey declared, defaults to z', await evl(`typeof zoomKey === 'string' && zoomKey === 'z'`));
check('options toggle exposed', await evl(`typeof window.__optToggle === 'function'`));
await evl(`window.__optToggle(true)`);
check('options opens', await evl(`optOpen === true`));
await evl(`document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'x', bubbles: true }))`);
check('keys swallowed while open', await evl(`keys['x'] !== true && !placeOn`));
await evl(`optCap = OPT[0]`);                                          // = clicking the ZOOM row
await evl(`document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'q', code: 'KeyQ', bubbles: true }))`);
check('rebind takes the next keypress', await evl(`zoomKey === 'q'`));
check('HUD chip tracks the bind', await evl(`document.getElementById('hkZoom').textContent === 'Q'`));
check('bind persists to localStorage', await evl(`localStorage.getItem('dvm_key_zoom') === 'q'`));
await evl(`optCap = OPT[0]`);
await evl(`document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))`);
check('Esc cancels a capture, never binds', await evl(`zoomKey === 'q' && optCap === null`));
await evl(`window.__optToggle(false)`);
await evl(`window.dispatchEvent(new KeyboardEvent('keydown', { key: 'q' }))`);
await sleep(400);
check('scope answers the rebound key', (await evl(`camera.fov`)) < 40, 'fov=' + await evl(`camera.fov`));
await evl(`window.dispatchEvent(new KeyboardEvent('keyup', { key: 'q' }))`);
await sleep(400);
await evl(`zoomKey = 'z'; localStorage.setItem('dvm_key_zoom', 'z')`);   // restore the default for the next run

// zoom power — the slider sets the scoped-FOV floor (58→zoomFov) + persists;
// a mid-scope move must re-pull the lens without a release/press cycle
check('zoomFov declared, defaults to 26', await evl(`typeof zoomFov === 'number' && zoomFov === 26`));
check('power slider mounted', await evl(`!!document.querySelector('#optSlds input[type=range]')`));
await evl(`const _si = document.querySelector('#optSlds input'); _si.value = 13; _si.dispatchEvent(new Event('input'))`);
check('slider drives zoomFov + persists', await evl(`zoomFov === 13 && localStorage.getItem('dvm_zoom_fov') === '13'`));
await evl(`window.dispatchEvent(new KeyboardEvent('keydown', { key: 'z' }))`);
await sleep(500);
check('scope pulls to the tuned floor', (await evl(`camera.fov`)) < 20, 'fov=' + await evl(`camera.fov`));
await evl(`const _sj = document.querySelector('#optSlds input'); _sj.value = 40; _sj.dispatchEvent(new Event('input'))`);
await sleep(400);
check('slider re-pulls mid-scope', (await evl(`camera.fov`)) > 30 && (await evl(`camera.fov`)) < 41,
  'fov=' + await evl(`camera.fov`));
await evl(`window.dispatchEvent(new KeyboardEvent('keyup', { key: 'z' }))`);
await sleep(400);
await evl(`zoomFov = 26; localStorage.setItem('dvm_zoom_fov', '26')`);   // restore the default for the next run

finish();
