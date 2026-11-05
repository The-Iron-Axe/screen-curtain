from pathlib import Path
import argparse
import base64
import hashlib
import json
import lzma
import _lzma
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
project = ET.parse(root / 'WinUI' / 'ScreenCurtain.WinUI.csproj')
version = project.findtext('PropertyGroup/Version')
executable = project.findtext('PropertyGroup/AssemblyName') + '.exe'
parser = argparse.ArgumentParser()
parser.add_argument('--publish', help='Fresh dotnet publish output; normally use build_extreme.py instead.')
parser.add_argument('--output', default='ScreenCurtain-build.exe')
parser.add_argument('--build-dir', default='ExtremeBuild')
parser.add_argument('--profile', default='compression-profile.json')
parser.add_argument('--keep-payload', action='store_true', help='Keep the intermediate compressed archive for inspection.')
parser.add_argument('--benchmark', action='store_true', help='Run compression and storage comparisons for the current publish directory.')
args = parser.parse_args()
profile_path = (root / args.profile).resolve()
profile = json.loads(profile_path.read_text(encoding='utf-8'))
if profile['schema_version'] != 1 or profile['archive_format'] != 'xz-lzma2':
    raise SystemExit('Unsupported compression profile.')
if profile['cache_compression'] not in ('none','XPRESS4K','XPRESS8K','XPRESS16K','LZX'):
    raise SystemExit('Unsupported Windows cache compression algorithm.')
build = (root / args.build_dir).resolve()
build.mkdir(parents=True, exist_ok=True)
if args.benchmark:
    subprocess.run([sys.executable, str(root/'benchmark_suite.py')], check=True)
    raise SystemExit(0)
if not args.publish: raise SystemExit('Run build_extreme.py for a fresh publish, or pass --publish explicitly.')
publish = (root / args.publish).resolve()
if not publish.is_dir(): raise SystemExit(f'Publish directory not found: {publish}')

excluded = {extension.lower() for extension in profile.get('excluded_extensions', [])}
files = [f for f in publish.rglob('*') if f.is_file() and f.suffix.lower() not in excluded]
if profile.get('group_files_by_extension'):
    files.sort(key=lambda f: (f.suffix.lower(), f.name.lower(), f.relative_to(publish).as_posix().lower()))
else: files.sort(key=lambda f: f.relative_to(publish).as_posix().lower())
names = [f.relative_to(publish).as_posix() for f in files]
folded = [n.casefold() for n in names]
if len(set(folded)) != len(folded) or '.ready' in folded or any(n.startswith('.decoder/') or n == '.payload.xz' or n == '.stage-owner' for n in folded):
    raise SystemExit('Publish output contains duplicate or reserved launcher paths.')
if executable.casefold() not in folded: raise SystemExit('The project entrypoint is missing from the publish output.')
manifest = [f'SCMANIFEST\t1\tXZ-LZMA2\t{version}\t'+base64.b64encode(executable.encode('utf-8')).decode('ascii')]
payload = build/'payload.xz'
started = time.perf_counter()
preset = profile['xz_preset'] | (lzma.PRESET_EXTREME if profile['xz_extreme'] else 0)
if profile['xz_x86_filter'] or profile.get('xz_dictionary_mib', 0):
    options={'id': lzma.FILTER_LZMA2, 'preset': preset}
    if profile.get('xz_dictionary_mib', 0): options['dict_size'] = profile['xz_dictionary_mib'] * 1024 * 1024
    filters=([{'id': lzma.FILTER_X86}] if profile['xz_x86_filter'] else []) + [options]
    encoder = lzma.LZMACompressor(format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC64, filters=filters)
else: encoder = lzma.LZMACompressor(format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC64, preset=preset)
with payload.open('wb') as output:
    for index, file in enumerate(files, 1):
        digest = hashlib.sha256(); size = 0
        with file.open('rb') as source:
            while block := source.read(1024*1024):
                digest.update(block); size += len(block); output.write(encoder.compress(block))
        encoded = base64.b64encode(file.relative_to(publish).as_posix().encode('utf-8')).decode('ascii')
        manifest.append(f'{size}\t{digest.hexdigest()}\t{encoded}')
        if index % 100 == 0: print(f'Compressed {index}/{len(files)} files', flush=True)
    output.write(encoder.flush())
manifest_bytes = ('\n'.join(manifest)+'\n').encode('utf-8')
manifest_file = build/'manifest.scm'
manifest_file.write_bytes(manifest_bytes)
with payload.open('rb') as payload_file: payload_digest = hashlib.file_digest(payload_file, 'sha256').hexdigest()
manifest_digest = hashlib.sha256(manifest_bytes).hexdigest()
vendor = root/profile['decoder_directory']
resources = {'ScreenCurtain.Payload': payload, 'ScreenCurtain.Manifest': manifest_file,
    'ScreenCurtain.XzDecoder': vendor/'xzdec.exe', 'ScreenCurtain.LzmaLibrary': vendor/'liblzma.dll',
    'ScreenCurtain.VcRuntime': vendor/'vcruntime140.dll', 'ScreenCurtain.XzLicense': vendor/'COPYING',
    'ScreenCurtain.XzLicense0': vendor/'COPYING.0BSD'}
for path in resources.values():
    if not path.is_file(): raise SystemExit(f'Missing build resource: {path}')
parts = re.match(r'^(\d+)\.(\d+)\.(\d+)', version)
if not parts: raise SystemExit('Project version must start with major.minor.patch.')
assembly_version = '.'.join(parts.groups()) + '.0'
template = (root/'ExtremeLauncher.template.cs').read_text(encoding='utf-8')
for key,value in {'__ASSEMBLY_VERSION__':assembly_version,'__VERSION__':version,
    '__PAYLOAD_DIGEST__':payload_digest,'__MANIFEST_DIGEST__':manifest_digest,'__CACHE_COMPRESSION__':profile['cache_compression'],
    '__ARCHIVE_FORMAT__':'XZ-LZMA2'}.items():
    if any(c in value for c in '"\r\n\\'): raise SystemExit(f'Invalid launcher setting: {key}')
    template=template.replace(key,value)
launcher=build/'Launcher.cs'; launcher.write_text(template,encoding='utf-8-sig')
compiler=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
result=(root/args.output).resolve()
result.parent.mkdir(parents=True,exist_ok=True)
if result in {payload.resolve(), manifest_file.resolve(), launcher.resolve()}:
    raise SystemExit('Output must not overwrite an intermediate build file.')
command=[str(compiler),'/nologo','/target:winexe','/platform:x64','/optimize+',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll',
    f'/win32manifest:{root / "ScreenCurtain.manifest"}',f'/win32icon:{root / "ScreenCurtain.ico"}',
    *[f'/resource:{path},{name}' for name,path in resources.items()], f'/out:{result}',str(launcher)]
subprocess.run(command,check=True)
metadata={'version':version,'files':len(files),'payload_sha256':payload_digest,'manifest_sha256':manifest_digest,
    'payload_bytes':payload.stat().st_size,'exe_bytes':result.stat().st_size,'elapsed_seconds':round(time.perf_counter()-started,2),
    'profile':profile,'python_version':sys.version,'encoder_module_sha256':hashlib.sha256(Path(_lzma.__file__).read_bytes()).hexdigest(),
    'decoder_file_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in resources.values() if p.parent==vendor}}
(build/'build-info.json').write_text(json.dumps(metadata,indent=2,ensure_ascii=False),encoding='utf-8')
print(json.dumps(metadata,ensure_ascii=False))
if not args.keep_payload: payload.unlink()
