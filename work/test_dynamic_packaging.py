from pathlib import Path
import base64, hashlib, json, lzma, subprocess, sys, uuid, xml.etree.ElementTree as ET

root=Path(__file__).resolve().parent
test=root/'ExtremeLab'/('dynamic-'+uuid.uuid4().hex)
publish=test/'publish'; publish.mkdir(parents=True)
profile=json.loads((root/'compression-profile.json').read_text(encoding='utf-8'))
profile['xz_dictionary_mib']=1
profile['excluded_extensions']=[]
profile_path=test/'profile.json'; profile_path.write_text(json.dumps(profile),encoding='utf-8')
entry=ET.parse(root/'WinUI/ScreenCurtain.WinUI.csproj').findtext('PropertyGroup/AssemblyName')+'.exe'
(publish/entry).write_bytes(b'dummy test entrypoint')
(publish/'old-component.bin').write_bytes(b'first version')
(publish/'reference.pdb').write_bytes(b'test data with a normally excluded suffix')
(publish/'目录').mkdir(); (publish/'目录'/'新组件.dat').write_bytes('unicode data'.encode())

def pack_and_check():
    subprocess.run([sys.executable,str(root/'package_extreme.py'),'--publish',str(publish),
        '--profile',str(profile_path),'--output',str(test/'fixture.exe'),'--build-dir',str(test/'build'),'--keep-payload'],check=True)
    lines=(test/'build/manifest.scm').read_text(encoding='utf-8').splitlines()
    contents=lzma.decompress((test/'build/payload.xz').read_bytes())
    offset=0; names=[]
    for line in lines[1:]:
        length,digest,encoded=line.split('\t'); length=int(length); name=base64.b64decode(encoded).decode('utf-8')
        block=contents[offset:offset+length]; offset+=length; names.append(name)
        assert hashlib.sha256(block).hexdigest()==digest
        assert block==(publish/name).read_bytes()
    assert offset==len(contents)
    assert set(names)=={p.relative_to(publish).as_posix() for p in publish.rglob('*') if p.is_file()}
    return set(names)

before=pack_and_check()
(publish/'old-component.bin').unlink()
(publish/'new-component.bin').write_bytes(b'new version')
after=pack_and_check()
assert 'old-component.bin' in before and 'old-component.bin' not in after
assert 'new-component.bin' not in before and 'new-component.bin' in after
assert '目录/新组件.dat' in before and '目录/新组件.dat' in after
print('PASS: adding, removing and Unicode-named components automatically updates the manifest and compressed content.')
