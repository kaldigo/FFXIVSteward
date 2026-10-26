"""Freeze reviewed observations; refuses incomplete or superseded measurement records."""
import datetime
import hashlib
import json
from pathlib import Path

import argparse
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("work_dir",type=Path)
HERE=parser.parse_args().work_dir.resolve()
REPO=Path(__file__).resolve().parents[2]


def read(name):
    return json.loads((HERE / (name + '.json')).read_text(encoding='utf-8'))


catalogue = read('catalogue')
counts = read('counts')
expected = {c['search'] for c in catalogue['rows'] if c.get('acquisition', {}).get('kind') != 'Special'}
measured = {r['search']: r for r in counts['rows']}
assert len(measured) == len(counts['rows']), 'Duplicate usage records need review'
assert expected <= measured.keys(), f'{len(expected - measured.keys())} candidates lack measured counts'
assert all(r.get('measurementVersion') == 2 for r in measured.values()), 'Early measurements must be rechecked'
for candidate in catalogue['rows']:
    candidate.pop('sourceError', None)
for candidate in catalogue['excluded']:
    if candidate['excludedReason'] == 'unresolved exact name' and candidate['search'].startswith('/glamours/fashion/'):
        candidate['excludedReason'] = 'fashion accessory outside inventory equipment'
observations = {r['path']: {'path': r['path'], 'pieces': sorted({i['search'] for i in r['items']})}
                for r in read('observations')['rows']}
metadata = read('metadata')
census = counts['census']
census['assessments'] = []
fashion = {'Angel Wings', 'Archangel Wings', 'Knapsack', 'Diabolos Wings', 'Fallen Angel Wings'}
for entry in census['items']:
    if entry['name'] in fashion:
        census['assessments'].append({'name': entry['name'], 'decision': 'non-equipment'})
        continue
    matches = [m for m in metadata if m['name'].casefold() == entry['name'].casefold()]
    assert len(matches) == 1, 'Unresolved official census equipment: ' + entry['name']
    m = matches[0]
    protected = (m['level'] == 1 or m['ilvl'] == 1 or m['armoire'] or m['pvp']
                 or m['rarity'] > 3 or (m['rarity'] == 3 and not m['marketable']))
    census['assessments'].append({'name': entry['name'], 'facts': m,
                                  'decision': 'already-protected' if protected else 'candidate'})
evidence = {
    'schemaVersion': 1,
    'checkedAt': datetime.datetime.now(datetime.timezone.utc).date().isoformat(),
    'minimumUses': 25,
    'gameData': {'language': 'English', 'equipmentRecords': len(metadata),
                 'sha256': hashlib.sha256((HERE / 'metadata.json').read_bytes()).hexdigest(),
                 'exporter': 'tools/curation-game-data/Program.cs'},
    'discovery': read('discovery'),
    'observations': sorted(observations.values(), key=lambda r: r['path']),
    'catalogue': catalogue,
    'usage': {'rows': sorted(measured.values(), key=lambda r: r['itemId'])},
    'census': census,
}
# Validate the complete evidence and forbid silent protection removals before replacing repository data.
import sys
sys.path.insert(0,str(REPO / 'tools'))
from build_common_glamour import build, preserve_existing
bundle=build(evidence)
preserve_existing(json.loads((REPO/'Steward/data/common-glamour.json').read_text(encoding='utf-8')),bundle)
path = REPO / 'docs/data/common-glamour-evidence.json' 
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'Wrote {path}, {len(measured)} complete usage records')
