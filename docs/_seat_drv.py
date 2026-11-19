# _seat_drv.py — DESIGNER's live drive tool. Commands ride the /cmd relay into the
# boss's live construct session (page must have __live on — /peek carries state back).
#   python _seat_drv.py peek                 — dump live state (pos, followers, placed)
#   python _seat_drv.py go <x> <z>           — tagAt + goto order (possesses bodies)
#   python _seat_drv.py gaze <x> <y> <z>     — head-aim only, never moves feet
#   python _seat_drv.py order <wait|goto|follow|post>
#   python _seat_drv.py zoom <0..1>            — POV hold-scope level (0 = wide 72deg, 1 = 26deg)
#   python _seat_drv.py torch <0|1>            — my own POV lamp for lit shots (never his L-rig)
#   python _seat_drv.py waitpos <x> <z> [r]  — poll /peek till a body is within r (def .35)
#   python _seat_drv.py pov [out.png]        — pull latest probe_live_pov.png -> file
import json, sys, time, shutil, urllib.request, os

BASE = 'http://127.0.0.1:8723'
HERE = os.path.dirname(os.path.abspath(__file__))
_seq = [0]

def _get(u):
    return json.loads(urllib.request.urlopen(u, timeout=5).read().decode('utf-8'))

def _post(u, obj):
    r = urllib.request.urlopen(urllib.request.Request(u, data=json.dumps(obj).encode(),
        headers={'Content-Type': 'application/json'}), timeout=5)
    return json.loads(r.read().decode('utf-8'))

def peek():
    d = _get(BASE + '/peek')
    if 'err' in d: return d
    d['_age'] = round((time.time() * 1000 - d.get('t', 0)) / 1000, 1)
    return d

def cmd(agent, args):
    d = peek()
    if 'err' in d: return d
    seq = (d.get('cmdAck') or 0) + 1
    return _post(BASE + '/cmd', {'seq': seq, 'agent': agent, 'args': args})

def my_pos(d):
    fl = d.get('followers') or []
    return fl[0]['pos'] if fl else None

if __name__ == '__main__':
    a = sys.argv[1:]
    if not a or a[0] == 'peek':
        d = peek()
        print(json.dumps({k: d.get(k) for k in ('_age', 'pos', 'yaw', 'povOn', 'placeOn',
              'held', 'aimOn', 'followers', 'cmdAck')}, indent=1))
    elif a[0] == 'go':
        print(json.dumps(cmd('go', [float(a[1]), float(a[2])])))
    elif a[0] == 'gaze':
        print(json.dumps(cmd('gaze', [float(a[1]), float(a[2]), float(a[3])])))
    elif a[0] == 'order':
        print(json.dumps(cmd('order', [a[1]])))
    elif a[0] == 'zoom':
        print(json.dumps(cmd('zoom', [float(a[1])])))
    elif a[0] == 'torch':
        print(json.dumps(cmd('torch', [float(a[1])])))
    elif a[0] == 'waitpos':
        x, z, r = float(a[1]), float(a[2]), float(a[3]) if len(a) > 3 else .35
        t0 = time.time()
        while time.time() - t0 < 20:
            d = peek(); p = my_pos(d)
            if 'err' in d or not p:
                print('no live/follower:', d); break
            dd = ((p[0] - x) ** 2 + (p[2] - z) ** 2) ** .5
            if dd <= r:
                print(json.dumps({'arrived': True, 'pos': p, 'dist': round(dd, 3),
                                  'order': d['followers'][0].get('order'),
                                  'possessed': d['followers'][0].get('possessed')})); break
            time.sleep(.4)
        else:
            print(json.dumps({'arrived': False, 'pos': p}))
    elif a[0] == 'pov':
        out = a[1] if len(a) > 1 else 'probe_seat_pov.png'
        src = os.path.join(HERE, 'probe_live_pov.png')
        dst = os.path.join(HERE, out)
        shutil.copyfile(src, dst)
        print(json.dumps({'wrote': out, 'src_age_s': round(time.time() - os.path.getmtime(src), 1)}))
    else:
        print(__doc__)
