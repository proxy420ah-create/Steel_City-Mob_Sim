"""
Scale the 10x10 test-rig city up to an NxN grid (default 32x32) -- a direct
scale-up of the existing convention, no new content.

Source of truth (schema + conventions): the 10x10 backups in StreamingAssets
    city_layout_10x10_backup.json    (visual: .stasset placement per block)
    city_template_10x10_backup.json  (game logic: blocks/businesses/HQ flags)

Convention carried over from the 10x10:
  * every block = 9 building slots, all empty_land
  * special blocks (single building in slot 0) keep their relative map
    position: player HQ (tenement), rival HQ (tenement), shooting range
  * block naming bands: N / S / W / E Quarter + Central Block (proportional)

Output: city_layout_<N>.json + city_template_<N>.json (the existing tier
naming convention). With --activate they are also copied to the ACTIVE names
(city_layout.json / city_template.json) -- both files must change together
(see RECENT_CHANGES.md "City Scale Testing" reminder).

Usage:
    python Tools/generate_city_layout.py                # 32x32, write tier files
    python Tools/generate_city_layout.py --activate     # ...and make it active
    python Tools/generate_city_layout.py --size 20 --activate
    python Tools/generate_city_layout.py --restore      # re-activate the 10x10

Terrain modes (M2 — river/bridge/mainstreet/oob recipes):
    --river-row R --bridge-cols 8,20     # straight E-W river test layout
    --replica                            # stamp terrain from REPLICA1_DATA in
                                         # city_editor.html (water/bridge/mainst/oob)
Both emit blocks[].terrain + hSeamRows/vSeamRows (Unity-consumable seam rows)
and write *_river/_replica tier files — never the active pair without --activate.
"""
import argparse
import json
import os
import shutil

SA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "StreamingAssets")
SRC_LAYOUT = os.path.join(SA, "city_layout_10x10_backup.json")
SRC_TEMPLATE = os.path.join(SA, "city_template_10x10_backup.json")
SRC_N = 10


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def save(path, obj):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, indent=2)
        f.write("\n")


def scale_pos(p, n):
    """Map a 0..9 source index onto 0..n-1, keeping edges on edges."""
    return round(p * (n - 1) / (SRC_N - 1))


def block_name(row, col, n, num):
    """N/S/W/E Quarter + Central Block, in the same proportional bands as the 10x10."""
    lo, hi = round(0.3 * n), round(0.7 * n)
    if row < lo:
        d = "N Quarter"
    elif row >= hi:
        d = "S Quarter"
    elif col < lo:
        d = "W Quarter"
    elif col >= hi:
        d = "E Quarter"
    else:
        d = "Central Block"
    return f"{d} {num}"


def generate(n):
    src_l, src_t = load(SRC_LAYOUT), load(SRC_TEMPLATE)

    # Reference blocks from the 10x10: an ordinary one + the special ones.
    plain_l = next(b for b in src_l["blocks"] if len(b["buildings"]) == 9)
    plain_t = next(b for b in src_t["blocks"] if not (b["player_hq"] or b["rival_hq"] or b["police_station"]) and b["businesses"][0]["count"] == 9)
    special = {}  # (row, col) in n-grid -> (layout block, template block) from the 10x10
    t_by_id = {b["id"]: b for b in src_t["blocks"]}
    for lb in src_l["blocks"]:
        if len(lb["buildings"]) == 1 or lb["buildings"][0]["type"] != "empty_land":
            key = (scale_pos(lb["row"], n), scale_pos(lb["col"], n))
            assert key not in special, f"special blocks collide at {key} - grid too small"
            special[key] = (lb, t_by_id[lb["block_id"]])

    layout_blocks, template_blocks = [], []
    for r in range(n):
        for c in range(n):
            num = r * n + c + 1
            bid, bname = f"block_{num}", block_name(r, c, n, num)
            src = special.get((r, c))
            lb, tb = (src if src else (plain_l, plain_t))
            layout_blocks.append({
                "block_id": bid, "block_name": bname, "row": r, "col": c,
                "buildings": [dict(b) for b in lb["buildings"]],
            })
            tb2 = json.loads(json.dumps(tb))
            tb2.update({"id": bid, "name": bname, "row": r, "col": c})
            template_blocks.append(tb2)

    layout = {k: v for k, v in src_l.items() if k != "blocks"}
    layout["_comment"] = (f"Steel City layout: {n}x{n} = {n*n} blocks. Direct scale-up of the "
                          f"10x10 convention via Tools/generate_city_layout.py (source: *_10x10_backup.json).")
    layout["blocks"] = layout_blocks

    template = {k: v for k, v in src_t.items() if k != "blocks"}
    template["_comment"] = (f"Steel City template: {n*n} blocks, scaled from the 10x10 via "
                            f"Tools/generate_city_layout.py.")
    if isinstance(template.get("grid"), dict):
        template["grid"] = {"rows": n, "cols": n}
    template["blocks"] = template_blocks
    return layout, template, special


CHANNEL = {"water", "bridge"}   # terrain types the channel flows through

