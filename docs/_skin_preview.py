import numpy as np
from PIL import Image, ImageDraw
import building_skin as S
SC = 5
def rgb(a):
    h = np.zeros(a.shape + (3,), np.uint8)
    cache = {n: tuple(int(S.PAL[n][i:i+2], 16) for i in (1, 3, 5)) for n in set(a.ravel().tolist())}
    for n, c in cache.items(): h[a == n] = c
    return h
def img(a, flip=False):
    h = rgb(a[::-1] if flip else a)
    return Image.fromarray(h).resize((h.shape[1]*SC, h.shape[0]*SC), Image.NEAREST)
W = 120
for k, st in S.STYLES.items():
    wall = img(st['wall'](62, W), flip=True)          # row 0 = floor -> flip so the floor is at the bottom
    floor = img(st['floor'](W, 80))
    hall = img(st['hall'](W, 40))
    pad, title_h = 8, 26
    H = title_h + wall.height + floor.height + hall.height + pad*4
    sheet = Image.new('RGB', (W*SC + 2*pad, H), (18, 20, 28))
    d = ImageDraw.Draw(sheet); d.text((pad, 6), st['title'], fill=(230, 220, 190))
    y = title_h
    for lbl, im in (('WALL  (floor to ceiling, 3.6 m run)', wall), ('OFFICE FLOOR  (3.6 x 2.4 m)', floor), ('HALL FLOOR', hall)):
        sheet.paste(im, (pad, y)); d.text((pad+4, y+3), lbl, fill=(255, 255, 255)); y += im.height + pad
    sheet.save(f'design_previews/{k}.png')
    print(k, sheet.size, 'palette entries used:', len({n for f in (st['wall'](62, W), st['floor'](W, 80), st['hall'](W, 40)) for n in f.ravel().tolist()}))
open('design_previews/index.html', 'w', encoding='utf-8').write("""<!doctype html><meta charset=utf-8><title>Commission skins</title>
<body style="background:#10131a;color:#ddd;font:14px Consolas,monospace;margin:16px">
<h2>Commission office skins - pick a direction</h2>
<p>Top row = raw palette. Bottom row = approximate in-scene look (dim warm light).</p>
<div style="display:flex;gap:14px;flex-wrap:wrap">
""" + "".join(f'<div><img src="{k}.png" style="display:block;image-rendering:pixelated"><img src="{k}.png" style="display:block;margin-top:8px;image-rendering:pixelated;filter:brightness(.62) sepia(.25) saturate(1.1)"></div>' for k in S.STYLES) + "</div>")
