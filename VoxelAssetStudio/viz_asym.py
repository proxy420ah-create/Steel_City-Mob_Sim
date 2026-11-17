import json

P = r"JSON Models In Progress/vehicle_490_touring.json"
d = json.load(open(P))
W, H, D = d["dims"]
vox = {(x, y, z): m for x, y, z, m in d["voxels"]}
mir = lambda x: 70 - x

def colmap(z0, z1, y0, y1, title):
    """For each x column: which y's have material in z band. L/R compare."""
    print(f"\n=== {title}  (z {z0}..{z1}, y {y0}..{y1}) ===")
    for x in range(0, W):
        ys = [y for y in range(y0, y1 + 1) for z in range(z0, z1 + 1) if (x, y, z) in vox]
        if ys:
            print(f"  x={x:2d}  y {min(ys):2d}..{max(ys):2d}  n={len(ys):3d}  mir-x={mir(x):2d}")

# wheels: four bands where wheel material lives (y 0..28)
colmap(20, 48, 0, 28, "FRONT wheel band")
colmap(118, 146, 0, 28, "REAR wheel band")

# top-down slice of the rear quarter asymmetry at a mid y
print("\n=== XZ occupancy, y=28 (rear quarter z=95..154) ===  L=left edge -> R")
for z in range(95, 155, 2):
    row = ""
    for x in range(0, W):
        a = (x, 28, z) in vox or (x, 28, z + 1) in vox
        b = (mir(x), 28, z) in vox or (mir(x), 28, z + 1) in vox
        row += "." if not a else ("#" if b else "X")   # X = asymmetric cell
    if "X" in row or "#" in row:
        print(f" z={z:3d} |{row}|")

print("\n=== XZ occupancy, y=55 (cabin z=40..105) ===")
for z in range(40, 106, 2):
    row = ""
    for x in range(0, W):
        a = (x, 55, z) in vox or (x, 55, z + 1) in vox
        b = (mir(x), 55, z) in vox or (mir(x), 55, z + 1) in vox
        row += "." if not a else ("#" if b else "X")
    if "X" in row or "#" in row:
        print(f" z={z:3d} |{row}|")
