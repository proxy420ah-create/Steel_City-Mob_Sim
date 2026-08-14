import json
with open(r'c:\Users\NADECC\ATSTradingDashboard Project\Cursor Workshop\SteelCityMobSim\Assets\MacroMap\MacroMap.json') as f:
    data = json.load(f)
tiles = {}
for t in data['tiles']:
    tiles[(t['x'], t['y'])] = t['t']
chars = {'block': '.', 'mainst': 'M', 'river': '~', 'bridge': 'B', 'oob': '#'}
for y in range(data['height']):
    line = ''
    for x in range(data['width']):
        line += chars.get(tiles.get((x, y), '?'), '?')
    print(line)
