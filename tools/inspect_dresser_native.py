"""Read-only verification of the ordinary dresser binding against a local PE executable.

Usage: python tools/inspect_dresser_native.py path/to/ffxiv_dx11.exe
Does not attach to FFXIV, invoke functions, or distribute game bytes.
"""
import hashlib
from pathlib import Path
import re
import struct
import sys

image = Path(sys.argv[1]).read_bytes()
pe = struct.unpack_from('<I', image, 0x3c)[0]
assert image[pe:pe+4] == b'PE\0\0', 'Not a PE executable'
machine, count = struct.unpack_from('<HH', image, pe+4)
assert machine == 0x8664, 'Expected x64 executable'
optional_size = struct.unpack_from('<H', image, pe+20)[0]
sections = pe+24+optional_size
text_sections = []
for i in range(count):
    section = sections+i*40
    _, rva, size, offset = struct.unpack_from('<IIII', image, section+8)
    flags = struct.unpack_from('<I', image, section+36)[0]
    if flags & 0x20000000:
        text_sections.append((rva, image[offset:offset+size]))
source = (Path(__file__).resolve().parents[1]/'Steward/Services/NativeDresserStore.cs').read_text()
matches = {}
for name in ('BodySignature', 'CallerSignature'):
    signature = re.search(name+r'="([^"]+)"', source).group(1)
    pattern = b''.join(b'.' if x == '??' else re.escape(bytes.fromhex(x)) for x in signature.split())
    hits = [(rva+m.start(), data, m.start()) for rva, data in text_sections
            for m in re.finditer(pattern, data, re.DOTALL)]
    assert len(hits) == 1, f'{name}: expected one match, found {len(hits)}'
    matches[name] = hits[0]
    print(f'{name}: RVA {hits[0][0]:#x}, unique')
caller, data, offset = matches['CallerSignature']
assert data[offset+22] == 0xe8, 'Expected direct call instruction'
target = caller+27+struct.unpack_from('<i', data, offset+23)[0]
assert target == matches['BodySignature'][0], 'Caller does not target verified member function'
print(f'Caller target: RVA {target:#x}, matches body')
print(f'Executable SHA256: {hashlib.sha256(image).hexdigest()}')
print('Static binding verification passed; this does not establish in-game acceptance.')
