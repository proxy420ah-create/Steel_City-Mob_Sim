"""_plan.py — render bake occupancy slices to labeled PNGs for agent inspection.

Usage:
  python _plan.py plan [--z0 0] [--z1 600] [--ylo 0] [--yhi 6] [--out plan.png] [--mark wx,wz[,label]]
      top-down: any voxel in y-band counts as solid
  python _plan.py section {z|x} N [--yhi 50] [--out sec.png]
      vertical cut at file z or x = N

World transform (verified vs door reveal + coffer):  wx = fx*.03-9.555, wz = fz*.03-2.34, wy = fy*.03-.09
"""
import json, base64, os, argparse
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

D = json.load(open(os.path.join(os.path.dirname(__file__), '../Assets/StreamingAssets/voxel_items/office_complex.bake.json')))
W, H, DEP = D['occ']['w'], D['occ']['h'], D['occ']['d']
raw = base64.b64decode(D['occ']['b64'])
G = np.unpackbits(np.frombuffer(raw, dtype=np.uint8))[:W * H * DEP].reshape(W, H, DEP).transpose(2, 1, 0)  # runtime layout is index=(x*H+y)*D+z, MSB-first; -> [z,y,x]

wx = lambda fx: fx * .03 - 9.57
wz = lambda fz: fz * .03 - 2.34
wy = lambda fy: fy * .03 - .09
fx = lambda w: int(round((w + 9.57) / .03))
fz = lambda w: int(round((w + 2.34) / .03))

p = argparse.ArgumentParser()
sub = p.add_subparsers(dest='mode', required=True)
pp = sub.add_parser('plan')
pp.add_argument('--z0', type=int, default=0); pp.add_argument('--z1', type=int, default=DEP)
pp.add_argument('--x0', type=int, default=0); pp.add_argument('--x1', type=int, default=W)
pp.add_argument('--ylo', type=int, default=0); pp.add_argument('--yhi', type=int, default=6)
pp.add_argument('--out', default='_plan.png')
pp.add_argument('--mark', action='append', default=[], help='wx,wz[,label] — drop a marker')
ps = sub.add_parser('section')
ps.add_argument('axis', choices=['x', 'z']); ps.add_argument('n', type=int)
ps.add_argument('--yhi', type=int, default=50); ps.add_argument('--out', default=None)
a = p.parse_args()

def axes_world(ax, fx0, fx1, fz0, fz1):
    xt = [f for f in range(fx0 - fx0 % 20, fx1, 20)]
    ax.set_xticks([f - fx0 for f in xt]); ax.set_xticklabels([f'{wx(f):.1f}' for f in xt], fontsize=7)
    yt = [z for z in range(fz0 - fz0 % 20, fz1, 20)]
    ax.set_yticks([z - fz0 for z in yt]); ax.set_yticklabels([f'{wz(z):.1f}' for z in yt], fontsize=7)
    ax.set_xlabel('world x (m)'); ax.set_ylabel('world z (m)')

if a.mode == 'plan':
    img = G[a.z0:a.z1, a.ylo:a.yhi, a.x0:a.x1].any(axis=1)   # [z,x]
    fig, ax = plt.subplots(figsize=(14, 14 * img.shape[0] / img.shape[1]))
    ax.imshow(img, cmap='gray_r', origin='lower', interpolation='nearest')
    axes_world(ax, a.x0, a.x1, a.z0, a.z1)
    for m in a.mark:
        parts = m.split(','); mx, mz, lbl = float(parts[0]), float(parts[1]), parts[2] if len(parts) > 2 else ''
        ax.plot(fx(mx) - a.x0, fz(mz) - a.z0, 'r+', ms=14, mew=2)
        if lbl: ax.annotate(lbl, (fx(mx) - a.x0, fz(mz) - a.z0), color='red', fontsize=8, xytext=(4, 4), textcoords='offset points')
    ax.set_title(f'plan y-file {a.ylo}..{a.yhi} ({wy(a.ylo):.2f}..{wy(a.yhi):.2f} m)   . = open')
    fig.savefig(a.out, dpi=110, bbox_inches='tight')
    print('wrote', a.out)

elif a.mode == 'section':
    if a.axis == 'z':
        img = G[a.n, :a.yhi, :]
        lo_lab, conv, where = 'world x (m)', wx, wz(a.n)
    else:
        img = G[:, :a.yhi, a.n]
        lo_lab, conv, where = 'world z (m)', wz, wx(a.n)
    fig, ax = plt.subplots(figsize=(16, 6))
    ax.imshow(img, cmap='gray_r', origin='lower', interpolation='nearest')
    xt = list(range(0, img.shape[1], 20))
    ax.set_xticks(xt); ax.set_xticklabels([f'{conv(f):.1f}' for f in xt], fontsize=7)
    yt = list(range(0, a.yhi, 10))
    ax.set_yticks(yt); ax.set_yticklabels([f'{wy(y):.2f}' for y in yt], fontsize=7)
    ax.set_xlabel(lo_lab); ax.set_ylabel('world y (m)')
    ax.set_title(f'section file {a.axis}={a.n}  ({where:.2f} m)')
    out = a.out or f'_sec_{a.axis}{a.n}.png'
    fig.savefig(out, dpi=110, bbox_inches='tight')
    print('wrote', out)
