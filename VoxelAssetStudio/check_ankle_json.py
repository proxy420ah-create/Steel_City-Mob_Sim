import json

with open('../My project/Assets/StreamingAssets/Actor.stasset', 'rb') as f:
    data = f.read()
    json_start = data.find(b'{')
    if json_start != -1:
        json_str = data[json_start:].decode('utf-8')
        obj = json.loads(json_str)
        
        print("=== ANKLE JOINTS ===")
        for joint in obj['joints']:
            if 'ankle' in joint['name']:
                print(f"{joint['name']}:")
                print(f"  use_position_for_anchor: {joint.get('use_position_for_anchor', 'MISSING')}")
                print(f"  type: {type(joint.get('use_position_for_anchor', 'MISSING'))}")
                print(f"  has voxel_bounds: {'voxel_bounds_min' in joint}")
                print(f"  position: {joint['position']}")
                if 'voxel_bounds_min' in joint:
                    print(f"  bounds: {joint['voxel_bounds_min']} - {joint['voxel_bounds_max']}")
                print()
        
        print("=== OTHER JOINTS FOR COMPARISON ===")
        for joint in obj['joints']:
            if joint['name'] in ['pelvis', 'left_hip', 'left_knee']:
                print(f"{joint['name']}:")
                print(f"  use_position_for_anchor: {joint.get('use_position_for_anchor', 'MISSING')}")
                print(f"  type: {type(joint.get('use_position_for_anchor', 'MISSING'))}")
                print(f"  has voxel_bounds: {'voxel_bounds_min' in joint}")
                print(f"  position: {joint['position']}")
                if 'voxel_bounds_min' in joint:
                    print(f"  bounds: {joint['voxel_bounds_min']} - {joint['voxel_bounds_max']}")
                print()
