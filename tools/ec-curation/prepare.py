import collections
import json
from pathlib import Path

import argparse
parser=argparse.ArgumentParser(description="Resolve saved public EC observations against exported game metadata; no network requests.")
parser.add_argument("work_dir",type=Path)
ROOT=parser.parse_args().work_dir.resolve()
metadata = json.loads((ROOT / 'metadata.json').read_text())
names = collections.defaultdict(list)
for item in metadata:
    names[item['name'].casefold()].append(item)
observations = json.loads((ROOT / 'observations.json').read_text())['rows']
pieces = {}
seeds = {}
for observation in observations:
    for item in observation['items']:
        pieces[item['search']] = item
        if observation['path'].startswith('/gearset/skallic-'):
            seeds[item['search']] = observation['path']
candidates, excluded = [], []
for piece in pieces.values():
    matches = names[piece['name'].casefold()]
    row = dict(piece)
    reason = None
    if len(matches) != 1:
        reason = 'unresolved exact name'
        row['matchingIds'] = [m['id'] for m in matches]
    else:
        facts = row['facts'] = matches[0]
        if facts['level'] == 1 or facts['ilvl'] == 1:
            reason = 'level-one glamour equipment'
        elif facts['rarity'] > 3 or (facts['rarity'] == 3 and not facts['marketable']):
            reason = 'rarity protection'
        elif facts['pvp'] or facts['armoire']:
            reason = 'PvP or armoire protection'
        elif facts['crafted']:
            row['acquisition'] = {'kind': 'Crafted', 'evidence': 'Installed Recipe sheet exact output ID'}
        elif row['search'] in seeds:
            row['acquisition'] = {'kind': 'Dungeon Drop', 'source': 'https://ffxiv.eorzeacollection.com' + seeds[row['search']]}
    if reason:
        row['excludedReason'] = reason
        excluded.append(row)
    else:
        candidates.append(row)
(ROOT / 'catalogue.json').write_text(json.dumps({'kind': 'catalogue', 'rows': candidates, 'excluded': excluded}, ensure_ascii=False), encoding='utf-8')
print(f'{len(pieces)} unique pieces; {len(candidates)} candidates; {len(excluded)} already protected/unresolved')
print('Acquisition ready', sum('acquisition' in c for c in candidates))
print('Unresolved names', [e['name'] for e in excluded if e['excludedReason'] == 'unresolved exact name'])
