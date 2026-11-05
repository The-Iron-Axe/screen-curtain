from pathlib import Path
import argparse, base64, ctypes, hashlib, json, lzma, os, struct, subprocess, time
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=['prepare', 'xz', 'lzms', 'disk'])
parser.add_argument('--preset', type=int, default=9)
parser.add_argument('--extreme', action='store_true')
parser.add_argument('--bcj', action='store_true')
parser.add_argument('--dict-mib', type=int, default=0)
parser.add_argument('--publish', default=str(root/'StylePublish'))
parser.add_argument('--lab', default=str(root/'ExtremeLab'))
args = parser.parse_args()
lab = Path(args.lab).resolve(); lab.mkdir(parents=True,exist_ok=True)
publish = Path(args.publish).resolve()
profile=json.loads((root/'compression-profile.json').read_text(encoding='utf-8'))
excluded={extension.lower() for extension in profile.get('excluded_extensions', [])}

if args.mode == 'prepare':
    version = ET.parse(root/'WinUI/ScreenCurtain.WinUI.csproj').findtext('PropertyGroup/Version')
    files = sorted((f for f in publish.rglob('*') if f.is_file() and f.suffix.lower() not in excluded),
                   key=lambda f: (f.suffix.lower(), f.name.lower(), f.relative_to(publish).as_posix().lower()))
    manifest = [f'SCMANIFEST\t1\tXZ-LZMA2\t{version}']
    start = time.perf_counter()
    with (lab/'contents.raw').open('wb') as output:
        for file in files:
            digest = hashlib.sha256()
            with file.open('rb') as source:
                while chunk := source.read(1024*1024):
                    digest.update(chunk); output.write(chunk)
            name = base64.b64encode(file.relative_to(publish).as_posix().encode('utf-8')).decode('ascii')
            manifest.append(f'{file.stat().st_size}\t{digest.hexdigest()}\t{name}')
    (lab/'manifest.scm').write_text('\n'.join(manifest)+'\n', encoding='utf-8')
    print(json.dumps({'files':len(files), 'bytes':(lab/'contents.raw').stat().st_size, 'seconds':round(time.perf_counter()-start,2)}))

elif args.mode == 'xz':
    preset = args.preset | (lzma.PRESET_EXTREME if args.extreme else 0)
    name = f'xz-{args.preset}' + ('-extreme' if args.extreme else '') + ('-bcj' if args.bcj else '') + (f'-dict{args.dict_mib}' if args.dict_mib else '') + '.xz'
    start = time.perf_counter()
    if args.bcj or args.dict_mib:
        options={'id':lzma.FILTER_LZMA2, 'preset':preset}
        if args.dict_mib: options['dict_size']=args.dict_mib*1024*1024
        filters=([{'id':lzma.FILTER_X86}] if args.bcj else [])+[options]
        encoder = lzma.LZMACompressor(format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC64, filters=filters)
    else: encoder = lzma.LZMACompressor(format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC64, preset=preset)
    with (lab/'contents.raw').open('rb') as source, (lab/name).open('wb') as output:
        while block := source.read(1024*1024): output.write(encoder.compress(block))
        output.write(encoder.flush())
    print(json.dumps({'algorithm':name, 'bytes':(lab/name).stat().st_size, 'seconds':round(time.perf_counter()-start,2)}))

elif args.mode == 'lzms':
    dll = ctypes.WinDLL('cabinet', use_last_error=True)
    dll.CreateCompressor.argtypes = [ctypes.c_uint32, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
    dll.Compress.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
    dll.CloseCompressor.argtypes = [ctypes.c_void_p]
    h = ctypes.c_void_p()
    if not dll.CreateCompressor(5, None, ctypes.byref(h)): raise ctypes.WinError(ctypes.get_last_error())
    start=time.perf_counter(); total=0; count=0; peak=0
    try:
        with (lab/'contents.raw').open('rb') as source, (lab/'lzms-8m.bin').open('wb') as output:
            while block := source.read(8*1024*1024):
                raw=ctypes.create_string_buffer(block); required=ctypes.c_size_t()
                dll.Compress(h,raw,len(block),None,0,ctypes.byref(required))
                buf=ctypes.create_string_buffer(required.value)
                if not dll.Compress(h,raw,len(block),buf,required.value,ctypes.byref(required)): raise ctypes.WinError(ctypes.get_last_error())
                output.write(struct.pack('<II',len(block),required.value)); output.write(buf.raw[:required.value]); count+=1; total+=required.value+8
    finally: dll.CloseCompressor(h)
    print(json.dumps({'algorithm':'lzms-8m', 'bytes':total, 'blocks':count, 'seconds':round(time.perf_counter()-start,2)}))

elif args.mode == 'disk':
    import shutil
    base=lab/'disk'/str(time.time_ns()); base.mkdir(parents=True,exist_ok=True)
    kernel=ctypes.WinDLL('kernel32', use_last_error=True)
    fn=kernel.GetCompressedFileSizeW; fn.argtypes=[ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_uint32)]; fn.restype=ctypes.c_uint32
    for algo in ['XPRESS4K','XPRESS8K','XPRESS16K','LZX']:
        dest=base/algo
        if not dest.exists(): shutil.copytree(publish,dest,ignore=lambda directory, names: [n for n in names if Path(n).suffix.lower() in excluded])
        start=time.perf_counter()
        cmd=['compact.exe','/C',f'/EXE:{algo}',f'/S:{dest}','/I','/Q']
        proc=subprocess.run(cmd,capture_output=True,text=True,errors='replace')
        logical=physical=0
        for file in dest.rglob('*'):
            if file.is_file():
                logical+=file.stat().st_size; hi=ctypes.c_uint32(); lo=fn(str(file),ctypes.byref(hi))
                if lo==0xffffffff and ctypes.get_last_error()!=0: raise ctypes.WinError(ctypes.get_last_error())
                physical+=(hi.value<<32)|lo
        print(json.dumps({'algorithm':algo,'returncode':proc.returncode,'logical':logical,'physical':physical,'seconds':round(time.perf_counter()-start,2),'output':proc.stdout[-300:]}),flush=True)
