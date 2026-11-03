"""commission_inbox.py — DVM-924 rung 3: the inbox-listener daemon.

A persistent process that holds the /pulse/<SEAT> socket CONTINUOUSLY and
drops every delivered batch into a per-seat inbox file — decoupling delivery
from how fast the host wakes. Replaces the spawn-per-wake relay cycle:

    relay:  spawn subagent → hold socket → return → report → re-spawn (per wake)
    daemon: hold socket forever → append batch to inbox_<SEAT>.jsonl → hold again

The host drains the inbox on its own cadence (turn boundaries, natural yields)
instead of paying a fresh subagent every cycle.

Usage:  python commission_inbox.py <SEAT>          # holds forever
        python commission_inbox.py <SEAT> --once   # one batch, for drills

Inbox format (append-only JSONL, one line per delivered batch):
    {"at": <ms>, "batch": "<verbatim pulse stdout>"}
Host-side watermark: inbox_n_<SEAT>.js — the count of lines already drained.
"""
import json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
PULSE = os.path.join(HERE, 'commission_pulse.py')
INBOX = 'inbox_%s.jsonl'
MARK = 'inbox_n_%s.js'
BACKOFF = [1, 2, 5, 15, 30]   # retry ladder on socket errors; holds otherwise


def mark_n(seat):
    p = os.path.join(HERE, MARK % seat)
    try:
        m = json.loads(open(p, encoding='utf-8').read().split('=', 1)[1].strip().rstrip(';'))
        return m.get('n', 0)
    except Exception:
        return 0


def heartbeat(seat, n):
    p = os.path.join(HERE, MARK % seat)
    tmp = p + '.tmp'
    open(tmp, 'w', encoding='utf-8').write('window.DVM_INBOX_%s = %s;\n' % (seat, json.dumps({'n': n, 'at': int(time.time() * 1000)})))
    try:
        os.replace(tmp, p)
    except OSError:
        pass


def hold(seat):
    """One socket-hold → returns the delivered batch text, or raises."""
    return subprocess.run([sys.executable, '-X', 'utf8', PULSE, seat],
                          cwd=HERE, capture_output=True, text=True,
                          encoding='utf-8', errors='replace', timeout=7200,
                          creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))   # daemon is windowless — without this each relay spawn pops a console


def append_inbox(seat, text):
    p = os.path.join(HERE, INBOX % seat)
    with open(p, 'a', encoding='utf-8') as f:
        f.write(json.dumps({'at': int(time.time() * 1000), 'batch': text}, ensure_ascii=False) + '\n')
    # NOTE: busy_ is host sole-writer — the daemon does NOT stamp WAKING here;
    # WAKING derives render-side from the inbox_n_<SEAT>.js heartbeat instead.


def main():
    seat = sys.argv[1].upper()
    once = '--once' in sys.argv
    print('inbox daemon up — %s, holding continuous' % seat, flush=True)
    n = mark_n(seat)
    tries = 0
    while True:
        try:
            r = hold(seat)
            tries = 0
            body = (r.stdout or '') + (r.stderr or '')
            append_inbox(seat, body)
            n += 1
            heartbeat(seat, n)
            print('batch #%d appended (%d chars, exit %d)' % (n, len(body), r.returncode), flush=True)
        except Exception as e:
            tries += 1
            wait = BACKOFF[min(tries - 1, len(BACKOFF) - 1)]
            print('hold failed (%s) — retry in %ds' % (e, wait), flush=True)
            time.sleep(wait)
            continue
        if once:
            break


if __name__ == '__main__':
    main()
