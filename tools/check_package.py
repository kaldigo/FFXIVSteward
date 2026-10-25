"""Check the release archive, dependencies and unchanged plugin identity."""
import argparse,hashlib,json,zipfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--tag',required=True);a=p.parse_args();root=Path(__file__).resolve().parents[1]
cfg=json.loads((root/'release-config.json').read_text());name=cfg['internal_name'];out=root/'artifacts'
feed=json.loads((out/'pluginmaster.json').read_text());assert len(feed)==1
m=feed[0];assert m['InternalName']==name and m['AssemblyVersion']==a.tag[1:]
url=f"https://github.com/{cfg['repository']}/releases/download/{a.tag}/{name}.zip"
assert all(m[k]==url for k in ['DownloadLinkInstall','DownloadLinkUpdate','DownloadLinkTesting'])
assert m['RepoUrl']=='https://github.com/'+cfg['repository']
archive=out/(name+'.zip');assert (out/'SHA256SUMS.txt').read_text().split()==[hashlib.sha256(archive.read_bytes()).hexdigest(),archive.name]
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    assert set(cfg['required_dlls']+list(cfg['licenses'].values())+['images/icon.png',name+'.json']).issubset(z.namelist())
    assert json.loads(z.read(name+'.json'))==m
    assert not any(x.endswith('.zip') or 'configuration' in x.lower() or x.startswith('/') or '..' in Path(x).parts for x in z.namelist())
    if name=='Accord':assert 'WrathCombo.dll' not in z.namelist()
    if name=='WrathCombo':assert 'Accord.dll' not in z.namelist() and 'WrathFork.dll' not in z.namelist()
print('Validated release archive:',name,a.tag)
