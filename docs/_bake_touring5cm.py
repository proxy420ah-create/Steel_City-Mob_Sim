# _bake_touring5cm.py — bake the game's touring (1cm voxels) into a 5cm stasset for
# block-composite stamping. Majority vote per 5^3 cube over NONZERO cells (thin
# features survive — a solid-cell vote would erode pillars/trim). STAS binary:
# magic u32be, then u16le W,H,D, then W*H*D u16le x-major.
import struct, os
SRC = '../Assets/StreamingAssets/voxel_vehicles/vehicle_490_touring.stasset'
DST = '../Assets/StreamingAssets/voxel_vehicles/vehicle_490_touring_5cm.stasset'
K = 5
b = open(SRC, 'rb').read()
assert b[:4] == b'STAS'
W, H, D = struct.unpack('<HHH', b[6:12])
v = struct.unpack('<%dH' % (W * H * D), b[16:16 + W * H * D * 2])
W2, H2, D2 = W // K, H // K, D // K
out = [0] * (W2 * H2 * D2)
for z2 in range(D2):
  for y2 in range(H2):
    for x2 in range(W2):
      tally = {}
      for dz in range(K):
        for dy in range(K):
          for dx in range(K):
            i = (x2 * K + dx) + W * ((y2 * K + dy) + H * (z2 * K + dz))
            if v[i]: tally[v[i]] = tally.get(v[i], 0) + 1
      if tally:
        # most common id wins; ties → the larger tally count then lowest id (stable)
        out[x2 + W2 * (y2 + H2 * z2)] = max(tally, key=lambda k: (tally[k], -k))
nz = sum(1 for x in out if x)
hdr = b'STAS' + struct.pack('<H', 2) + struct.pack('<HHH', W2, H2, D2) + b'\x00' * 4   # v2 header = 16B like the source
open(DST, 'wb').write(hdr + struct.pack('<%dH' % len(out), *out))
print('baked', W2, 'x', H2, 'x', D2, '=', nz, 'voxels ->', DST)
