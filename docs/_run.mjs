// _run.mjs — probe suite runner: one deterministic call, no babysitting.
//   node _run.mjs _place_probe          → runs, writes _logs/_place_probe.log,
//                                         prints a one-line verdict + FAILURES.
//   node _run.mjs _place_probe --tail   → also prints the last 25 lines.
// Exits 0 on ALL PASS, 1 on any FAIL / timeout / crash. Never hangs: hard cap
// below, and the child's Chrome tree is killed with it.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const name = (process.argv[2] || '').replace(/\.mjs$/, '');
if (!name) { console.error('usage: node _run.mjs <probe> [--tail]'); process.exit(2); }
const script = path.join(HERE, name + '.mjs');
const LOGDIR = path.join(HERE, '_logs'); fs.mkdirSync(LOGDIR, { recursive: true });
const log = path.join(LOGDIR, name + '.log');
const out = fs.createWriteStream(log);
const HARD_CAP = 15 * 60 * 1000;

const child = spawn(process.execPath, [script], { stdio: ['ignore', 'pipe', 'pipe'], cwd: HERE });
let killed = false;
const cap = setTimeout(() => { killed = true; try { child.kill('SIGKILL'); } catch {} }, HARD_CAP);
child.stdout.pipe(out); child.stderr.pipe(out);
child.on('exit', code => {
  clearTimeout(cap); out.end();
  setTimeout(() => {                                                    // flush
    const txt = fs.existsSync(log) ? fs.readFileSync(log, 'utf8') : '';
    const pass = (txt.match(/^PASS\b/gm) || []).length + (txt.match(/^ ok\b/gm) || []).length;
    const fails = [...txt.matchAll(/^FAIL\s+(.*)/gm)].map(m => m[1].trim());
    const failuresJson = (txt.match(/^FAILURES: (\[.*)/m) || [])[1];
    if (process.argv.includes('--tail')) console.log(txt.trim().split('\n').slice(-25).join('\n'));
    if (killed) { console.log(`VERDICT ${name}: TIMEOUT after ${HARD_CAP / 60000}min (log: docs/_logs/${name}.log)`); process.exit(1); }
    if (fails.length === 0 && pass > 0) {
      console.log(`VERDICT ${name}: ALL PASS (${pass} checks, log: docs/_logs/${name}.log)`); process.exit(0);
    }
    console.log(`VERDICT ${name}: ${pass} pass / ${fails.length} FAIL (exit ${code ?? 'sig'}, log: docs/_logs/${name}.log)`);
    for (const f of fails) console.log('  FAIL ' + f);
    if (failuresJson) console.log('  ' + failuresJson.slice(0, 2000));
    process.exit(1);
  }, 300);
});
