"""Convert python-fsb5's vorbis_headers.py (MIT, Simon Pinfold / HearthSim) into a compact binary table.
Format: magic 'VHT1', uint32 count, then per entry: uint32 crc32, uint32 length, bytes. Little endian.
Usage: python -I convert_vorbis_headers.py <vorbis_headers.py> <out.bin>
"""
import sys, struct, ast
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding='utf-8').read()
# file is: lookup = { int: b'...' ... }
tree = ast.parse(text)
lookup = None
for node in tree.body:
    if isinstance(node, ast.Assign) and node.targets[0].id == 'lookup':
        lookup = ast.literal_eval(node.value)
assert lookup, 'lookup dict not found'
with open(dst, 'wb') as f:
    f.write(b'VHT1'); f.write(struct.pack('<I', len(lookup)))
    for crc in sorted(lookup):
        blob = lookup[crc]
        f.write(struct.pack('<II', crc & 0xFFFFFFFF, len(blob))); f.write(blob)
print('entries', len(lookup), 'bytes', sum(len(v) for v in lookup.values()))
