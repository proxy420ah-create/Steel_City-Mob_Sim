import json
with open(r'c:\Users\NADECC\ATSTradingDashboard Project\Cursor Workshop\SteelCityMobSim\Assets\MacroMap\MacroMap.json') as f:
    data = json.load(f)

# Build compact tile map: single string where each char is a tile type
chars = {'block': '.', 'mainst': 'M', 'river': '~', 'bridge': 'B', 'oob': '#'}
rows = []
for y in range(data['height']):
    row = ''
    for x in range(data['width']):
        t = data['tiles']
        # tiles is a flat list, find tile at x,y
    # Actually tiles is a list of {x,y,t} objects
tile_map = {}
for t in data['tiles']:
    tile_map[(t['x'], t['y'])] = t['t']

rows = []
for y in range(data['height']):
    row = ''
    for x in range(data['width']):
        row += chars.get(tile_map.get((x, y), 'oob'), '#')
    rows.append(row)

compact = '|'.join(rows)
print(f"Compact length: {len(compact)}")
print(f"First 200 chars: {compact[:200]}")

# Write as JS variable
js = f"const EMBEDDED_MACRO_MAP = {{ width: {data['width']}, height: {data['height']}, type: 'replica', compact: '{compact}' }};"
with open(r'c:\Users\NADECC\ATSTradingDashboard Project\Cursor Workshop\SteelCityMobSim\Assets\MacroMap\embedded_map.txt', 'w') as f:
    f.write(js)
print(f"\nJS variable written, length: {len(js)}")
