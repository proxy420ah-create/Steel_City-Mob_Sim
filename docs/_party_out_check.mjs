// _party_out_check.mjs — DVM-2269: commission → construct party handoff.
// Both seat bodies on FOLLOW → portal enter() → construct page loads →
// dvm_party carries both chars → both emit as construct followers.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp({ page: 'thecommission', settle: 2500 });

check('both bodies mounted', await evl(`!!__chairbody && !!__designerbody`));
await evl(`__chairbody.setOrder('follow'); __designerbody.setOrder('follow')`);
await sleep(400);
check('both on follow', await evl(`__chairbody.order()==='follow' && __designerbody.order()==='follow'`));

// step through the portal — the party write happens inside enter()
await evl(`window.__portal.enter()`);
for (let i = 0; i < 120 && !(await evl('!!window.__construct')); i++) await sleep(500);
await sleep(2500);                                                       // party emit + rig fetches
check('arrived in the Construct', await evl(`location.pathname.endsWith('construct.html')`));
check('CHAIR1 heeled in',    await evl(`followers.some(f=>f.spec.char==='CHAIR1' && f.order==='follow')`));
check('DESIGNER1 heeled in', await evl(`followers.some(f=>f.spec.char==='DESIGNER1' && f.order==='follow')`));

finish();
