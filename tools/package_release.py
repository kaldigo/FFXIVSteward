"""Package one validated plugin and its immutable source release feed."""
import argparse,hashlib,json,re,struct,zipfile
from pathlib import Path

def package(root,tag,changelog='Validated source update. Development preview; native acceptance remains separate.'):
    if not re.fullmatch(r'v\d+\.\d+\.\d+\.\d+',tag):raise ValueError('Expected a four-part immutable release tag')
    cfg=json.loads((root/'release-config.json').read_text());name=cfg['internal_name'];build=root/cfg['build']
    manifest=json.loads((build/(name+'.json')).read_text(encoding='utf-8-sig'))
    if manifest['InternalName']!=name or manifest['AssemblyVersion']!=tag[1:] or manifest['DalamudApiLevel']!=15:raise ValueError('Built manifest identity/version/API differs from release plan')
    for dll in cfg['required_dlls']:
        if not (build/dll).is_file():raise ValueError('Missing runtime dependency: '+dll)
    icon=root/'assets/icon.png';raw=icon.read_bytes()
    if raw[:8]!=b'\x89PNG\r\n\x1a\n' or any(n<1 or n>512 for n in struct.unpack('>II',raw[16:24])):raise ValueError('Invalid plugin icon')
    manifest.update(RepoUrl='https://github.com/'+cfg['repository'],IconUrl='https://raw.githubusercontent.com/'+cfg['repository']+'/main/assets/icon.png',Changelog=changelog)
    url='https://github.com/'+cfg['repository']+'/releases/download/'+tag+'/'+name+'.zip'
    manifest.update(DownloadLinkInstall=url,DownloadLinkUpdate=url,DownloadLinkTesting=url)
    out=root/'artifacts';out.mkdir(exist_ok=True);archive=out/(name+'.zip')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        # Explicit dependencies exclude stale incremental outputs from removed features.
        for dll in cfg['required_dlls']:z.write(build/dll,dll)
        deps=build/(name+'.deps.json')
        if deps.is_file():z.write(deps,deps.name)
        z.writestr(name+'.json',json.dumps(manifest,indent=2)+'\n')
        z.write(icon,'images/icon.png')
        for source,target in cfg['licenses'].items():z.write(root/source,target)
        z.write(root/'docs/publishing.md','PUBLISHING.md')
    feed=json.dumps([manifest],indent=2)+'\n'
    (out/'pluginmaster.json').write_text(feed,encoding='utf-8')
    (root/'pluginmaster.json').write_text(feed,encoding='utf-8')
    (out/'SHA256SUMS.txt').write_text(hashlib.sha256(archive.read_bytes()).hexdigest()+'  '+archive.name+'\n')
    print('Packaged',name,manifest['AssemblyVersion']);return archive
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--tag',required=True);p.add_argument('--changelog-file');a=p.parse_args()
    package(Path(__file__).resolve().parents[1],a.tag,Path(a.changelog_file).read_text(encoding='utf-8-sig').strip() if a.changelog_file else 'Validated source update. Development preview; native acceptance remains separate.')
