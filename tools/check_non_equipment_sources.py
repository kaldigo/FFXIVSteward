"""Validate frozen duty evidence against the bundled non-equipment source index."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
evidence = json.loads((root / 'docs/data/non-equipment-duty-evidence.json').read_text(encoding='utf-8'))
index = json.loads((root / 'Steward/data/non-equipment-duty-sources.json').read_text(encoding='utf-8'))

def classify(route):
    if route['alliance'] or route['maxPlayers'] == 24:
        return 5
    if 'Savage' in route['duty']:
        return 6
    if route['highEnd'] or 'Coil of Bahamut' in route['duty'] or route['type'] in (4, 28):
        return 7
    return {2: 3, 5: 4}.get(route['type'], 2)

expected = []
seen = set()
for item in evidence['items']:
    assert item['item'] > 0 and item['item'] not in seen
    seen.add(item['item'])
    assert item['routes']
    kinds = []
    for route in item['routes']:
        assert route['contentFinderCondition'] > 0 and route['duty']
        kind = classify(route)
        assert kind == route['kind'], (item['item'], route)
        kinds.append(kind)
    kind = max(kinds, key=lambda k: 3 if k in (2, 5, 6, 7) else 2)
    assert kind == item['kind']
    expected.append([item['item'], kind])
assert sorted(expected) == index['items']
assert evidence['revision'] in index['provenance']
print(f'Verified {len(expected)} non-equipment duty sources against frozen acquisition evidence.')
