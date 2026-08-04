# Steel Tide: Voxel Asset Studio
# stasset_io.py - Import/Export .stasset files
#
# Format versions:
#   v1: 16-byte header + voxel data (uint16, X-major / Fortran order).
#   v2: identical to v1, then an optional appended skeleton block:
#           b'SKEL' (4 bytes) + uint32 LE json_length + UTF-8 JSON payload.
#       The JSON payload holds {version, bones, joints, influence_map, attachments}.
#       v2 keeps the model and its rig in a single self-contained file.

import json
import struct
import numpy as np

SKELETON_BLOCK_MAGIC = b'SKEL'


def _json_default(obj):
    """Make numpy scalar/array types JSON-serializable."""
    if isinstance(obj, np.integer):
        return int(obj)
    if isinstance(obj, np.floating):
        return float(obj)
    if isinstance(obj, np.ndarray):
        return obj.tolist()
    raise TypeError(f"Object of type {type(obj).__name__} is not JSON serializable")


def _skeleton_has_content(skeleton):
    """True if the skeleton dict contains any bones or joints worth persisting."""
    if not skeleton:
        return False
    return bool(skeleton.get('bones')) or bool(skeleton.get('joints'))


def load_stasset(filepath):
    """
    Load .stasset file and return voxel data plus optional skeleton metadata.
    
    Args:
        filepath: Path to .stasset file
        
    Returns:
        tuple: (voxels, dims, skeleton) where voxels is a 3D numpy array (uint16),
               dims is (width, height, depth), and skeleton is a dict (v2 files
               with a rig) or None (v1 files / no rig).
    """
    with open(filepath, 'rb') as f:
        # Read header (16 bytes)
        magic = f.read(4)
        if magic != b'STAS':
            raise ValueError(f"Not a valid .stasset file! Magic: {magic}")
            
        version = struct.unpack('B', f.read(1))[0]
        if version not in (1, 2):
            raise ValueError(f"Unsupported version: {version}")
            
        f.read(1)  # flags (reserved)
        
        width = struct.unpack('<H', f.read(2))[0]   # Little-endian uint16
        height = struct.unpack('<H', f.read(2))[0]
        depth = struct.unpack('<H', f.read(2))[0]
        f.read(4)  # reserved
        
        # Read voxel data
        count = width * height * depth
        voxel_bytes = f.read(count * 2)
        
        if len(voxel_bytes) != count * 2:
            raise ValueError(f"Truncated file! Expected {count * 2} bytes, got {len(voxel_bytes)}")
        
        # Convert to numpy array (little-endian uint16) using X-major order
        voxels = np.frombuffer(voxel_bytes, dtype='<u2')
        voxels = voxels.reshape((width, height, depth), order='F')
        
        # CRITICAL: Make a writable C-contiguous copy for editing
        voxels = voxels.copy(order='C')
        
        # Read optional skeleton block (v2)
        skeleton = None
        if version >= 2:
            block_magic = f.read(4)
            if block_magic == SKELETON_BLOCK_MAGIC:
                json_len = struct.unpack('<I', f.read(4))[0]
                json_bytes = f.read(json_len)
                if len(json_bytes) != json_len:
                    raise ValueError(
                        f"Truncated skeleton block! Expected {json_len} bytes, got {len(json_bytes)}"
                    )
                skeleton = json.loads(json_bytes.decode('utf-8'))
        
    print(f"✅ Loaded {filepath}")
    print(f"   Dimensions: {width}×{height}×{depth}")
    print(f"   Total voxels: {count:,}")
    print(f"   Non-air voxels: {np.count_nonzero(voxels):,}")
    if skeleton is not None:
        print(f"   Skeleton: {len(skeleton.get('bones', []))} bones, "
              f"{len(skeleton.get('joints', []))} joints")
    
    return voxels, (width, height, depth), skeleton


def save_stasset(filepath, voxels, skeleton=None):
    """
    Save voxel data to .stasset file, optionally embedding skeleton metadata.
    
    Args:
        filepath: Output path
        voxels: 3D numpy array (uint16)
        skeleton: Optional dict with 'bones', 'joints', 'influence_map',
                  'attachments'. When provided (and non-empty) the file is
                  written as v2 with an appended skeleton block; otherwise v1.
    """
    dims = voxels.shape
    has_skeleton = _skeleton_has_content(skeleton)
    version = 2 if has_skeleton else 1
    
    # Pre-serialize the skeleton block so we can report an accurate file size.
    skeleton_bytes = b''
    if has_skeleton:
        payload = {
            'version': 2,
            'root_joint': skeleton.get('root_joint'),
            'bones': skeleton.get('bones', []),
            'joints': skeleton.get('joints', []),
            'influence_map': skeleton.get('influence_map', {}),
            'attachments': skeleton.get('attachments', []),
            'materials': skeleton.get('materials', {}),
            'ams': skeleton.get('ams', {}),
        }
        json_bytes = json.dumps(payload, default=_json_default).encode('utf-8')
        skeleton_bytes = (
            SKELETON_BLOCK_MAGIC
            + struct.pack('<I', len(json_bytes))
            + json_bytes
        )
    
    with open(filepath, 'wb') as f:
        # Header (16 bytes)
        f.write(b'STAS')                       # Magic
        f.write(struct.pack('B', version))     # Version (1 = voxels only, 2 = + skeleton)
        f.write(struct.pack('B', 0))           # Flags (reserved)
        f.write(struct.pack('<H', dims[0]))    # Width
        f.write(struct.pack('<H', dims[1]))    # Height
        f.write(struct.pack('<H', dims[2]))    # Depth
        f.write(struct.pack('<I', 0))          # Reserved (4 bytes)
        
        # Voxel data (little-endian uint16) with X-major ordering
        voxel_array = np.asarray(voxels, dtype='<u2', order='F')
        voxel_data = voxel_array.ravel(order='F')
        f.write(voxel_data.tobytes())
        
        # Optional skeleton block (v2)
        if skeleton_bytes:
            f.write(skeleton_bytes)
    
    count = dims[0] * dims[1] * dims[2]
    total_bytes = 16 + len(voxel_data.tobytes()) + len(skeleton_bytes)
    print(f"✅ Saved {filepath} (v{version})")
    print(f"   Dimensions: {dims[0]}×{dims[1]}×{dims[2]}")
    print(f"   Total voxels: {count:,}")
    if has_skeleton:
        print(f"   Skeleton: {len(payload['bones'])} bones, {len(payload['joints'])} joints, "
              f"{len(payload['influence_map'])} influenced voxels")
    print(f"   File size: {total_bytes:,} bytes")
