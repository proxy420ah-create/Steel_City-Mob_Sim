const { spawn } = require('child_process'), fs = require('fs'), os = require('os'), path = require('path');
const EDGE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', PORT = 9366, OUT = path.join(os.tmpdir(), 'dvm2253');
fs.mkdirSync(OUT, { recursive: true });
const prof = fs.mkdtempSync(path.join(os.tmpdir(), 'edge-prof-'));
const edge = spawn(EDGE, ['--headless=new', '--remote-debugging-port=' + PORT, '--user-data-dir=' + prof, '--window-size=1280,800',
  '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  let tabs;
  for (let i = 0; i < 30; i++) { try { tabs = await (await fetch('http://127.0.0.1:' + PORT + '/json')).json(); if (tabs.length) break; } catch (e) {} await sleep(400); }
  const ws = new WebSocket(tabs.find(t => t.type === 'page').webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener('open', r));
  let id = 0; const pend = {};
  ws.addEventListener('message', m => { const d = JSON.parse(m.data); if (d.id && pend[d.id]) { pend[d.id](d); delete pend[d.id]; } });
  const send = (m, p = {}) => new Promise(r => { const i = ++id; pend[i] = r; ws.send(JSON.stringify({ id: i, method: m, params: p })); });
  const ev = async e => (await send('Runtime.evaluate', { expression: e, returnByValue: true })).result.result.value;
  await send('Page.enable'); await send('Runtime.enable');
  await send('Page.navigate', { url: 'http://127.0.0.1:8723/DELIBERATION_MACHINE.html' });
  for (let i = 0; i < 50; i++) { await sleep(500); try { if (await ev('!!(window.__scene && window.__vinny)')) break; } catch (e) {} }
  await sleep(2500);
  // guest view — envelope should be hidden
  await sleep(800);
  const r0 = await send('Page.captureScreenshot', { format: 'png' }); fs.writeFileSync(path.join(OUT, 'env_outside.png'), Buffer.from(r0.result.data, 'base64'));
  const g = await ev('(() => { const s = window.__scene; const eg = s.children.find(c => c.type==="Group" && c.children.length===2 && c.visible===false); return {envChildren: eg ? eg.children.length : -1}; })()');
  console.log('outside: envGroup children(hidden)=', JSON.stringify(g));
  // possess view — camera inside the box
  await ev('window.__vinny.cam.grab()'); await sleep(1200);
  const st = await ev('window.__vinny.cam.info()');
  console.log('possess:', JSON.stringify({ possessed: st.possessed, boomCur: +st.boomCur.toFixed(2) }));
  const r1 = await send('Page.captureScreenshot', { format: 'png' }); fs.writeFileSync(path.join(OUT, 'env_inside.png'), Buffer.from(r1.result.data, 'base64'));
  // look toward the front wall (turn ~180 from spawn facing) so the front brick is in frame
  await ev(`window.dispatchEvent(new MouseEvent('mousemove', { movementX: ${Math.round(Math.PI / (.18 * Math.PI / 180))}, movementY: -400, buttons: 1 }))`);
  await sleep(800);
  const r2 = await send('Page.captureScreenshot', { format: 'png' }); fs.writeFileSync(path.join(OUT, 'env_front.png'), Buffer.from(r2.result.data, 'base64'));
  await ev('window.__vinny.cam.release()'); await sleep(600);
  const r3 = await send('Page.captureScreenshot', { format: 'png' }); fs.writeFileSync(path.join(OUT, 'env_after.png'), Buffer.from(r3.result.data, 'base64'));
  ws.close(); edge.kill(); await sleep(300); try { fs.rmSync(prof, { recursive: true, force: true }); } catch (e) {}
  console.log('done'); process.exit(0);
})().catch(e => { console.error('ERR', String(e).slice(0, 200)); try { edge.kill(); } catch (_) {} process.exit(1); });