def stamp_terrain(layout, template, terrain, n):
    """Apply a {(row,col): terrain} map: suppress buildings/businesses off-land."""
    t_by_rc = {(b["row"], b["col"]): b for b in template["blocks"]}
    counts = {}
    for lb in layout["blocks"]:
        t = terrain.get((lb["row"], lb["col"]), "land")
        if t != "land":
            lb["terrain"] = t
            lb["buildings"] = []                       # nothing stands in a river/boulevard/oob
            tb = t_by_rc.get((lb["row"], lb["col"]))
            if tb is not None:
                tb["businesses"] = []                  # no shops in the channel either
                tb["player_hq"] = tb["rival_hq"] = tb["police_station"] = False
        counts[t] = counts.get(t, 0) + 1
    return counts


def derive_seams(terrain, n):
    """Seam types from terrain adjacency — the 'river seams just like road seams'
    rule: the corridor between two channel cells is water; between two mainst
    cells it's boulevard; everything else is an ordinary road."""
    h = [["road"] * n for _ in range(n - 1)]           # [r][c] between (r,c)-(r+1,c)
    v = [["road"] * (n - 1) for _ in range(n)]         # [r][c] between (r,c)-(r,c+1)
    def T(r, c): return terrain.get((r, c), "land")
    for r in range(n - 1):
        for c in range(n):
            if T(r, c) in CHANNEL and T(r + 1, c) in CHANNEL:
                h[r][c] = "river"
            elif T(r, c) == "mainstreet" and T(r + 1, c) == "mainstreet":
                h[r][c] = "mainstreet"
    for r in range(n):
        for c in range(n - 1):
            if T(r, c) in CHANNEL and T(r, c + 1) in CHANNEL:
                v[r][c] = "river"
            elif T(r, c) == "mainstreet" and T(r, c + 1) == "mainstreet":
                v[r][c] = "mainstreet"
    return ({"cells": row} for row in h), ({"cells": row} for row in v)


def replica_terrain(html_path):
    """Pull the embedded REPLICA1_DATA tile map out of city_editor.html."""
    import re
    html = open(html_path, encoding="utf-8").read()
    m = re.search(r"window\.REPLICA1_DATA = (\{.*?\});", html, re.S)
    if not m:
        raise SystemExit("[city] REPLICA1_DATA not found in city_editor.html")
    data = json.loads(m.group(1))
    tmap = {"block": "land", "river": "water", "bridge": "bridge",
            "mainst": "mainstreet", "oob": "oob", "road": "mainstreet"}
    terrain = {}
    for t in data["tiles"]:
        terrain[(t["y"], t["x"])] = tmap.get(t["t"], "land")  # tiles are (x,y) → (row,col)
    return terrain, data["width"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=32)
    ap.add_argument("--activate", action="store_true", help="copy the generated pair to the active city_layout/city_template")
    ap.add_argument("--restore", action="store_true", help="re-activate the 10x10 backups and exit")
    ap.add_argument("--replica", action="store_true",
                    help="stamp terrain from the Gangsters replica tiles embedded in city_editor.html (writes *_replica files)")
    ap.add_argument("--river-row", type=int, default=None,
                    help="stamp a straight E-W river across this row (test layout; writes *_river files)")
    ap.add_argument("--bridge-cols", type=str, default="",
                    help="comma list of cols that become bridge cells on --river-row")
    a = ap.parse_args()

    if a.restore:
        shutil.copyfile(SRC_LAYOUT, os.path.join(SA, "city_layout.json"))
        shutil.copyfile(SRC_TEMPLATE, os.path.join(SA, "city_template.json"))
        print("[city] restored 10x10 as the active city")
        return

    n = a.size
    if a.replica:
        terrain, n = replica_terrain(os.path.join(os.path.dirname(SA), "..", "VoxelAssetStudio", "city_editor.html"))
    elif a.river_row is not None:
        bcols = {int(c) for c in a.bridge_cols.split(",") if c.strip()}
        terrain = {}
        for c in range(n):
            terrain[(a.river_row, c)] = "bridge" if c in bcols else "water"
    else:
        terrain = None

    layout, template, special = generate(n)
    suffix = ""
    if terrain is not None:
        counts = stamp_terrain(layout, template, terrain, n)
        h_rows, v_rows = derive_seams(terrain, n)
        layout["hSeamRows"] = list(h_rows)
        layout["vSeamRows"] = list(v_rows)
        suffix = "_replica" if a.replica else "_river"
        print(f"[city] terrain census: {counts}")

    lp = os.path.join(SA, f"city_layout_{n}{suffix}.json")
    tp = os.path.join(SA, f"city_template_{n}{suffix}.json")
    save(lp, layout)
    save(tp, template)
    slots = sum(len(b["buildings"]) for b in layout["blocks"])
    print(f"[city] {n}x{n}: {len(layout['blocks'])} blocks, {slots} building slots")
    for (r, c), (lb, _) in sorted(special.items()):
        print(f"[city]   special block ({r},{c}) <- 10x10 {lb['block_id']}: {[b['type'] for b in lb['buildings']]}")
    print(f"[city] wrote {os.path.basename(lp)} + {os.path.basename(tp)}")
    if a.activate:
        shutil.copyfile(lp, os.path.join(SA, "city_layout.json"))
        shutil.copyfile(tp, os.path.join(SA, "city_template.json"))
        print("[city] ACTIVE city is now", f"{n}x{n}", "(both files copied)")


if __name__ == "__main__":
    main()
